using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Renci.SshNet;

namespace ShellTree {
    public sealed class Connection {
        public string Host;
        public string User;
        [ScriptIgnore] public string Password;
        public int Port = 22;
        public string Root = "/";
        public int Depth = 3;
        public void Validate() {
            if (string.IsNullOrWhiteSpace(Host) || !Regex.IsMatch(Host, @"\A[A-Za-z0-9:][A-Za-z0-9.:%_-]*\z")) throw new ArgumentException("Enter a host name, IPv4 address, or unbracketed IPv6 address.");
            if (string.IsNullOrWhiteSpace(User) || User.IndexOf('\0') >= 0 || User.IndexOf('\n') >= 0) throw new ArgumentException("Enter an SSH username.");
            if (Port < 1 || Port > 65535 || Depth < 0 || Depth > 20) throw new ArgumentException("Invalid port or scan depth.");
            if (string.IsNullOrEmpty(Root) || !Root.StartsWith("/", StringComparison.Ordinal) || Root.IndexOf('\0') >= 0) throw new ArgumentException("Enter an absolute Linux path, such as /home.");
        }
    }
    public static class SshScanner {
        public static string ShellQuote(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }
        public static string Script(Connection c) {
            c.Validate();
            return "export LC_ALL=C\ncd " + ShellQuote(c.Root) + " || exit 1\n" +
                "find . -maxdepth " + (c.Depth + 1) + " -printf '%y\\0%p\\0%s\\0%T@\\0'\n";
        }
        internal static async Task<byte[]> ReadLimited(Stream stream, int limit, CancellationToken token) {
            using (var result = new MemoryStream()) {
                byte[] buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0) {
                    if (result.Length + count > limit) throw new InvalidDataException("Remote output exceeds the size limit.");
                    result.Write(buffer, 0, count);
                }
                return result.ToArray();
            }
        }
        public static Task<Snapshot> Scan(Connection c, CancellationToken token, Func<string, string, bool> verifyHost) {
            c.Validate();
            if (c.Password == null) throw new ArgumentException("Enter your SSH password.");
            if (verifyHost == null) throw new ArgumentNullException("verifyHost");
            return Task.Run(async delegate {
                using (var auth = new PasswordAuthenticationMethod(c.User, c.Password))
                using (var client = new SshClient(new ConnectionInfo(c.Host, c.Port, c.User, auth) { Timeout = TimeSpan.FromSeconds(15) })) {
                    Exception hostError = null;
                    client.HostKeyReceived += delegate(object sender, Renci.SshNet.Common.HostKeyEventArgs e) {
                        e.CanTrust = false;
                        try { e.CanTrust = verifyHost(e.HostKeyName, HostKeys.Fingerprint(e.HostKey)); }
                        catch (Exception ex) { hostError = ex; }
                    };
                    client.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    try { await client.ConnectAsync(token).ConfigureAwait(false); }
                    catch { if (hostError != null) throw hostError; throw; }
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    using (var command = client.CreateCommand("sh -c " + ShellQuote(Script(c)))) {
                        timeout.CancelAfter(TimeSpan.FromMinutes(2));
                        command.CommandTimeout = TimeSpan.FromMinutes(2);
                        Task execution = command.ExecuteAsync(timeout.Token);
                        Task<byte[]> stdout = ReadLimited(command.OutputStream, Snapshots.MaxBytes, timeout.Token);
                        Task<byte[]> stderr = ReadLimited(command.ExtendedOutputStream, 64 * 1024, timeout.Token);
                        Exception failure = null;
                        try {
                            var pending = new System.Collections.Generic.List<Task> { execution, stdout, stderr };
                            while (pending.Count > 0) {
                                Task completed = await Task.WhenAny(pending).ConfigureAwait(false);
                                await completed.ConfigureAwait(false); pending.Remove(completed);
                            }
                            timeout.Token.ThrowIfCancellationRequested();
                            if (command.ExitStatus != 0 || !string.IsNullOrEmpty(command.ExitSignal))
                                throw new IOException("SSH scan failed; the previous listing was retained.\n" + Encoding.UTF8.GetString(stderr.Result).Trim());
                            return new Snapshot { Version = 1, Host = c.Host, User = c.User, Port = c.Port, Root = c.Root,
                                Depth = c.Depth, ScannedUtc = DateTime.UtcNow.ToString("o"), Entries = Snapshots.Parse(stdout.Result) };
                        } catch (Exception ex) { failure = ex; }
                        bool timedOut = timeout.IsCancellationRequested;
                        timeout.Cancel();
                        try { client.Disconnect(); } catch {}
                        try { await Task.WhenAll(execution, stdout, stderr).ConfigureAwait(false); } catch {}
                        token.ThrowIfCancellationRequested();
                        if (timedOut) throw new TimeoutException("Scan exceeded two minutes. Try a smaller root or depth.");
                        throw failure;
                    }
                }
            }, token);
        }
    }
}

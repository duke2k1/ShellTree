using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShellTree {
    public sealed class Connection {
        public string Host;
        public string User;
        public int Port = 22;
        public string Root = "/";
        public int Depth = 3;
        public void Validate() {
            if (string.IsNullOrWhiteSpace(Host) || !Regex.IsMatch(Host, @"\A[A-Za-z0-9:][A-Za-z0-9.:%_-]*\z")) throw new ArgumentException("Enter a host name, IPv4 address, or unbracketed IPv6 address.");
            if (string.IsNullOrWhiteSpace(User) || !Regex.IsMatch(User, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z")) throw new ArgumentException("Enter a valid SSH username.");
            if (Port < 1 || Port > 65535 || Depth < 0 || Depth > 20) throw new ArgumentException("Invalid port or scan depth.");
            if (string.IsNullOrEmpty(Root) || !Root.StartsWith("/", StringComparison.Ordinal) || Root.IndexOf('\0') >= 0) throw new ArgumentException("Enter an absolute Linux path, such as /home.");
        }
    }
    public static class SshScanner {
        public static string ShellQuote(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }
        public static string Script(Connection c) {
            c.Validate();
            // NUL framing preserves whitespace, quotes and newlines. Never parse ls -l.
            // GNU find does not follow symlinks by default.
            return "export LC_ALL=C\ncd " + ShellQuote(c.Root) + " || exit 1\n" +
                "find . -maxdepth " + (c.Depth + 1) + " -printf '%y\\0%p\\0%s\\0%T@\\0'\n";
        }
        static async Task<byte[]> ReadLimited(Stream stream, int limit, CancellationToken token) {
            using (var result = new MemoryStream()) {
                byte[] buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0) {
                    if (result.Length + count > limit) throw new InvalidDataException("Remote output exceeds the size limit.");
                    result.Write(buffer, 0, count);
                }
                return result.ToArray();
            }
        }
        public static async Task<Snapshot> Scan(Connection c, CancellationToken token) {
            string script = Script(c);
            string ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            if (!File.Exists(ssh)) throw new FileNotFoundException("Install the Windows OpenSSH Client optional feature first.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var process = new Process()) {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                process.StartInfo = new ProcessStartInfo {
                    FileName = ssh,
                    Arguments = "-T -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=2 -o ClearAllForwardings=yes -p " + c.Port + " -l " + c.User + " " + c.Host + " sh -s",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                using (timeout.Token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) {} })) {
                    // Read both streams concurrently so neither can block the SSH process.
                    Task<byte[]> stdout = ReadLimited(process.StandardOutput.BaseStream, Snapshots.MaxBytes, timeout.Token);
                    Task<byte[]> stderr = ReadLimited(process.StandardError.BaseStream, 64 * 1024, timeout.Token);
                    try {
                        byte[] input = new UTF8Encoding(false).GetBytes(script);
                        await process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length, timeout.Token).ConfigureAwait(false);
                        process.StandardInput.Close();
                        // Observe a stream failure promptly, including output-limit violations.
                        Task first = await Task.WhenAny(stdout, stderr).ConfigureAwait(false);
                        await first.ConfigureAwait(false);
                        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        string error = Encoding.UTF8.GetString(stderr.Result).Trim();
                        if (process.ExitCode != 0) throw new IOException("SSH scan failed; no snapshot was saved.\n" + error);
                        return new Snapshot { Version = 1, Host = c.Host, User = c.User, Port = c.Port, Root = c.Root,
                            Depth = c.Depth, ScannedUtc = DateTime.UtcNow.ToString("o"), Entries = Snapshots.Parse(stdout.Result) };
                    } catch {
                        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) {}
                        try { Task.WaitAll(stdout, stderr); } catch {}
                        if (timeout.IsCancellationRequested) {
                            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                            throw new TimeoutException("Scan exceeded two minutes. Try a smaller root or depth.");
                        }
                        throw;
                    }
                }
            }
        }
    }
}



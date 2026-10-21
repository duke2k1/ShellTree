using System;
using System.IO;
using System.Text;
using System.Diagnostics;
namespace ShellTree {
    static class Tests {
        static int count;
        static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        static void Reject(Action action, string name) { try { action(); } catch { count++; return; } throw new Exception("Did not reject: " + name); }
        static byte[] Data(string s) { return Encoding.UTF8.GetBytes(s); }
        static string Record(string kind, string path, string size) { return kind + "\0" + path + "\0" + size + "\0" + "1700000000.25\0"; }
        static int Main(string[] args) {
            try {
                if (args.Length == 1) return Integration(int.Parse(args[0]));
                string basic = Record("d", ".", "4096") + Record("f", "./hello\n's 世界.txt", "12") + Record("l", "./loop", "1") + Record("d", "./empty", "4096");
                var entries = Snapshots.Parse(Data(basic));
                Check(entries.Count == 4 && entries[1].Name == "hello\n's 世界.txt", "NUL framing");
                Check(entries[1].Parent == "." && entries[2].Kind == "l", "parents and symlinks");
                Reject(() => Snapshots.Parse(Data(basic.TrimEnd('\0'))), "truncated output");
                Reject(() => Snapshots.Parse(Data(Record("d", ".", "1") + Record("f", "./../x", "1"))), "traversal");
                Reject(() => Snapshots.Parse(Data(Record("d", ".", "1") + Record("f", "./missing/x", "1"))), "orphan");
                Reject(() => Snapshots.Parse(Data(Record("d", ".", "1") + Record("d", ".", "1"))), "duplicate");
                Reject(() => Snapshots.Parse(new byte[] { 255, 0 }), "invalid UTF8");
                Reject(() => new Connection { Host = "-oProxyCommand=bad", User = "user" }.Validate(), "option injection");
                Reject(() => new Connection { Host = "server", User = "user\nbad" }.Validate(), "invalid username");
                var c = new Connection { Host = "::1", User = "user", Root = "/a'b;$(touch bad)", Depth = 0 };
                c.Validate();
                Check(SshScanner.Script(c).Contains("cd '/a'\"'\"'b;$(touch bad)'"), "shell escaping");
                Check(SshScanner.Script(c).Contains("-maxdepth 1"), "depth zero");
                var secret = new Connection { Host = "server", User = "user", Password = "never-save-this" };
                Check(!new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(secret).Contains("never-save-this"), "password excluded from JSON");
                using (var stream = new MemoryStream(new byte[10])) {
                    Reject(() => SshScanner.ReadLimited(stream, 4, System.Threading.CancellationToken.None).GetAwaiter().GetResult(), "stream output limit");
                }
                string trustFile = Path.Combine(Path.GetTempPath(), "ShellTree-trust-" + Guid.NewGuid().ToString("N") + ".json");
                try {
                    var trust = new HostKeys(trustFile);
                    Check(!trust.Verify("host", 22, "ssh-ed25519", "SHA256:test", () => false) && !File.Exists(trustFile), "reject unknown host");
                    Check(trust.Verify("host", 22, "ssh-ed25519", "SHA256:test", () => true), "approve unknown host");
                    Check(new HostKeys(trustFile).Verify("HOST", 22, "ssh-ed25519", "SHA256:test", () => { throw new Exception("must not prompt"); }), "persist known host");
                    Reject(() => trust.Verify("host", 22, "ssh-ed25519", "SHA256:changed", () => true), "changed host blocked");
                    Check(!trust.Verify("host", 2222, "ssh-ed25519", "SHA256:test", () => false), "trust scoped to port");
                } finally { if (File.Exists(trustFile)) File.Delete(trustFile); }
                var s = new Snapshot { Version = 1, Root = "/", Depth = 0, Entries = entries, Host = "test", User = "test", Port = 22 };
                string temp = Path.Combine(Path.GetTempPath(), "ShellTree-test-" + Guid.NewGuid().ToString("N") + ".json");
                try {
                    Snapshots.Save(s, temp); Check(Snapshots.Load(temp).Entries[1].Path == entries[1].Path, "JSON roundtrip");
                    Snapshots.Save(s, temp); Check(Snapshots.Load(temp).Entries.Count == 4, "atomic replace");
                    s.Version = 2; Reject(() => Snapshots.Save(s, temp), "schema version");
                    Check(Snapshots.Load(temp).Version == 1, "failed save preserves cache");
                } finally { if (File.Exists(temp)) File.Delete(temp); }
                // Optional local GNU find integration using the Git for Windows shell.
                string bash = @"C:\Program Files\Git\bin\bash.exe";
                if (File.Exists(bash)) {
                    string fixture = Path.Combine(Path.GetTempPath(), "ShellTree quote's " + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(Path.Combine(fixture, "empty"));
                    File.WriteAllText(Path.Combine(fixture, "space ' and 世界.txt"), "hello");
                    try {
                        string posix = "/" + char.ToLowerInvariant(fixture[0]) + fixture.Substring(2).Replace('\\', '/');
                        var config = new Connection { Host = "local", User = "test", Root = posix, Depth = 0 };
                        using (var process = new Process()) {
                            process.StartInfo = new ProcessStartInfo(bash, "--noprofile --norc -s") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                            process.Start();
                            byte[] script = Encoding.UTF8.GetBytes(SshScanner.Script(config));
                            process.StandardInput.BaseStream.Write(script, 0, script.Length); process.StandardInput.Close();
                            using (var output = new MemoryStream()) {
                                process.StandardOutput.BaseStream.CopyTo(output);
                                string error = process.StandardError.ReadToEnd(); process.WaitForExit();
                                Check(process.ExitCode == 0, "real find execution: " + error);
                                var found = Snapshots.Parse(output.ToArray());
                                Check(found.Count == 3, "real find entry count");
                                Check(found.Exists(e => e.Path == "./space ' and 世界.txt" && e.Size == 5), "real find quoted/unicode filename");
                            }
                        }
                    } finally { Directory.Delete(fixture, true); }
                }
                Console.WriteLine("Passed " + count + " checks."); return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static int Integration(int port) {
            var c = new Connection { Host = "127.0.0.1", Port = port, User = "shelltree-test", Password = "test-only-password", Root = "/test", Depth = 1 };
            bool checkedHost = false;
            var s = SshScanner.Scan(c, System.Threading.CancellationToken.None, (algorithm, fingerprint) => {
                checkedHost = !string.IsNullOrEmpty(algorithm) && fingerprint.StartsWith("SHA256:"); return checkedHost;
            }).GetAwaiter().GetResult();
            Check(checkedHost && s.Entries.Count == 3, "integrated SSH password scan");
            Check(s.Entries.Exists(e => e.Path == "./quote's 世界.txt"), "SSH preserves Unicode names");
            c.Password = "wrong";
            Reject(() => SshScanner.Scan(c, System.Threading.CancellationToken.None, (a, f) => true).GetAwaiter().GetResult(), "wrong password");
            c.Password = "test-only-password";
            Reject(() => SshScanner.Scan(c, System.Threading.CancellationToken.None, (a, f) => false).GetAwaiter().GetResult(), "untrusted server");
            c.Root = "/denied";
            Reject(() => SshScanner.Scan(c, System.Threading.CancellationToken.None, (a, f) => true).GetAwaiter().GetResult(), "remote failure");
            c.Root = "/truncated";
            Reject(() => SshScanner.Scan(c, System.Threading.CancellationToken.None, (a, f) => true).GetAwaiter().GetResult(), "truncated SSH listing");
            c.Root = "/slow";
            using (var cancel = new System.Threading.CancellationTokenSource(1000)) {
                var timer = Stopwatch.StartNew();
                bool cancelled = false;
                try { SshScanner.Scan(c, cancel.Token, (a, f) => true).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && timer.Elapsed.TotalSeconds < 10, "cancel SSH scan promptly");
            }
            Console.WriteLine("Passed " + count + " SSH integration checks."); return 0;
        }
    }
}

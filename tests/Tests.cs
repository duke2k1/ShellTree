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
        static int Main() {
            try {
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
                Reject(() => new Connection { Host = "server", User = "user & bad" }.Validate(), "username injection");
                var c = new Connection { Host = "::1", User = "user", Root = "/a'b;$(touch bad)", Depth = 0 };
                c.Validate();
                Check(SshScanner.Script(c).Contains("cd '/a'\"'\"'b;$(touch bad)'"), "shell escaping");
                Check(SshScanner.Script(c).Contains("-maxdepth 1"), "depth zero");
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
    }
}

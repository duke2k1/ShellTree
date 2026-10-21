using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ShellTree {
    public sealed class Entry {
        public string Path { get; set; }
        public string Kind { get; set; }
        public long Size { get; set; }
        public decimal ModifiedUnix { get; set; }
        public string Name { get { return Path == "." ? "." : Path.Substring(Path.LastIndexOf('/') + 1); } }
        public string Parent { get { int i = Path.LastIndexOf('/'); return i < 0 ? "" : i == 1 ? "." : Path.Substring(0, i); } }
    }
    public sealed class Snapshot {
        public int Version { get; set; }
        public string Host { get; set; }
        public string User { get; set; }
        public int Port { get; set; }
        public string Root { get; set; }
        public string ScannedUtc { get; set; }
        public int Depth { get; set; }
        public List<Entry> Entries { get; set; }
    }
    public static class Snapshots {
        public const int MaxBytes = 32 * 1024 * 1024;
        public const int MaxEntries = 100000;
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 64 }; }
        public static List<Entry> Parse(byte[] bytes) {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Listing exceeds 32 MB.");
            string text = new UTF8Encoding(false, true).GetString(bytes);
            string[] fields = text.Split('\0');
            if (fields.Length < 5 || fields[fields.Length - 1] != "" || (fields.Length - 1) % 4 != 0)
                throw new InvalidDataException("Incomplete or unsupported remote listing.");
            var entries = new List<Entry>();
            for (int i = 0; i < fields.Length - 1; i += 4) {
                long size; decimal modified;
                if (!long.TryParse(fields[i + 2], NumberStyles.None, CultureInfo.InvariantCulture, out size) ||
                    !decimal.TryParse(fields[i + 3], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out modified))
                    throw new InvalidDataException("Invalid remote metadata.");
                entries.Add(new Entry { Kind = fields[i], Path = fields[i + 1], Size = size, ModifiedUnix = modified });
            }
            ValidateEntries(entries);
            return entries;
        }
        static void ValidateEntries(List<Entry> entries) {
            if (entries == null || entries.Count == 0 || entries.Count > MaxEntries) throw new InvalidDataException("Snapshot must contain 1–100,000 entries.");
            var paths = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var e in entries) {
                if (e == null || e.Path == null || e.Kind == null || e.Kind.Length != 1 || "df lbcps".Replace(" ", "").IndexOf(e.Kind, StringComparison.Ordinal) < 0 || e.Size < 0)
                    throw new InvalidDataException("Invalid entry.");
                if (e.Path != "." && (!e.Path.StartsWith("./", StringComparison.Ordinal) || e.Path.Substring(2).Split('/').Any(p => p == "" || p == "." || p == "..")))
                    throw new InvalidDataException("Invalid snapshot path.");
                if (e.Path.IndexOf('\0') >= 0 || paths.ContainsKey(e.Path)) throw new InvalidDataException("Duplicate or invalid path.");
                paths.Add(e.Path, e);
            }
            Entry root;
            if (!paths.TryGetValue(".", out root) || root.Kind != "d") throw new InvalidDataException("Missing root directory.");
            foreach (var e in entries.Where(e => e.Path != ".")) {
                Entry parent;
                if (!paths.TryGetValue(e.Parent, out parent) || parent.Kind != "d") throw new InvalidDataException("Missing parent directory.");
            }
        }
        public static void Validate(Snapshot s) {
            if (s == null || s.Version != 1 || string.IsNullOrWhiteSpace(s.Root) || !s.Root.StartsWith("/", StringComparison.Ordinal) || s.Depth < 0 || s.Depth > 20)
                throw new InvalidDataException("Unsupported or invalid ShellTree snapshot.");
            ValidateEntries(s.Entries);
        }
        public static Snapshot Load(string path) {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("JSON exceeds 32 MB.");
            var s = Serializer().Deserialize<Snapshot>(File.ReadAllText(path, Encoding.UTF8));
            Validate(s); return s;
        }
        public static void Save(Snapshot s, string path) {
            Validate(s);
            string json = Serializer().Serialize(s);
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("JSON exceeds 32 MB.");
            string full = System.IO.Path.GetFullPath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

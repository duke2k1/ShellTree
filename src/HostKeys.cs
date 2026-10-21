using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ShellTree {
    public sealed class HostKeys {
        readonly string path;
        public HostKeys(string path) { this.path = path; }
        public static string Fingerprint(byte[] key) {
            using (var sha = SHA256.Create()) return "SHA256:" + Convert.ToBase64String(sha.ComputeHash(key)).TrimEnd('=');
        }
        public bool Verify(string host, int port, string algorithm, string fingerprint, Func<bool> approve) {
            var serializer = new JavaScriptSerializer();
            var keys = File.Exists(path) ? serializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : new Dictionary<string, string>();
            if (keys == null) throw new InvalidDataException("The trusted-hosts file is invalid.");
            string id = host.ToLowerInvariant() + ":" + port;
            string value = algorithm + " " + fingerprint;
            string known;
            if (keys.TryGetValue(id, out known)) {
                if (known != value) throw new IOException("The SSH host key for " + id + " has changed. Connection blocked. Verify the server identity before removing its entry from trusted-hosts.json.");
                return true;
            }
            if (!approve()) return false;
            keys[id] = value;
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, serializer.Serialize(keys), new UTF8Encoding(false));
                if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
    }
}

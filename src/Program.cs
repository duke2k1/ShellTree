using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ShellTree {
    static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BrowserForm(args.Length > 0 ? args[0] : null));
        }
    }
    public sealed class BrowserForm : Form {
        readonly TextBox host = new TextBox { Width = 160 };
        readonly TextBox user = new TextBox { Width = 100 };
        readonly TextBox root = new TextBox { Width = 220, Text = "/home" };
        readonly NumericUpDown port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22, Width = 65 };
        readonly NumericUpDown depth = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 3, Width = 45 };
        readonly Button scan = new Button { Text = "Connect / Scan", AutoSize = true };
        readonly Button cancel = new Button { Text = "Cancel", Enabled = false };
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        readonly ListView files = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        readonly TextBox address = new TextBox { ReadOnly = true, Width = 650 };
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Text = "Connect over SSH or open a saved JSON snapshot." };
        readonly Stack<string> history = new Stack<string>();
        readonly Dictionary<string, TreeNode> nodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
        readonly Button back = new Button { Text = "Back", Enabled = false };
        readonly Button open = new Button { Text = "Open JSON", AutoSize = true };
        readonly Button save = new Button { Text = "Save JSON As", AutoSize = true };
        Snapshot snapshot;
        Dictionary<string, List<Entry>> children;
        string current = ".";
        bool selecting;
        CancellationTokenSource cancellation;
        readonly string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellTree", "last.snapshot.json");

        public BrowserForm(string initialFile) {
            Text = "ShellTree — SSH directory browser";
            Size = new Size(1180, 760); MinimumSize = new Size(960, 560);
            Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterScreen;
            var header = new Label { Text = "ShellTree    |    SSH folders, familiar view", Dock = DockStyle.Top, Height = 58,
                BackColor = Color.FromArgb(18, 35, 54), ForeColor = Color.FromArgb(92, 218, 231), Font = new Font("Segoe UI", 19, FontStyle.Bold), Padding = new Padding(16, 10, 0, 0) };
            var connection = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(8), AutoScroll = true };
            Add(connection, "Host", host); Add(connection, "User", user); Add(connection, "Port", port); Add(connection, "Root", root); Add(connection, "Depth", depth);
            connection.Controls.Add(scan); connection.Controls.Add(cancel);
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8) };
            var up = new Button { Text = "Up" };
            var help = new Button { Text = "SSH setup", AutoSize = true };
            nav.Controls.AddRange(new Control[] { back, up, address, open, save, help });
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280, Width = 1100 };
            split.Panel1.Controls.Add(tree); split.Panel2.Controls.Add(files);
            files.Columns.Add("Name", 340); files.Columns.Add("Type", 110); files.Columns.Add("Bytes", 110); files.Columns.Add("Modified (local)", 170);
            var strip = new StatusStrip(); strip.Items.Add(status);
            Controls.Add(split); Controls.Add(nav); Controls.Add(connection); Controls.Add(header); Controls.Add(strip);
            scan.Click += async delegate {
                try {
                    var c = new Connection { Host = host.Text.Trim(), User = user.Text.Trim(), Port = (int)port.Value, Root = root.Text, Depth = (int)depth.Value };
                    c.Validate();
                    cancellation = new CancellationTokenSource(); SetBusy(true); status.Text = "Scanning " + c.Host + " …";
                    Snapshot result = await SshScanner.Scan(c, cancellation.Token);
                    Snapshots.Save(result, cache);
                    // The GUI deliberately reads the persisted JSON, not scanner objects.
                    Display(Snapshots.Load(cache));
                } catch (OperationCanceledException) { status.Text = "Scan cancelled. Previous snapshot retained."; }
                catch (Exception ex) { ShowError(ex); }
                finally { if (cancellation != null) cancellation.Dispose(); cancellation = null; SetBusy(false); }
            };
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            open.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "ShellTree JSON (*.json)|*.json", CheckFileExists = true }) {
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName);
            }};
            save.Click += delegate {
                if (snapshot == null) return;
                using (var dialog = new SaveFileDialog { Filter = "ShellTree JSON (*.json)|*.json", FileName = "ShellTree.snapshot.json" }) {
                    if (dialog.ShowDialog(this) == DialogResult.OK) try { Snapshots.Save(snapshot, dialog.FileName); } catch (Exception ex) { ShowError(ex); }
                }
            };
            tree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { if (!selecting) Navigate((string)e.Node.Tag, true); };
            files.DoubleClick += delegate { if (files.SelectedItems.Count == 1) { var e = (Entry)files.SelectedItems[0].Tag; if (e.Kind == "d") Navigate(e.Path, true); } };
            back.Click += delegate { if (history.Count > 0) Navigate(history.Pop(), false); };
            up.Click += delegate { if (current != ".") { int i = current.LastIndexOf('/'); Navigate(i <= 1 ? "." : current.Substring(0, i), true); } };
            help.Click += delegate { MessageBox.Show(this,
                "ShellTree uses Windows OpenSSH and an existing SSH key or agent.\n\n" +
                "First connect in a terminal with ssh -p PORT USER@HOST. Verify the server fingerprint with your administrator, accept the trusted host key, and confirm key authentication works.\n\n" +
                "ShellTree checks known_hosts strictly and does not store passwords. Password prompts are not supported in v0.1.\n\n" +
                "The Linux server needs sh and GNU find (-printf). Depth 0 lists immediate children. Symlinks are shown but never followed. Files are not downloaded or executed.", "SSH setup"); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling scan; close again when it finishes."; } };
            Shown += delegate { if (initialFile != null) LoadFile(initialFile); else if (File.Exists(cache)) LoadFile(cache); };
        }
        static void Add(FlowLayoutPanel panel, string label, Control control) { panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(5, 7, 2, 0) }); panel.Controls.Add(control); }
        void SetBusy(bool busy) { scan.Enabled = open.Enabled = host.Enabled = user.Enabled = port.Enabled = root.Enabled = depth.Enabled = !busy; cancel.Enabled = busy; }
        void ShowError(Exception ex) { status.Text = "Operation failed. Previous snapshot retained."; MessageBox.Show(this, ex.Message, "ShellTree", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        void LoadFile(string path) { try { Display(Snapshots.Load(path)); } catch (Exception ex) { ShowError(ex); } }
        static string DisplayName(string value) { return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"); }
        void Display(Snapshot s) {
            snapshot = s; history.Clear(); current = ".";
            children = s.Entries.Where(e => e.Path != ".").GroupBy(e => e.Parent, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Kind != "d").ThenBy(e => e.Name, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            tree.BeginUpdate(); selecting = true;
            try {
                tree.Nodes.Clear(); nodes.Clear();
                foreach (var e in s.Entries.Where(e => e.Kind == "d").OrderBy(e => e.Path.Count(c => c == '/'))) {
                    var node = new TreeNode(e.Path == "." ? DisplayName(s.Root) : DisplayName(e.Name)) { Tag = e.Path };
                    nodes.Add(e.Path, node);
                    if (e.Path == ".") tree.Nodes.Add(node); else nodes[e.Parent].Nodes.Add(node);
                }
                nodes["."].Expand();
            } finally { selecting = false; tree.EndUpdate(); }
            host.Text = s.Host; user.Text = s.User; root.Text = s.Root; depth.Value = s.Depth;
            if (s.Port >= 1 && s.Port <= 65535) port.Value = s.Port;
            Navigate(".", false);
        }
        void Navigate(string path, bool remember) {
            if (snapshot == null || !nodes.ContainsKey(path)) return;
            if (remember && path != current) history.Push(current);
            current = path; back.Enabled = history.Count > 0;
            address.Text = DisplayName(snapshot.Root.TrimEnd('/') + (path == "." ? "/" : path.Substring(1)));
            selecting = true; tree.SelectedNode = nodes[path]; selecting = false;
            files.BeginUpdate(); files.Items.Clear();
            List<Entry> list;
            if (children.TryGetValue(path, out list)) foreach (var e in list) {
                string kind = e.Kind == "d" ? "Folder" : e.Kind == "f" ? "File" : e.Kind == "l" ? "Symbolic link" : "Special file";
                string modified;
                try { modified = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((double)e.ModifiedUnix).ToLocalTime().ToString("g"); } catch { modified = "Unknown"; }
                var item = new ListViewItem(new[] { DisplayName(e.Name), kind, e.Kind == "f" ? e.Size.ToString("N0") : "", modified }) { Tag = e };
                files.Items.Add(item);
            }
            files.EndUpdate();
            int level = path == "." ? 0 : path.Count(c => c == '/');
            status.Text = level > snapshot.Depth ? "Not scanned at this depth. Set this folder as Root and scan again." :
                files.Items.Count + " items | Snapshot: " + snapshot.ScannedUtc + " | " + snapshot.Host;
        }
    }
}


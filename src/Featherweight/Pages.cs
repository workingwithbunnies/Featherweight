using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Featherweight
{
    static class Ui
    {
        public static Control Section(string title, Control body)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Dock = DockStyle.Fill;
            p.Controls.Add(body);
            p.Controls.Add(new Label { Text = title, Font = Theme.H2, Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Text });
            return p;
        }
        public static Control Spacer(int h) { return new Panel { Dock = DockStyle.Top, Height = h, BackColor = Theme.Bg }; }
        public static void RightAlign(ListView lv, params int[] cols) { foreach (var c in cols) lv.Columns[c].TextAlign = HorizontalAlignment.Right; }

        public static string StateText(ItemState s)
        {
            switch (s) { case ItemState.Applied: return "Applied"; case ItemState.NotApplied: return "Not applied"; case ItemState.NotFound: return "Not on this PC"; default: return "Error"; }
        }
        public static Color StateColor(ItemState s)
        {
            switch (s) { case ItemState.Applied: return Theme.Ok; case ItemState.NotApplied: return Theme.Warn; case ItemState.NotFound: return Theme.Muted; default: return Theme.Bad; }
        }
        public static Color ChangeColor(double change, bool lowerIsBetter)
        {
            if (Math.Abs(change) < 1) return Theme.Muted;
            return (change < 0) == lowerIsBetter ? Theme.Ok : Theme.Bad;
        }
        /// <summary>Two-column header: title/subtitle on the left, a control (buttons) on the right.</summary>
        public static Control HeaderWith(string title, string subtitle, Control right)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Bg };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var h = Theme.Header(title, subtitle); h.Dock = DockStyle.Fill;
            t.Controls.Add(h, 0, 0);
            right.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            t.Controls.Add(right, 1, 0);
            return t;
        }
    }

    // ===================================================================== Dashboard
    class DashboardPage : Page
    {
        readonly Card inUse = new Card("Memory in use"), avail = new Card("Available"), commit = new Card("Committed"), kernel = new Card("Kernel memory");
        readonly Graph graph = new Graph();
        readonly ListView groups, top;
        readonly Label status = Theme.Label("", Theme.Small, Theme.Muted);
        readonly Button cleanBtn = Theme.Button("Free up RAM", true);
        readonly Timer timer = new Timer { Interval = 1000 };
        int tick;

        public DashboardPage()
        {
            groups = Theme.List("App / vendor", -1, "Procs", 60, "Private", 90, "Working set", 100);
            top = Theme.List("Process", -1, "PID", 70, "Private", 90, "Working set", 100);
            Ui.RightAlign(groups, 1, 2, 3); Ui.RightAlign(top, 1, 2, 3);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 16, BackColor = Theme.Bg };
            split.Panel1.Controls.Add(Ui.Section("By app / vendor", groups));
            split.Panel2.Controls.Add(Ui.Section("Top processes", top));
            split.SizeChanged += (s, e) => { if (split.Width > 200) split.SplitterDistance = split.Width / 2 - 8; };

            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 132, ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.Controls.Add(inUse, 0, 0); cards.Controls.Add(avail, 1, 0); cards.Controls.Add(commit, 2, 0); cards.Controls.Add(kernel, 3, 0);
            kernel.Margin = new Padding(0, 0, 0, 12);

            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
            right.Controls.Add(cleanBtn); right.Controls.Add(status);
            cleanBtn.Click += async (s, e) => await Clean();

            Controls.Add(split);
            Controls.Add(Ui.Spacer(14));
            Controls.Add(graph);
            Controls.Add(cards);
            Controls.Add(Ui.HeaderWith("Dashboard", "Live memory use, refreshed every second. \"Available\" already counts cache that Windows hands back instantly.", right));

            timer.Tick += (s, e) => Tick();
            timer.Start();
            Tick();
        }

        public override void OnShow() { RefreshLists(); }

        void Tick()
        {
            var s = SysMem.Read();
            inUse.Set(s.UsedPct.ToString("0") + "%", Theme.MB(s.UsedMB) + " of " + Theme.MB(s.TotalMB), s.UsedPct > 85 ? Theme.Bad : s.UsedPct > 70 ? Theme.Warn : Theme.Text);
            avail.Set(Theme.MB(s.AvailMB), "ready for apps");
            commit.Set(Theme.MB(s.CommitMB), "of " + Theme.MB(s.CommitLimitMB) + " limit");
            kernel.Set(Theme.MB(s.NonpagedMB), "non-paged · " + Theme.MB(s.PagedMB) + " paged", s.NonpagedMB > 800 ? Theme.Warn : Theme.Text);
            graph.Push(s.UsedPct, s.CommitLimitMB > 0 ? 100 * s.CommitMB / s.CommitLimitMB : 0);
            if (Visible && ++tick % 3 == 0) RefreshLists();
        }

        void RefreshLists()
        {
            var procs = ProcScan.Processes();
            groups.BeginUpdate(); groups.Items.Clear();
            // Only companies with something running: the fixed groups (Claude, Dell…) used to show as grey 0 MB rows on every PC.
            foreach (var g in ProcScan.Group(procs).Where(g => g.Count > 0).OrderByDescending(g => g.PrivMB))
                groups.Items.Add(new ListViewItem(new[] { g.Group, g.Count.ToString(), Theme.MB(g.PrivMB), Theme.MB(g.WSMB) }) { ToolTipText = g.Names });
            groups.EndUpdate(); Theme.FillColumn(groups);
            top.BeginUpdate(); top.Items.Clear();
            foreach (var p in procs.OrderByDescending(p => p.PrivMB).Take(30))
                top.Items.Add(new ListViewItem(new[] { p.Name, p.Id.ToString(), Theme.MB(p.PrivMB), Theme.MB(p.WSMB) }));
            top.EndUpdate(); Theme.FillColumn(top);
        }

        async Task Clean()
        {
            if (!Admin.Require(FindForm(), "Dashboard", "Cleaning RAM")) return;
            cleanBtn.Enabled = false; status.Text = "Cleaning…";
            var r = await Task.Run(() => Cleaner.Run(false));
            double total = SysMem.Read().TotalMB;
            status.Text = "Freed " + Theme.MB(Math.Max(0, r.Freed)) + " · in use " + (100 * r.UsedBefore / total).ToString("0") + "% → " + (100 * r.UsedAfter / total).ToString("0") + "%";
            cleanBtn.Enabled = true;
            RefreshLists();
        }
    }

    // ===================================================================== Clean RAM
    class CleanPage : Page
    {
        readonly Button run = Theme.Button("Free up RAM now", true);
        readonly CheckBox deep = new CheckBox { Text = "Deep clean: also clear the standby cache (file cache)", AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        readonly Label freed = Theme.Label("", Theme.Big, Theme.Ok);
        readonly ListView result = Theme.List("Memory", -1, "Before", 120, "After", 120, "Change", 120);
        readonly TextBox log = Theme.LogBox();

        public CleanPage()
        {
            run.Font = new Font("Segoe UI Semibold", 13f); run.Padding = new Padding(22, 10, 22, 10);
            run.Click += async (s, e) => await Go();
            Ui.RightAlign(result, 1, 2, 3);
            log.Height = 110;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 12) };
            top.Controls.Add(run); top.Controls.Add(deep);
            var note = Theme.Label(
                "Quick clean trims idle memory out of every program and writes pending changes to disk, so \"in use\" drops right away. " +
                "Deep clean also empties Windows' file cache: the free-memory number jumps, but apps and games load from disk once more " +
                "until the cache refills. Windows already hands cache back instantly when something needs it, so use this before " +
                "launching something heavy, not on a timer.", Theme.Small, Theme.Muted);
            note.MaximumSize = new Size(820, 0);
            top.Controls.Add(note);
            top.Controls.Add(freed);

            Controls.Add(result);
            Controls.Add(log);
            Controls.Add(top);
            Controls.Add(Theme.Header("Clean RAM", "One click to push idle memory out of programs. Needs administrator rights."));
        }

        async Task Go()
        {
            if (!Admin.Require(FindForm(), "Clean", "Cleaning RAM")) return;
            run.Enabled = false; freed.Text = "Cleaning…"; freed.ForeColor = Theme.Muted;
            bool d = deep.Checked;
            var r = await Task.Run(() => Cleaner.Run(d));
            freed.Text = r.Freed > 1 ? "Freed " + Theme.MB(r.Freed) : "Nothing to free right now";
            freed.ForeColor = r.Freed > 1 ? Theme.Ok : Theme.Muted;
            result.Items.Clear();
            Row("In use", r.UsedBefore, r.UsedAfter, true);
            Row("Available", r.AvailBefore, r.AvailAfter, false);
            Row("Free (zeroed)", r.FreeBefore, r.FreeAfter, false);
            Row("Standby (cache)", r.StandbyBefore, r.StandbyAfter, null);
            Row("Modified (waiting for disk)", r.ModifiedBefore, r.ModifiedAfter, null);
            Theme.Append(log, DateTime.Now.ToString("HH:mm:ss") + (d ? "  deep clean" : "  quick clean"));
            foreach (var l in r.Log) Theme.Append(log, "  " + l);
            run.Enabled = true;
        }

        void Row(string name, double before, double after, bool? lowerIsBetter)
        {
            var it = new ListViewItem(new[] { name, Theme.MB(before), Theme.MB(after), Theme.Delta(after - before) }) { UseItemStyleForSubItems = false };
            it.SubItems[3].ForeColor = lowerIsBetter.HasValue ? Ui.ChangeColor(after - before, lowerIsBetter.Value) : Theme.Muted;
            result.Items.Add(it);
        }
    }

    // ===================================================================== Backups
    class BackupsPage : Page
    {
        readonly ListView list = Theme.List("When", 160, "What was changed", -1, "Settings", 80, "Undone", 150);
        readonly ListBox details = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base, IntegralHeight = false, HorizontalScrollbar = true };
        readonly TextBox log = Theme.LogBox();
        readonly Button restore = Theme.Button("Undo selected", true);
        List<BackupInfo> backups = new List<BackupInfo>();

        public BackupsPage()
        {
            list.MultiSelect = false;
            list.SelectedIndexChanged += (s, e) => ShowDetails();
            var row = Theme.ButtonRow();
            restore.Click += async (s, e) => await Restore();
            var refresh = Theme.Button("Refresh"); refresh.Click += (s, e) => Reload();
            var folder = Theme.Button("Open backups folder"); folder.Click += (s, e) => Paths.Open(Paths.Backups);
            row.Controls.AddRange(new Control[] { restore, refresh, folder });

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 12, BackColor = Theme.Bg };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(Ui.Section("What this backup puts back", details));
            split.SizeChanged += (s, e) => { if (split.Height > 200) split.SplitterDistance = split.Height / 2; };

            Controls.Add(split);
            Controls.Add(row);
            Controls.Add(log);
            Controls.Add(Theme.Header("Backups / Undo", "Each time changes are applied, the original settings are saved here. Undo puts them back (newest first if you undo several)."));
            log.Height = 120;
        }

        public override void OnShow() { Reload(); }

        void Reload()
        {
            backups = Engine.ListBackups();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var b in backups)
            {
                string undone = null;
                try { undone = Json.ReadFile(b.File).Str("RestoredAt"); } catch { }
                var it = new ListViewItem(new[] { FormatTime(b.Time), b.PlanName, b.Count.ToString(), undone == null ? "" : FormatTime(undone) }) { Tag = b };
                if (undone != null) it.ForeColor = Theme.Muted;
                list.Items.Add(it);
            }
            list.EndUpdate(); Theme.FillColumn(list);
            details.Items.Clear();
        }

        static string FormatTime(string iso) { DateTime t; return DateTime.TryParse(iso, out t) ? t.ToString("dd MMM yyyy  HH:mm") : iso; }

        void ShowDetails()
        {
            details.Items.Clear();
            if (list.SelectedItems.Count == 0) return;
            foreach (var i in ((BackupInfo)list.SelectedItems[0].Tag).Items) details.Items.Add(Engine.DescribeBackupItem(i));
        }

        async Task Restore()
        {
            if (list.SelectedItems.Count == 0) { MessageBox.Show(FindForm(), "Select a backup first.", "Featherweight"); return; }
            var b = (BackupInfo)list.SelectedItems[0].Tag;
            if (!Admin.Require(FindForm(), "Backups", "Undoing changes")) return;
            if (MessageBox.Show(FindForm(), "Undo " + b.Count + " change(s) from \"" + b.PlanName + "\" (applied " + FormatTime(b.Time) + ")?",
                    "Undo changes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            restore.Enabled = false;
            Theme.Append(log, "");
            await Task.Run(() =>
            {
                Engine.Restore(b.File, line => Theme.Append(log, line));
                var d = Json.ReadFile(b.File); d["RestoredAt"] = DateTime.Now.ToString("s"); Json.WriteFile(b.File, d);
            });
            restore.Enabled = true;
            Reload();
            MessageBox.Show(FindForm(), "Undone. Restart your PC so startup behaviour is fully back to how it was.", "Featherweight", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ===================================================================== Snapshots
    class SnapshotsPage : Page
    {
        readonly TextBox label = new TextBox { Width = 260, Margin = new Padding(0, 3, 10, 0) };
        readonly ComboBox before = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 3, 10, 0) };
        readonly ComboBox after = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 3, 10, 0) };
        readonly ListView result = Theme.List("What", -1, "Before", 120, "After", 120, "Change", 120);
        readonly Label note = Theme.Label("", Theme.Small, Theme.Warn);
        List<string> files = new List<string>();

        public SnapshotsPage()
        {
            Ui.RightAlign(result, 1, 2, 3);
            var take = Theme.Button("Take snapshot", true);
            take.Click += (s, e) => { var p = Snapshots.Take(label.Text.Trim()); label.Text = ""; Reload(); after.SelectedIndex = 0; };
            var folder = Theme.Button("Open snapshots folder"); folder.Click += (s, e) => Paths.Open(Paths.Snapshots);
            var row1 = Theme.ButtonRow();
            row1.Controls.Add(Theme.Label("Name (optional):", Theme.Base, Theme.Muted)); row1.Controls.Add(label); row1.Controls.Add(take); row1.Controls.Add(folder);
            var compare = Theme.Button("Compare"); compare.Click += (s, e) => Compare();
            var row2 = Theme.ButtonRow();
            row2.Controls.Add(Theme.Label("Before:", Theme.Base, Theme.Muted)); row2.Controls.Add(before);
            row2.Controls.Add(Theme.Label("After:", Theme.Base, Theme.Muted)); row2.Controls.Add(after); row2.Controls.Add(compare);
            foreach (Control c in row1.Controls.Cast<Control>().Concat(row2.Controls.Cast<Control>())) if (c is Label) c.Margin = new Padding(0, 7, 6, 0);

            Controls.Add(result);
            Controls.Add(note);
            note.Dock = DockStyle.Top;
            Controls.Add(row2);
            Controls.Add(row1);
            Controls.Add(Theme.Header("Snapshots", "Save a picture of memory use now, then compare two snapshots, e.g. before and after a change."));
        }

        public override void OnShow() { Reload(); }

        void Reload()
        {
            files = Snapshots.List();
            var names = files.Select(f => Path.GetFileNameWithoutExtension(f) + "   (" + File.GetLastWriteTime(f).ToString("dd MMM HH:mm") + ")").ToArray();
            int b = before.SelectedIndex, a = after.SelectedIndex;
            before.Items.Clear(); after.Items.Clear(); before.Items.AddRange(names); after.Items.AddRange(names);
            if (files.Count > 0) { after.SelectedIndex = a >= 0 && a < files.Count ? a : 0; before.SelectedIndex = b >= 0 && b < files.Count ? b : Math.Min(1, files.Count - 1); }
        }

        void Compare()
        {
            if (before.SelectedIndex < 0 || after.SelectedIndex < 0) return;
            var x = Json.ReadFile(files[before.SelectedIndex]); var y = Json.ReadFile(files[after.SelectedIndex]);
            result.BeginUpdate(); result.Items.Clear();
            var sx = x.Obj("System"); var sy = y.Obj("System");
            Row("Memory in use", sx.Num("UsedMB"), sy.Num("UsedMB"), true, true);
            Row("Available", sx.Num("AvailableMB"), sy.Num("AvailableMB"), false, true);
            Row("Committed", sx.Num("CommitMB"), sy.Num("CommitMB"), true, true);
            Row("Kernel non-paged", sx.Num("NonpagedMB"), sy.Num("NonpagedMB"), true, true);
            Row("Kernel paged", sx.Num("PagedMB"), sy.Num("PagedMB"), true, true);
            Row("Processes", sx.Num("Processes"), sy.Num("Processes"), true, false);
            result.Items.Add(new ListViewItem(""));
            var gx = x.List("Groups").ToDictionary(g => g.Str("Group"), g => g.Num("PrivMB"));
            var gy = y.List("Groups").ToDictionary(g => g.Str("Group"), g => g.Num("PrivMB"));
            foreach (var name in gx.Keys.Union(gy.Keys).OrderBy(n => (gy.ContainsKey(n) ? gy[n] : 0) - (gx.ContainsKey(n) ? gx[n] : 0)))
                Row(name, gx.ContainsKey(name) ? gx[name] : 0, gy.ContainsKey(name) ? gy[name] : 0, true, true);
            result.EndUpdate(); Theme.FillColumn(result);
            double ux = x.Num("UptimeMin"), uy = y.Num("UptimeMin");
            note.Text = Math.Abs(ux - uy) > 30
                ? "Note: uptimes differ (" + ux.ToString("0") + " vs " + uy.ToString("0") + " min after boot). Memory grows after boot, so per-app rows are more reliable than totals."
                : "Uptime at capture: " + ux.ToString("0") + " vs " + uy.ToString("0") + " min after boot.";
        }

        void Row(string name, double a, double b, bool lowerIsBetter, bool mb)
        {
            var it = new ListViewItem(new[] { name, mb ? Theme.MB(a) : a.ToString("0"), mb ? Theme.MB(b) : b.ToString("0"), mb ? Theme.Delta(b - a) : (b - a).ToString("+0;−0;0") }) { UseItemStyleForSubItems = false };
            it.SubItems[3].ForeColor = Ui.ChangeColor(b - a, lowerIsBetter);
            result.Items.Add(it);
        }
    }

    // ===================================================================== Security
    class SecurityPage : Page
    {
        readonly ListView list = Theme.List("Level", 90, "Area", 120, "Finding", -1);
        readonly TextBox detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = Theme.Mono, BackColor = Color.FromArgb(250, 251, 253), BorderStyle = BorderStyle.FixedSingle, WordWrap = false };
        readonly Button scan = Theme.Button("Run security check", true), save = Theme.Button("Save report"), fix = Theme.Button("Remove this certificate (backed up)");
        readonly Label status = Theme.Label("", Theme.Base, Theme.Muted);
        List<Finding> findings = new List<Finding>();

        public SecurityPage()
        {
            list.SelectedIndexChanged += (s, e) => ShowDetail();
            scan.Click += async (s, e) => await Scan();
            save.Click += (s, e) => Save();
            fix.Click += async (s, e) => await Fix();
            fix.Visible = false; save.Enabled = false;
            status.Margin = new Padding(6, 8, 0, 0);
            var row = Theme.ButtonRow();
            row.Controls.AddRange(new Control[] { scan, save, fix, status });

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 12, BackColor = Theme.Bg };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(Ui.Section("Details", detail));
            split.SizeChanged += (s, e) => { if (split.Height > 200) split.SplitterDistance = (int)(split.Height * 0.55); };

            Controls.Add(split);
            Controls.Add(row);
            Controls.Add(Theme.Header("Security check", "Read-only sweep: Defender, unsigned programs, custom root certificates, autostart, scheduled tasks, hijack points and listening ports."));
        }

        async Task Scan()
        {
            scan.Enabled = false; fix.Visible = false; list.Items.Clear(); detail.Text = "";
            findings = await Task.Run(() => SecurityScan.Run(p => BeginInvoke(new Action(() => status.Text = p))));
            list.BeginUpdate();
            foreach (var f in findings)
            {
                var it = new ListViewItem(new[] { f.Severity.ToString(), f.Area, f.Title }) { Tag = f, UseItemStyleForSubItems = false };
                it.SubItems[0].ForeColor = f.Severity == Severity.OK ? Theme.Ok : f.Severity == Severity.Info ? Theme.Info : f.Severity == Severity.Warning ? Theme.Warn : Theme.Bad;
                it.SubItems[0].Font = Theme.Bold;
                list.Items.Add(it);
            }
            list.EndUpdate(); Theme.FillColumn(list);
            int alerts = findings.Count(f => f.Severity == Severity.Alert), warns = findings.Count(f => f.Severity == Severity.Warning);
            status.Text = (alerts == 0 && warns == 0 ? "Looks clean. " : "") + alerts + " alert(s), " + warns + " warning(s), " + findings.Count(f => f.Severity == Severity.Info) + " info" +
                          (Admin.IsAdmin ? "" : "  ·  run as administrator for the full check");
            status.ForeColor = alerts > 0 ? Theme.Bad : warns > 0 ? Theme.Warn : Theme.Ok;
            scan.Enabled = true; save.Enabled = true;
        }

        Finding Selected { get { return list.SelectedItems.Count > 0 ? (Finding)list.SelectedItems[0].Tag : null; } }

        void ShowDetail()
        {
            var f = Selected;
            detail.Text = f == null ? "" : f.Title + "\r\n\r\n" + f.Detail;
            fix.Visible = f != null && f.Fix != null;
        }

        void Save()
        {
            string path = Path.Combine(Paths.Logs, "security-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllLines(path, new[] { "Featherweight security check " + DateTime.Now + (Admin.IsAdmin ? " (administrator)" : " (standard user)"), "" }
                .Concat(findings.SelectMany(f => new[] { "[" + f.Severity + "] " + f.Area + ": " + f.Title, "    " + (f.Detail ?? "").Replace("\r\n", "\r\n    "), "" })));
            MessageBox.Show(FindForm(), "Saved to\n" + path, "Featherweight");
        }

        async Task Fix()
        {
            var f = Selected; if (f == null || f.Fix == null) return;
            if (!Admin.Require(FindForm(), "Security", "Removing a certificate")) return;
            if (MessageBox.Show(FindForm(), "Remove \"" + f.Fix.D.Str("label") + "\" from " + f.Fix.D.Str("location") + "\\Root?\n\nA copy is saved first; you can put it back on the Backups / Undo page.",
                    "Remove certificate", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            var plan = new Plan { File = "(Security page)", Name = "Security: remove certificate " + f.Fix.D.Str("label"), Items = new List<PlanItem> { f.Fix } };
            var lines = new List<string>();
            await Task.Run(() => Engine.Apply(plan, plan.Items, false, lines.Add));
            MessageBox.Show(FindForm(), string.Join("\n", lines), "Featherweight");
            await Scan();
        }
    }
}

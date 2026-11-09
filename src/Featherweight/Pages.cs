using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            p.Controls.Add(new Label { Text = title, Font = Theme.H2, Dock = DockStyle.Top, Height = Theme.S(30), ForeColor = Theme.Text });
            return p;
        }
        public static Control Spacer(int h) { return new Panel { Dock = DockStyle.Top, Height = Theme.S(h), BackColor = Theme.Bg }; }
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
        readonly Button cleanBtn = Theme.Button("Free up RAM", true), moreBtn = Theme.Button("▾", true);
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // companies opened in the list
        int tick;

        public DashboardPage()
        {
            groups = Theme.List("Company (click to see its programs)", -1, "Procs", 60, "Private", 90, "Working set", 100);
            top = Theme.List("Process", -1, "PID", 70, "Private", 90, "Working set", 100);
            Ui.RightAlign(groups, 1, 2, 3); Ui.RightAlign(top, 1, 2, 3);
            groups.ShowItemToolTips = true;
            // Click a company to open or close it.
            groups.MouseClick += (s, e) =>
            {
                var hit = groups.HitTest(e.Location).Item;
                var name = hit == null ? null : hit.Tag as string;
                if (name == null) return;
                if (!expanded.Remove(name)) expanded.Add(name);
                RefreshLists();
            };

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 16, BackColor = Theme.Bg };
            split.Panel1.Controls.Add(Ui.Section("By company", groups));
            split.Panel2.Controls.Add(Ui.Section("Top processes", top));
            split.SizeChanged += (s, e) => { if (split.Width > 200) split.SplitterDistance = split.Width / 2 - 8; };

            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(132), ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.Controls.Add(inUse, 0, 0); cards.Controls.Add(avail, 1, 0); cards.Controls.Add(commit, 2, 0); cards.Controls.Add(kernel, 3, 0);
            kernel.Margin = new Padding(0, 0, 0, 12);

            // "Free up RAM" (quick clean) with a small menu for the deep clean; the result shows underneath.
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            cleanBtn.Margin = new Padding(0); moreBtn.Margin = new Padding(1, 0, 0, 0); moreBtn.Padding = new Padding(6, 4, 6, 4);
            buttons.Controls.Add(cleanBtn); buttons.Controls.Add(moreBtn);
            var menu = new ContextMenuStrip { Font = Theme.Base, ShowImageMargin = false };
            menu.Items.Add("Quick clean (recommended)", null, async (s, e) => await Clean(false));
            menu.Items.Add("Deep clean: also empty the file cache", null, async (s, e) => await Clean(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("What's the difference?", null, (s, e) => MessageBox.Show(FindForm(), CleanHelp, "Free up RAM", MessageBoxButtons.OK, MessageBoxIcon.Information));
            moreBtn.Click += (s, e) => menu.Show(moreBtn, new Point(moreBtn.Width - menu.PreferredSize.Width, moreBtn.Height));
            cleanBtn.Click += async (s, e) => await Clean(false);
            status.TextAlign = ContentAlignment.TopRight; status.Anchor = AnchorStyles.Right;
            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
            right.Controls.Add(buttons); right.Controls.Add(status);

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
            // Remember what was at the top of the list, so the view doesn't jump while it refreshes.
            string topKey = groups.TopItem != null ? groups.TopItem.Name : null;
            groups.BeginUpdate(); groups.Items.Clear();
            // Only companies with something running: the fixed groups (Claude, Dell…) used to show as grey 0 MB rows on every PC.
            foreach (var g in ProcScan.Group(procs).Where(g => g.Count > 0).OrderByDescending(g => g.PrivMB))
            {
                bool open = expanded.Contains(g.Group);
                var it = new ListViewItem(new[] { (open ? "▾  " : "▸  ") + g.Group, g.Count.ToString(), Theme.MB(g.PrivMB), Theme.MB(g.WSMB) })
                { Name = "g|" + g.Group, Tag = g.Group, ToolTipText = open ? "Click to close" : "Click to see its programs" };
                groups.Items.Add(it);
                if (!open) continue;
                // its programs: every copy of a program added up, biggest first
                foreach (var p in g.Members.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Sum(m => m.PrivMB)))
                {
                    int n = p.Count();
                    groups.Items.Add(new ListViewItem(new[] { "        " + p.Key + (n > 1 ? "  ×" + n : ""), n.ToString(), Theme.MB(p.Sum(m => m.PrivMB)), Theme.MB(p.Sum(m => m.WSMB)) })
                    { Name = "p|" + g.Group + "|" + p.Key, ForeColor = Theme.Muted, ToolTipText = n > 1 ? n + " copies of " + p.Key + " are running" : p.Key });
                }
            }
            groups.EndUpdate(); Theme.FillColumn(groups);
            if (topKey != null) { var t = groups.Items[topKey]; if (t != null) try { groups.TopItem = t; } catch { } }
            top.BeginUpdate(); top.Items.Clear();
            foreach (var p in procs.OrderByDescending(p => p.PrivMB).Take(30))
                top.Items.Add(new ListViewItem(new[] { p.Name, p.Id.ToString(), Theme.MB(p.PrivMB), Theme.MB(p.WSMB) }));
            top.EndUpdate(); Theme.FillColumn(top);
        }

        const string CleanHelp =
            "Quick clean trims idle memory out of every program and writes pending changes to disk, so \"in use\" drops right away. " +
            "Programs pull back what they need over time.\n\n" +
            "Deep clean also empties Windows' file cache. The free-memory number jumps, but apps and games load from disk once more " +
            "until the cache refills. Windows already hands cache back instantly when something needs it, so use it right before " +
            "launching something heavy, not on a timer.\n\nBoth need administrator rights. The lasting win is fewer background apps (Optimize).";

        async Task Clean(bool deep)
        {
            if (!Admin.Require(FindForm(), "Dashboard", "Cleaning RAM")) return;
            cleanBtn.Enabled = moreBtn.Enabled = false; status.Text = deep ? "Deep cleaning…" : "Cleaning…";
            var r = await Task.Run(() => Cleaner.Run(deep));
            double total = SysMem.Read().TotalMB;
            string line = (deep ? "Deep clean" : "Quick clean") + ": freed " + Theme.MB(Math.Max(0, r.Freed)) + " · in use " +
                          (100 * r.UsedBefore / total).ToString("0") + "% → " + (100 * r.UsedAfter / total).ToString("0") + "%";
            if (deep && !double.IsNaN(r.StandbyBefore)) line += "\nFile cache " + Theme.MB(r.StandbyBefore) + " → " + Theme.MB(r.StandbyAfter) + " (it refills as you use the PC)";
            status.Text = line;
            try { File.AppendAllText(Path.Combine(Paths.Logs, "activity.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + line.Replace("\n", " · ") + Environment.NewLine); } catch { }
            cleanBtn.Enabled = moreBtn.Enabled = true;
            RefreshLists();
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
            log.Height = Theme.S(120);
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
        readonly TextBox label = new TextBox { Width = Theme.S(260), Margin = new Padding(0, 3, 10, 0) };
        readonly ComboBox before = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(300), Margin = new Padding(0, 3, 10, 0) };
        readonly ComboBox after = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(300), Margin = new Padding(0, 3, 10, 0) };
        readonly ListView result = Theme.List("What", -1, "Before", 120, "After", 120, "Change", 120);
        readonly Label summary = new Label { Dock = DockStyle.Top, AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Text, Padding = new Padding(0, 4, 0, 6), UseMnemonic = false };
        readonly Label note = Theme.Label("", Theme.Small, Theme.Muted);
        List<string> files = new List<string>();
        bool filling;
        const double ProgramThresholdMB = 5;

        public SnapshotsPage()
        {
            Ui.RightAlign(result, 1, 2, 3);
            var take = Theme.Button("Take snapshot", true);
            take.Click += (s, e) => Take();
            label.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Take(); } };
            var folder = Theme.Button("Open snapshots folder"); folder.Click += (s, e) => Paths.Open(Paths.Snapshots);
            var row1 = Theme.ButtonRow();
            row1.Controls.Add(Theme.Label("Name (optional):", Theme.Base, Theme.Muted)); row1.Controls.Add(label); row1.Controls.Add(take); row1.Controls.Add(folder);
            var swap = Theme.Button("Swap"); swap.Click += (s, e) => { if (before.SelectedIndex >= 0 && after.SelectedIndex >= 0) { filling = true; int b = before.SelectedIndex; before.SelectedIndex = after.SelectedIndex; after.SelectedIndex = b; filling = false; Compare(); } };
            before.SelectedIndexChanged += (s, e) => { if (!filling) Compare(); };
            after.SelectedIndexChanged += (s, e) => { if (!filling) Compare(); };
            var row2 = Theme.ButtonRow();
            row2.Controls.Add(Theme.Label("Before:", Theme.Base, Theme.Muted)); row2.Controls.Add(before);
            row2.Controls.Add(Theme.Label("After:", Theme.Base, Theme.Muted)); row2.Controls.Add(after); row2.Controls.Add(swap);
            var del = Theme.Button("Delete…"); del.Click += (s, e) => DeleteSnapshot();
            row2.Controls.Add(del);
            foreach (Control c in row1.Controls.Cast<Control>().Concat(row2.Controls.Cast<Control>())) if (c is Label) c.Margin = new Padding(0, 7, 6, 0);
            note.Dock = DockStyle.Top; note.Padding = new Padding(0, 0, 0, 6);
            summary.SizeChanged += (s, e) => summary.MaximumSize = new Size(Math.Max(200, Width - 60), 0);

            Controls.Add(result);
            Controls.Add(note);
            Controls.Add(summary);
            Controls.Add(row2);
            Controls.Add(row1);
            Controls.Add(Theme.Header("Snapshots",
                "Save a picture of memory use now, then compare two, e.g. before and after you switch something off. " +
                "Taking a snapshot compares it with the one before automatically."));
        }

        public override void OnShow() { Reload(null, null); }

        void Take()
        {
            string previous = files.FirstOrDefault();
            string path = Snapshots.Take(label.Text.Trim());
            label.Text = "";
            Reload(path, previous); // newest as "After", the one just before it as "Before"
        }

        /// <summary>Fills both lists, keeping (or setting) the chosen snapshots by file, not by position.</summary>
        void Reload(string selectAfter, string selectBefore)
        {
            string keepB = selectBefore ?? (before.SelectedIndex >= 0 && before.SelectedIndex < files.Count ? files[before.SelectedIndex] : null);
            string keepA = selectAfter ?? (after.SelectedIndex >= 0 && after.SelectedIndex < files.Count ? files[after.SelectedIndex] : null);
            files = Snapshots.List(); // newest first
            var names = files.Select(Display).ToArray();
            filling = true;
            before.Items.Clear(); after.Items.Clear(); before.Items.AddRange(names); after.Items.AddRange(names);
            if (files.Count > 0)
            {
                int a = keepA != null ? files.IndexOf(keepA) : -1, b = keepB != null ? files.IndexOf(keepB) : -1;
                if (a < 0) a = 0;                                   // newest
                if (b < 0 || b == a) b = Math.Min(a + 1, files.Count - 1); // the one taken just before it
                after.SelectedIndex = a; before.SelectedIndex = b;
            }
            filling = false;
            Compare();
        }

        /// <summary>Deletes the snapshot shown as "After" (or "Before" if only that one is chosen), after asking.</summary>
        void DeleteSnapshot()
        {
            int i = after.SelectedIndex >= 0 ? after.SelectedIndex : before.SelectedIndex;
            if (i < 0 || i >= files.Count) return;
            if (MessageBox.Show(FindForm(), "Delete the snapshot “" + Display(files[i]) + "” (the one shown as “After”)?\n\nThis can't be undone.",
                    "Delete snapshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { File.Delete(files[i]); } catch (Exception ex) { MessageBox.Show(FindForm(), "Couldn't delete it: " + ex.Message, "Featherweight"); return; }
            Reload(null, null);
        }

        static string Display(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string when = File.GetLastWriteTime(file).ToString("ddd d MMM, HH:mm");
            return Regex.IsMatch(name, @"^\d{8}-\d{6}( \(\d+\))?$") ? "Snapshot  ·  " + when : name + "  ·  " + when;
        }

        void Compare()
        {
            result.BeginUpdate(); result.Items.Clear();
            try
            {
                if (files.Count < 2)
                {
                    summary.Text = files.Count == 0 ? "No snapshots yet. Take one now, change something (or just wait), then take another to compare."
                                                    : "One snapshot saved. Take another one later to see what changed.";
                    note.Text = ""; return;
                }
                if (before.SelectedIndex < 0 || after.SelectedIndex < 0) return;
                if (before.SelectedIndex == after.SelectedIndex) { summary.Text = "Pick two different snapshots to compare."; note.Text = ""; return; }
                var x = Json.ReadFile(files[before.SelectedIndex]); var y = Json.ReadFile(files[after.SelectedIndex]);
                var sx = x.Obj("System"); var sy = y.Obj("System");

                Section("Overall");
                Row("Memory in use", sx.Num("UsedMB"), sy.Num("UsedMB"), true, true);
                Row("Available", sx.Num("AvailableMB"), sy.Num("AvailableMB"), false, true);
                Row("Committed", sx.Num("CommitMB"), sy.Num("CommitMB"), true, true);
                Row("Kernel non-paged", sx.Num("NonpagedMB"), sy.Num("NonpagedMB"), true, true);
                Row("Kernel paged", sx.Num("PagedMB"), sy.Num("PagedMB"), true, true);
                Row("Processes", sx.Num("Processes"), sy.Num("Processes"), true, false);

                // By company: only companies that had something running in either snapshot, biggest drops first.
                Section("By company");
                var gx = x.List("Groups").ToDictionary(g => g.Str("Group"), g => g.Num("PrivMB"));
                var gy = y.List("Groups").ToDictionary(g => g.Str("Group"), g => g.Num("PrivMB"));
                foreach (var name in gx.Keys.Union(gy.Keys).Where(n => Val(gx, n) >= 1 || Val(gy, n) >= 1).OrderBy(n => Val(gy, n) - Val(gx, n)))
                    Row(name, Val(gx, name), Val(gy, name), true, true);

                // By program: what actually grew, shrank, started or closed.
                var px = Programs(x); var py = Programs(y);
                var changed = px.Keys.Union(py.Keys, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new { n, a = Val(px, n), b = Val(py, n) })
                    .Where(c => Math.Abs(c.b - c.a) >= ProgramThresholdMB).OrderBy(c => c.b - c.a).ToList();
                bool full = x.ContainsKey("Programs") && y.ContainsKey("Programs");
                Section("By program (changed by " + ProgramThresholdMB + " MB or more)" + (full ? "" : " — older snapshots only saved the 40 biggest programs"));
                if (changed.Count == 0) result.Items.Add(new ListViewItem("   No program changed by " + ProgramThresholdMB + " MB or more.") { ForeColor = Theme.Muted });
                foreach (var c in changed.Take(60))
                    Row(c.n + (c.a < 0.5 ? "   (started)" : c.b < 0.5 ? "   (closed)" : ""), c.a, c.b, true, true);

                summary.Text = Summary(x, y, sx, sy, changed.Select(c => Tuple.Create(c.n, c.b - c.a)).ToList());
                double ux = x.Num("UptimeMin"), uy = y.Num("UptimeMin");
                bool differ = Math.Abs(ux - uy) > 30 && uy < ux + 5; // a restart in between, or very different times after boot
                note.ForeColor = differ ? Theme.Warn : Theme.Muted;
                note.Text = uy < ux
                    ? "The PC was restarted between these snapshots (" + ux.ToString("0") + " vs " + uy.ToString("0") + " min after start-up). Memory grows after start-up, so compare programs rather than totals."
                    : "Taken " + ux.ToString("0") + " and " + uy.ToString("0") + " minutes after the PC started.";
            }
            catch (Exception ex) { summary.Text = "Couldn't read one of these snapshots: " + ex.Message; }
            finally { result.EndUpdate(); Theme.FillColumn(result); }
        }

        /// <summary>One plain sentence (or two) saying what changed between the snapshots.</summary>
        static string Summary(Dictionary<string, object> x, Dictionary<string, object> y, Dictionary<string, object> sx, Dictionary<string, object> sy, List<Tuple<string, double>> programs)
        {
            DateTime tx, ty;
            bool times = DateTime.TryParse(x.Str("Time"), out tx) & DateTime.TryParse(y.Str("Time"), out ty);
            string span = "";
            if (times)
            {
                var d = ty - tx;
                string len = Math.Abs(d.TotalMinutes) < 1 ? "less than a minute" : Math.Abs(d.TotalHours) < 1 ? (int)Math.Abs(d.TotalMinutes) + " min" :
                             Math.Abs(d.TotalDays) < 1 ? (int)Math.Abs(d.TotalHours) + " h " + Math.Abs(d.Minutes) + " min" : (int)Math.Abs(d.TotalDays) + " days";
                span = d.TotalSeconds < 0 ? " (note: “Before” was taken " + len + " after “After”)" : " over " + len;
            }
            double a = sx.Num("UsedMB"), b = sy.Num("UsedMB"), delta = b - a;
            string s = Math.Abs(delta) < 50
                ? "Memory in use stayed about the same (" + Theme.MB(b) + ")" + span + "."
                : "Memory in use went " + (delta < 0 ? "down" : "up") + " by " + Theme.MB(Math.Abs(delta)) + " (from " + Theme.MB(a) + " to " + Theme.MB(b) + ")" + span + ".";
            var drop = programs.Where(p => p.Item2 < 0).OrderBy(p => p.Item2).FirstOrDefault();
            var rise = programs.Where(p => p.Item2 > 0).OrderByDescending(p => p.Item2).FirstOrDefault();
            var parts = new List<string>();
            if (drop != null) parts.Add("biggest drop: " + drop.Item1 + " (" + Theme.Delta(drop.Item2) + ")");
            if (rise != null) parts.Add("biggest rise: " + rise.Item1 + " (" + Theme.Delta(rise.Item2) + ")");
            if (parts.Count > 0) s += "  " + char.ToUpper(parts[0][0]) + string.Join(", ", parts).Substring(1) + ".";
            return s;
        }

        static Dictionary<string, double> Programs(Dictionary<string, object> snap)
        {
            var res = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var list = snap.ContainsKey("Programs") ? snap.List("Programs") : snap.List("Top");
            foreach (var p in list) { string n = p.Str("Name"); if (n == null) continue; double v; res.TryGetValue(n, out v); res[n] = v + p.Num("PrivMB"); }
            return res;
        }

        static double Val(Dictionary<string, double> d, string k) { double v; return d.TryGetValue(k, out v) ? v : 0; }

        void Section(string title)
        {
            if (result.Items.Count > 0) result.Items.Add(new ListViewItem(""));
            result.Items.Add(new ListViewItem(title) { Font = Theme.Bold, ForeColor = Theme.Info });
        }

        void Row(string name, double a, double b, bool lowerIsBetter, bool mb)
        {
            var it = new ListViewItem(new[] { "   " + name, mb ? Theme.MB(a) : a.ToString("0"), mb ? Theme.MB(b) : b.ToString("0"), mb ? Theme.Delta(b - a) : (b - a).ToString("+0;−0;0") }) { UseItemStyleForSubItems = false };
            it.SubItems[3].ForeColor = Ui.ChangeColor(b - a, lowerIsBetter);
            result.Items.Add(it);
        }
    }

    // ===================================================================== Security
    class SecurityPage : Page
    {
        readonly ListView list = Theme.List("Level", 90, "Area", 120, "Finding", -1);
        readonly TextBox detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Base, BackColor = Color.FromArgb(250, 251, 253), BorderStyle = BorderStyle.FixedSingle, WordWrap = true, TabStop = false };
        readonly Button scan = Theme.Button("Run security check", true), save = Theme.Button("Save report"), fix = Theme.Button("Fix (backed up)", true), open = Theme.Button("Open in Windows");
        readonly Label status = Theme.Label("", Theme.Base, Theme.Muted);
        // Before the first check this is a quiet hint in normal text; after it, the coloured result in bigger type.
        readonly Label summary = new Label { Dock = DockStyle.Top, AutoSize = true, Font = Theme.Base, ForeColor = Theme.Muted, Padding = new Padding(0, 2, 0, 10), UseMnemonic = false,
                                             Text = "Run the check to see how this PC is protected. It only reads; nothing changes unless you click a fix." };
        List<Finding> findings = new List<Finding>();

        public SecurityPage()
        {
            list.SelectedIndexChanged += (s, e) => ShowDetail();
            scan.Click += async (s, e) => await Scan();
            save.Click += (s, e) => Save();
            fix.Click += async (s, e) => await Fix();
            open.Click += async (s, e) => await Open();
            fix.Visible = open.Visible = false; save.Enabled = false;
            status.Margin = new Padding(6, 8, 0, 0);
            var row = Theme.ButtonRow();
            row.Controls.AddRange(new Control[] { scan, save, status });

            var actions = Theme.ButtonRow(); actions.Dock = DockStyle.Bottom; actions.Padding = new Padding(0, 6, 0, 0);
            actions.Controls.AddRange(new Control[] { fix, open });
            var detailPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            detailPanel.Controls.Add(detail); detailPanel.Controls.Add(actions);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 12, BackColor = Theme.Bg };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(Ui.Section("Details", detailPanel));
            split.SizeChanged += (s, e) => { if (split.Height > 200) split.SplitterDistance = (int)(split.Height * 0.55); };
            // The summary wraps at the page's width (it used to keep the narrow width the page had when it was first built).
            Resize += (s, e) => summary.MaximumSize = new Size(Math.Max(200, ClientSize.Width - Padding.Horizontal), 0);

            Controls.Add(split);
            Controls.Add(summary);
            Controls.Add(row);
            Controls.Add(Theme.Header("Security check",
                "A read-only look at how this PC is protected: antivirus, Windows' own protections, updates, sign-in and sharing, startup entries, " +
                "certificates, browser extensions and more. It isn't an antivirus; Microsoft Defender (or your own antivirus) does the scanning."));
        }

        static string LevelText(Severity s)
        {
            switch (s) { case Severity.Alert: return "Act now"; case Severity.Warning: return "Look at"; case Severity.Info: return "Note"; default: return "OK"; }
        }

        async Task Scan()
        {
            scan.Enabled = false; fix.Visible = open.Visible = false; list.Items.Clear(); detail.Text = "";
            summary.Font = Theme.H2; summary.Text = "Checking… this takes about a minute."; summary.ForeColor = Theme.Muted;
            findings = await Task.Run(() => SecurityScan.Run(p => BeginInvoke(new Action(() => status.Text = p))));
            list.BeginUpdate();
            foreach (var f in findings)
            {
                var it = new ListViewItem(new[] { LevelText(f.Severity), f.Area, f.Title }) { Tag = f, UseItemStyleForSubItems = false };
                it.SubItems[0].ForeColor = f.Severity == Severity.OK ? Theme.Ok : f.Severity == Severity.Info ? Theme.Info : f.Severity == Severity.Warning ? Theme.Warn : Theme.Bad;
                it.SubItems[0].Font = Theme.Bold;
                list.Items.Add(it);
            }
            list.EndUpdate(); Theme.FillColumn(list);
            int alerts = findings.Count(f => f.Severity == Severity.Alert), warns = findings.Count(f => f.Severity == Severity.Warning), notes = findings.Count(f => f.Severity == Severity.Info);
            summary.Text = alerts > 0 ? alerts + (alerts == 1 ? " thing needs" : " things need") + " attention now" + (warns > 0 ? ", and " + warns + " more to look at" : "") + ". Select one to see what to do."
                         : warns > 0 ? warns + (warns == 1 ? " thing" : " things") + " to look at. Select one to see what to do."
                         : "Everything important looks good." + (notes > 0 ? " " + notes + " note(s) below, for your information." : "");
            summary.ForeColor = alerts > 0 ? Theme.Bad : warns > 0 ? Theme.Warn : Theme.Ok;
            status.Text = findings.Count(f => f.Severity == Severity.OK) + " checks OK" + (Admin.IsAdmin ? "" : "  ·  run as administrator for the full check");
            scan.Enabled = true; save.Enabled = true;
        }

        Finding Selected { get { return list.SelectedItems.Count > 0 ? (Finding)list.SelectedItems[0].Tag : null; } }

        void ShowDetail()
        {
            var f = Selected;
            detail.Text = f == null ? "" : f.Title + "\r\n\r\n" + f.Detail;
            detail.Select(0, 0);
            fix.Visible = f != null && f.Fix != null;
            if (fix.Visible) fix.Text = f.FixLabel ?? (f.Fix.Type == "cert" ? "Remove this certificate (backed up)" : "Fix (backed up)");
            open.Visible = f != null && f.OpenTarget != null;
            if (open.Visible) open.Text = f.OpenLabel ?? "Open in Windows";
        }

        void Save()
        {
            string path = Path.Combine(Paths.Logs, "security-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllLines(path, new[] { "Featherweight security check " + DateTime.Now + (Admin.IsAdmin ? " (administrator)" : " (standard user)"), summary.Text, "" }
                .Concat(findings.SelectMany(f => new[] { "[" + LevelText(f.Severity) + "] " + f.Area + ": " + f.Title, "    " + (f.Detail ?? "").Replace("\r\n", "\r\n    "), "" })));
            MessageBox.Show(FindForm(), "Saved to\n" + Paths.Display + "\\logs\\" + Path.GetFileName(path) + "\n\nIt stays on this PC.", "Featherweight");
        }

        async Task Fix()
        {
            var f = Selected; if (f == null || f.Fix == null) return;
            if (!Admin.Require(FindForm(), "Security", "Changing this")) return;
            bool cert = f.Fix.Type == "cert";
            string what = cert ? "Remove \"" + f.Fix.D.Str("label") + "\" from " + f.Fix.D.Str("location") + "\\Root?" : f.Fix.Describe() + "?";
            if (MessageBox.Show(FindForm(), what + "\n\nThe current setting is saved first; you can put it back on the Backups / Undo page.",
                    "Featherweight", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            var plan = new Plan { File = "(Security page)", Name = "Security: " + (cert ? "remove certificate " + f.Fix.D.Str("label") : f.Title), Items = new List<PlanItem> { f.Fix } };
            var lines = new List<string>();
            await Task.Run(() => Engine.Apply(plan, plan.Items, false, lines.Add));
            MessageBox.Show(FindForm(), string.Join("\n", lines), "Featherweight");
            await Scan();
        }

        async Task Open()
        {
            var f = Selected; if (f == null || f.OpenTarget == null) return;
            if (f.OpenTarget != SecurityScan.QuickScanTarget)
            {
                try { System.Diagnostics.Process.Start(f.OpenTarget); }
                catch { MessageBox.Show(FindForm(), "Windows couldn't open that page on this PC. You'll find it in Settings or Windows Security.", "Featherweight"); }
                return;
            }
            // Defender quick scan: Defender's own scanner, run in the background
            open.Enabled = scan.Enabled = false; status.Text = "Microsoft Defender is scanning… (usually a few minutes)";
            string result = await Task.Run(() => SecurityScan.RunQuickScan());
            open.Enabled = scan.Enabled = true; status.Text = result;
            try { File.AppendAllText(Path.Combine(Paths.Logs, "activity.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + "Defender quick scan: " + result + Environment.NewLine); } catch { }
            MessageBox.Show(FindForm(), result, "Microsoft Defender", MessageBoxButtons.OK, result.Contains("no threats") ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await Scan();
        }
    }
}

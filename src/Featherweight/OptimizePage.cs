using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Featherweight
{
    // Lists the optimizations from plans\optimizations.json that apply to this PC, grouped by vendor,
    // with a details panel and Optimize / Restore default for the selection.
    class OptimizePage : Page
    {
        readonly ListView list = Theme.List("Optimization", -1, "Suggested", 108, "Status", 108, "Memory", 118);
        readonly Label summary = new Label { Dock = DockStyle.Top, Height = 32, Font = Theme.Bold, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button optimizeAll = Theme.Button("Apply recommended", true), rescan = Theme.Button("Rescan"), toggleLog = Theme.Button("Show activity");
        readonly CheckBox showAdvanced = new CheckBox { Text = "Show advanced", AutoSize = true, Margin = new Padding(4, 9, 0, 0), BackColor = Theme.Bg };

        readonly Label dTitle = Theme.Label("", Theme.H2), dCategory = Theme.Label("", Theme.Small, Theme.Muted), dStatus = Theme.Label("", Theme.Bold);
        readonly Label dVerdict = Theme.Label("", Theme.Bold), dNote = Theme.Label("", Theme.Base);
        readonly Label dDesc = Theme.Label(""), dMemory = Theme.Label("", Theme.Base, Theme.Muted), dRisk = Theme.Label("", Theme.Base, Theme.Muted);
        readonly Button optimize = Theme.Button("Optimize", true), restore = Theme.Button("Restore default");
        readonly TextBox dChanges = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
            Font = Theme.Base, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(250, 251, 253),
            TabStop = false // otherwise it gets the focus after a dialog closes, and Windows selects all its text
        };
        readonly Label changesLabel = Theme.Label("What it changes", Theme.Bold, Theme.Muted);
        readonly TextBox log = Theme.LogBox();
        List<Optimization> opts = new List<Optimization>();
        bool busy;

        public OptimizePage()
        {
            list.MultiSelect = true; list.ShowItemToolTips = true;
            list.SelectedIndexChanged += (s, e) => ShowDetails();

            // ---- details panel
            var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card, Padding = new Padding(18, 14, 18, 14) };
            var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 12) };
            buttons.Controls.Add(optimize); buttons.Controls.Add(restore);
            var rows = new Control[] { dCategory, dTitle, dStatus, dDesc, dVerdict, dNote, dMemory, dRisk, buttons, changesLabel, dChanges };
            foreach (var c in rows) { detail.Controls.Add(c); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            detail.RowStyles[detail.RowStyles.Count - 1] = new RowStyle(SizeType.Percent, 100);
            dDesc.Margin = new Padding(0, 8, 0, 8);
            dNote.Margin = new Padding(0, 0, 0, 8);
            detail.SizeChanged += (s, e) =>
            {
                foreach (var l in new[] { dTitle, dCategory, dStatus, dVerdict, dNote, dDesc, dMemory, dRisk }) l.MaximumSize = new Size(Math.Max(100, detail.ClientSize.Width - 40), 0);
            };
            detail.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, detail.Width - 1, detail.Height - 1); };

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 16, BackColor = Theme.Bg, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(detail);
            split.SizeChanged += (s, e) => { if (split.Width > 760) split.SplitterDistance = split.Width - 350; };

            log.Visible = false; log.Height = 130;
            toggleLog.Click += (s, e) => ShowLog(!log.Visible);
            rescan.Click += async (s, e) => await Rescan();
            // "Apply recommended" only touches items judged Recommended for this PC; Optional ones stay the user's call.
            optimizeAll.Click += async (s, e) => await DoOptimize(Visible_().Where(o => o.Verdict == Verdict.Recommended && !o.IsManual).ToList());
            optimize.Click += async (s, e) => await DoOptimize(Selected());
            restore.Click += async (s, e) => await DoRestore(Selected());
            showAdvanced.CheckedChanged += (s, e) => Fill(Selected().Select(o => o.Id).ToList());

            var right = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false };
            right.Controls.AddRange(new Control[] { optimizeAll, rescan, toggleLog, showAdvanced });

            Controls.Add(split);
            Controls.Add(log);
            Controls.Add(summary);
            Controls.Add(Ui.HeaderWith("Optimize",
                "Background apps and services found on this PC. Reviewed items come with a suggestion based on how this PC is set up; everything else from other companies is listed below them. Every change is backed up first, and Restore default puts it back.", right));
        }

        public override void OnShow() { if (!busy) { var _ = Rescan(); } }

        // ---------------------------------------------------------------- scanning / list
        async Task Rescan()
        {
            busy = true; SetEnabled(false); summary.Text = "Scanning this PC…"; summary.ForeColor = Theme.Muted;
            var keep = Selected().Select(o => o.Id).ToList();
            List<Optimization> loaded = null; string error = null; var warnings = new List<string>();
            await Task.Run(() => { try { loaded = Catalog.ScanAll(w => { lock (warnings) warnings.Add(w); }); } catch (Exception ex) { error = ex.Message; } });
            busy = false; SetEnabled(true);
            foreach (var w in warnings) Log("NOTE   " + w);
            if (error != null) { summary.Text = "Could not read " + Path.GetFileName(Catalog.FilePath) + ": " + error; summary.ForeColor = Theme.Bad; return; }
            opts = loaded.Where(o => o.Present).ToList();
            Fill(keep);
        }

        /// <summary>Items shown in the list: advanced ones only when "Show advanced" is ticked (or already optimized).</summary>
        List<Optimization> Visible_() { return opts.Where(o => showAdvanced.Checked || !o.IsAdvanced || o.State != OptState.NotOptimized).ToList(); }

        void Fill(List<string> keep)
        {
            var shown = Visible_();
            list.BeginUpdate(); list.Items.Clear(); list.Groups.Clear();
            // Reviewed catalog groups first (catalog order), then each company's other background items, biggest first.
            var reviewedCats = shown.Where(o => !o.Discovered).Select(o => o.Category).Distinct();
            var foundCats = shown.Where(o => o.Discovered).GroupBy(o => o.Category).OrderByDescending(g => g.Sum(o => o.LiveMB)).ThenBy(g => g.Key).Select(g => g.Key);
            var groups = reviewedCats.Select(c => Tuple.Create(c, false)).Concat(foundCats.Select(c => Tuple.Create(c, true)));
            foreach (var grp in groups)
            {
                string cat = grp.Item1; bool found = grp.Item2;
                var items = shown.Where(o => o.Category == cat && o.Discovered == found).OrderByDescending(o => found ? o.LiveMB : 0).ToList();
                int counted = items.Count(o => !o.IsManual);
                int reviewedHere = items.Count(o => o.Knowledge != null);
                var g = new ListViewGroup(found
                    ? cat + "    found on this PC" + (reviewedHere == 0 ? " (not reviewed yet)" : reviewedHere == items.Count ? " (reviewed)" : " (" + reviewedHere + " of " + items.Count + " reviewed)")
                    : cat + "    " + items.Count(o => !o.IsManual && o.State == OptState.Optimized) + " of " + counted + " optimized" +
                      (counted < items.Count ? " · " + (items.Count - counted) + " set in another app" : ""));
                list.Groups.Add(g);
                foreach (var o in items)
                {
                    var it = new ListViewItem(new[] { o.Discovered ? o.Title + "  (" + o.Kind.ToLowerInvariant() + ")" : o.Title, VerdictText(o), StateText(o), MemoryText(o) }, g)
                    { Tag = o, UseItemStyleForSubItems = false, ToolTipText = o.VerdictNote ?? o.Description };
                    it.SubItems[1].ForeColor = o.ViewOnly || (o.Discovered && o.Knowledge == null) ? Theme.Muted : VerdictColor(o.Verdict);
                    it.SubItems[2].ForeColor = StateColor(o); it.SubItems[2].Font = Theme.Bold;
                    it.SubItems[3].ForeColor = o.RunningCount > 0 && o.State != OptState.Optimized ? Theme.Text : Theme.Muted;
                    list.Items.Add(it);
                    if (keep.Contains(o.Id)) it.Selected = true;
                }
            }
            list.EndUpdate(); Theme.FillColumn(list);
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[0].Selected = true;
            ShowDetails();

            // Counts only cover what Featherweight itself switches; items set in another app aren't claimed as savings.
            // "Reviewed" = the catalog plus found items our knowledge base describes; "keep" items aren't meant to be optimized.
            var reviewed = shown.Where(o => !o.IsManual && !o.ViewOnly && (!o.Discovered || (o.Knowledge != null && o.Knowledge.Suggest != "keep"))).ToList();
            var foundItems = shown.Where(o => o.Discovered && !o.ViewOnly && o.Knowledge == null).ToList();
            var rec = reviewed.Where(o => o.Verdict == Verdict.Recommended).ToList();
            int recDone = rec.Count(o => o.State == OptState.Optimized), recTodo = rec.Count - recDone;
            int done = reviewed.Count(o => o.State == OptState.Optimized), changed = shown.Count(o => o.State == OptState.ChangedBack);
            int foundOn = foundItems.Count(o => o.State != OptState.Optimized), foundDone = foundItems.Count - foundOn;
            double saving = reviewed.Where(o => o.State == OptState.Optimized).Sum(o => o.EstimateMB);
            double recWorth = rec.Where(o => o.State != OptState.Optimized).Sum(o => o.RunningCount > 0 ? o.LiveMB : o.EstimateMB);
            if (shown.Count == 0) summary.Text = "No background apps or services from other companies were found on this PC.";
            else summary.Text = (recTodo == 0 ? "All recommended items are applied" : recTodo + " recommended item(s) left, worth about " + Theme.MB(recWorth)) +
                                "  ·  " + done + " of " + reviewed.Count + " reviewed items optimized" + (saving >= 1 ? ", saving about " + Theme.MB(saving) : "") +
                                (foundItems.Count > 0 ? "  ·  " + foundOn + " more found, not reviewed" + (foundDone > 0 ? " (" + foundDone + " optimized)" : "") : "") +
                                (changed > 0 ? "  ·  " + changed + " changed back by an update or app" : "");
            int hiddenDrivers = opts.Count(o => o.ViewOnly && !shown.Contains(o));
            if (hiddenDrivers > 0) summary.Text += "  ·  " + hiddenDrivers + " driver helpers listed for information (tick Show advanced)";
            summary.ForeColor = changed > 0 ? Theme.Bad : recTodo > 0 ? Theme.Warn : Theme.Ok;
            optimizeAll.Enabled = !busy && recTodo > 0;
        }

        static string VerdictText(Optimization o)
        {
            if (o.ViewOnly) return "Info only";
            if (o.Discovered && o.Knowledge == null) return "Not reviewed";
            if (o.Knowledge != null && o.Knowledge.Suggest == "keep") return "Keep";
            if (o.IsAdvanced) return "Advanced";
            switch (o.Verdict) { case Verdict.Recommended: return "Recommended"; case Verdict.Avoid: return "Not advised here"; default: return "Optional"; }
        }

        static Color VerdictColor(Verdict v)
        {
            switch (v) { case Verdict.Recommended: return Theme.Info; case Verdict.Avoid: return Theme.Bad; default: return Theme.Muted; }
        }

        static string StateText(Optimization o)
        {
            if (o.IsManual || o.ViewOnly) return o.RunningCount > 0 ? "Running" : "Not running"; // Featherweight can only see these
            if (o.Knowledge != null && o.Knowledge.Suggest == "keep" && o.State == OptState.NotOptimized) return "On"; // meant to stay on
            return StateText(o.State);
        }

        static Color StateColor(Optimization o)
        {
            if (o.IsManual || o.ViewOnly) return o.RunningCount > 0 ? Theme.Warn : Theme.Muted;
            if (o.Knowledge != null && o.Knowledge.Suggest == "keep" && o.State == OptState.NotOptimized) return Theme.Muted;
            return StateColor(o.State);
        }

        static string StateText(OptState s)
        {
            switch (s)
            {
                case OptState.Optimized: return "Optimized";
                case OptState.Partial: return "Partly optimized";
                case OptState.ChangedBack: return "Changed back";
                default: return "Not optimized";
            }
        }

        static Color StateColor(OptState s)
        {
            switch (s) { case OptState.Optimized: return Theme.Ok; case OptState.ChangedBack: return Theme.Bad; default: return Theme.Warn; }
        }

        static string MemoryText(Optimization o)
        {
            if (o.RunningCount > 0) return "uses " + Theme.MB(o.LiveMB);
            if (o.EstimateMB < 1) return "—";
            return (o.State == OptState.Optimized && !o.IsManual ? "saves ~" : "~") + Theme.MB(o.EstimateMB);
        }

        List<Optimization> Selected() { return list.SelectedItems.Cast<ListViewItem>().Select(i => (Optimization)i.Tag).ToList(); }

        void ShowDetails()
        {
            var sel = Selected();
            optimize.Text = "Optimize"; restore.Text = "Restore default";
            if (sel.Count == 0)
            {
                dTitle.Text = "Select an item"; dCategory.Text = dStatus.Text = dVerdict.Text = dNote.Text = dDesc.Text = dMemory.Text = dRisk.Text = dChanges.Text = "";
                optimize.Enabled = restore.Enabled = false; return;
            }
            if (sel.Count > 1)
            {
                dCategory.Text = "SELECTION"; dTitle.Text = sel.Count + " items selected"; dStatus.Text = "";
                int avoid = sel.Count(o => o.Verdict == Verdict.Avoid);
                dVerdict.Text = avoid > 0 ? avoid + " of these aren't advised on this PC" : ""; dVerdict.ForeColor = Theme.Bad; dNote.Text = "";
                dDesc.Text = string.Join(", ", sel.Select(o => o.Title));
                dMemory.Text = "Using right now: " + Theme.MB(sel.Sum(o => o.LiveMB));
                dRisk.Text = "Optimize or restore them all at once.";
                dChanges.Text = string.Join("\r\n\r\n", sel.Where(o => !o.ViewOnly).Select(o => o.Title + "\r\n" + string.Join("\r\n", o.IsManual ? o.Details : o.Changes)));
                dChanges.Select(0, 0);
                optimize.Enabled = !busy && sel.Any(o => !o.ViewOnly && o.State != OptState.Optimized);
                restore.Enabled = !busy && sel.Any(o => !o.ViewOnly && o.State != OptState.NotOptimized);
                return;
            }
            var x = sel[0];
            dCategory.Text = (x.Discovered ? x.Category + "  ·  " + x.Kind : x.Category).ToUpper();
            dTitle.Text = x.Title;
            dStatus.Text = StateText(x) + (x.State == OptState.ChangedBack ? " — an update or app switched this back on" : "");
            dStatus.ForeColor = StateColor(x);
            // Found items: a short summary here; the full facts go in the scrolling box so the buttons stay in view.
            dDesc.Text = x.Discovered && x.Summary != null ? x.Summary : x.Description;
            dVerdict.Text = "Suggested: " + VerdictText(x);
            dVerdict.ForeColor = VerdictColor(x.Verdict);
            dNote.Text = x.VerdictNote != null && x.Knowledge == null ? "On this PC: " + x.VerdictNote : "";
            dMemory.Text = x.RunningCount > 0
                ? "Memory: using " + Theme.MB(x.LiveMB) + " right now (" + x.RunningCount + (x.RunningCount == 1 ? " process)" : " processes)")
                : x.EstimateMB >= 1 ? "Memory: about " + Theme.MB(x.EstimateMB) + " when it runs" : "Memory: negligible";
            dRisk.Text = x.ViewOnly ? "Listed for information only: Featherweight doesn't change driver helpers it hasn't reviewed"
                       : x.Discovered && x.Knowledge == null ? "Risk: unknown, not reviewed yet  ·  backed up, undo any time"
                       : x.Discovered ? "Reviewed (" + (x.Knowledge.Confidence ?? "see description") + ")  ·  backed up, undo any time"
                                      : "Risk: " + x.Risk + (x.IsManual ? "  ·  a setting in another app" : "  ·  backed up, undo any time");
            // One plain-language box for every item: what it is on this PC, then what Optimize would change.
            changesLabel.Text = "About this item";
            string about = x.Discovered && x.Summary != null
                ? x.Description
                : (x.IsManual ? x.Manual.Replace("\n", "\r\n") + "\r\n\r\n" : "") +
                  (x.Facts.Count > 0 ? "On this PC:\r\n" + string.Join("\r\n", x.Facts.Select(f => "•  " + f)) : "");
            string changes = x.ViewOnly || x.IsManual || x.Changes.Count == 0 ? "" : "What it changes:\r\n" + string.Join("\r\n", x.Changes) +
                (x.Changes.Any(c => c.StartsWith("✓")) ? "\r\n(✓ = already done)" : "");
            dChanges.Text = (about + (about.Length > 0 && changes.Length > 0 ? "\r\n\r\n" : "") + changes).Trim();
            dChanges.Select(0, 0);
            if (x.IsManual) { optimize.Text = "How to turn it off"; restore.Text = "How to turn it back on"; }
            optimize.Enabled = !busy && !x.ViewOnly && x.State != OptState.Optimized;
            restore.Enabled = !busy && !x.ViewOnly && x.State != OptState.NotOptimized;
        }

        void SetEnabled(bool on)
        {
            optimizeAll.Enabled = rescan.Enabled = list.Enabled = on;
            if (!on) optimize.Enabled = restore.Enabled = false;
            else { ShowDetails(); if (list.CanFocus) list.Focus(); } // keep the focus on the list, not the details text
        }

        void ShowLog(bool show) { log.Visible = show; toggleLog.Text = show ? "Hide activity" : "Show activity"; }

        void Log(string line)
        {
            Theme.Append(log, line);
            try { File.AppendAllText(Path.Combine(Paths.Logs, "activity.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + line + Environment.NewLine); } catch { }
            if (line.StartsWith("FAIL")) BeginInvoke(new Action(() => ShowLog(true)));
        }

        // ---------------------------------------------------------------- actions
        async Task DoOptimize(List<Optimization> targets)
        {
            targets = targets.Where(o => !o.ViewOnly && o.State != OptState.Optimized).ToList();
            if (targets.Count == 0) return;
            var auto = targets.Where(o => !o.IsManual).ToList();
            var manual = targets.Where(o => o.IsManual).ToList();
            if (auto.Count > 0)
            {
                if (!Admin.Require(FindForm(), "Optimize", "Optimizing")) return;
                // Items the per-PC checks advise against need an explicit, separate yes (Cancel is the default).
                var risky = auto.Where(o => o.Verdict == Verdict.Avoid).ToList();
                if (risky.Count > 0 && MessageBox.Show(FindForm(),
                        "Featherweight doesn't advise this on this PC:\n\n" + string.Join("\n\n", risky.Select(o => "• " + o.Title + "\n   " + o.VerdictNote)) +
                        "\n\nOptimize anyway?", "Not advised on this PC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                // Items found on this PC but not reviewed: say plainly that nobody has checked them (Cancel is the default).
                var unreviewed = auto.Where(o => o.Discovered && o.Knowledge == null).ToList();
                if (unreviewed.Count > 0 && MessageBox.Show(FindForm(),
                        "Featherweight found these on this PC, but nobody has reviewed them yet:\n\n• " + string.Join("\n• ", unreviewed.Select(o => o.Title + " (" + o.Category + ", " + o.Kind.ToLowerInvariant() + ")")) +
                        "\n\nThey're usually safe to switch off, but some hardware features (hotkeys, fans, lighting, audio effects, sync) can depend on them. " +
                        "If something stops working, select it and choose Restore default.\n\nContinue?",
                        "Not reviewed yet", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (MessageBox.Show(FindForm(), "Optimize " + auto.Count + " item(s)?\n\n• " + string.Join("\n• ", auto.Select(o => o.Title)) +
                        "\n\nCurrent settings are backed up first, and Restore default puts them back.",
                        "Optimize", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                busy = true; SetEnabled(false); summary.Text = "Optimizing…";
                Log("");
                await Task.Run(() => Catalog.Apply(auto, Log));
                busy = false;
            }
            if (manual.Count > 0)
                MessageBox.Show(FindForm(), string.Join("\n\n", manual.Select(m => m.Title + "\n" + m.Manual)), "One step in another app", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await Rescan();
            if (auto.Count > 0)
                MessageBox.Show(FindForm(), "Done. Restart your PC so the changes fully take effect.", "Featherweight", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        async Task DoRestore(List<Optimization> targets)
        {
            targets = targets.Where(o => !o.ViewOnly && o.State != OptState.NotOptimized).ToList();
            if (targets.Count == 0) return;
            var auto = targets.Where(o => !o.IsManual).ToList();
            var manual = targets.Where(o => o.IsManual).ToList();
            if (auto.Count > 0)
            {
                if (!Admin.Require(FindForm(), "Optimize", "Restoring defaults")) return;
                if (MessageBox.Show(FindForm(), "Restore the default for " + auto.Count + " item(s)?\n\n• " + string.Join("\n• ", auto.Select(o => o.Title)) +
                        "\n\nThey'll start with Windows again, as they did before.",
                        "Restore default", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                busy = true; SetEnabled(false); summary.Text = "Restoring…";
                Log("");
                await Task.Run(() => Catalog.RestoreDefaults(auto, Log));
                busy = false;
            }
            if (manual.Count > 0)
                MessageBox.Show(FindForm(), string.Join("\n\n", manual.Select(m => m.Title + "\n" + m.Manual)), "One step in another app", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await Rescan();
            if (auto.Count > 0)
                MessageBox.Show(FindForm(), "Restored. Restart your PC so startup behaviour is fully back.", "Featherweight", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

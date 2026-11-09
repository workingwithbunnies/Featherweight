using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Featherweight
{
    class MainForm : Form
    {
        readonly Panel pageHost, content;
        readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
        readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();

        // WinForms docks in reverse order of adding: add Fill controls first, then the edges.
        public MainForm(string startPage)
        {
            Text = "Featherweight" + (Admin.IsAdmin ? " (Administrator)" : "");
            // Sizes grow with the display scale, but never beyond the screen.
            var screen = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1220), screen.Width), Math.Min(Theme.S(800), screen.Height));
            MinimumSize = new Size(Math.Min(Theme.S(1000), screen.Width), Math.Min(Theme.S(660), screen.Height));
            StartPosition = FormStartPosition.CenterScreen; Font = Theme.Base; BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            content.Controls.Add(pageHost);
            if (!Admin.IsAdmin) content.Controls.Add(Banner());

            var sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(212), BackColor = Theme.Sidebar };
            Controls.Add(content);
            Controls.Add(sidebar);

            pages["Dashboard"] = new DashboardPage();
            pages["Optimize"] = new OptimizePage();
            pages["Backups"] = new BackupsPage();
            pages["Snapshots"] = new SnapshotsPage();
            pages["Security"] = new SecurityPage();
            pages["Settings"] = new SettingsPage();
            foreach (var p in pages.Values) { p.Visible = false; pageHost.Controls.Add(p); }

            var footer = new Label
            {
                Dock = DockStyle.Bottom, Height = Theme.S(54), ForeColor = Color.FromArgb(130, 138, 150), Font = Theme.Small,
                Padding = new Padding(20, 0, 10, 12), TextAlign = ContentAlignment.BottomLeft,
                // No PC or user name here: people share screenshots in bug reports.
                Text = (Admin.IsAdmin ? "● Administrator" : "○ Standard user") + "\nVersion " + AppVersion
            };
            sidebar.Controls.Add(footer);
            var labels = new[] {
                Tuple.Create("Dashboard", "Dashboard"), Tuple.Create("Optimize", "Optimize"),
                Tuple.Create("Backups", "Backups / Undo"), Tuple.Create("Snapshots", "Snapshots"), Tuple.Create("Security", "Security check"),
                Tuple.Create("Settings", "Settings & about") };
            foreach (var t in labels.Reverse()) sidebar.Controls.Add(NavButton(t.Item1, t.Item2));
            var brand = new Label
            {
                Dock = DockStyle.Top, Height = Theme.S(78), Text = "Featherweight", ForeColor = Color.White, Font = Theme.H1,
                Padding = new Padding(18, 0, 0, 8), TextAlign = ContentAlignment.BottomLeft
            };
            var tag = new Label { Dock = DockStyle.Top, Height = Theme.S(30), Text = "memory · startup · security", ForeColor = Color.FromArgb(130, 138, 150), Font = Theme.Small, Padding = new Padding(21, 0, 0, 0) };
            sidebar.Controls.Add(tag);
            sidebar.Controls.Add(brand);

            ShowPage(startPage != null && pages.ContainsKey(startPage) ? startPage : "Dashboard");
            Shown += async (s, e) => await AutomaticUpdateCheck();
        }

        /// <summary>Once a day, only if the user allowed it on the welcome screen / in Settings.</summary>
        async System.Threading.Tasks.Task AutomaticUpdateCheck()
        {
            if (!Updates.AutomaticCheckDue) return;
            string error = null;
            var info = await System.Threading.Tasks.Task.Run(() => Updates.Check(out error));
            // Same opt-in, same GitHub: also fetch newer item descriptions (one file; nothing about this PC is sent).
            await System.Threading.Tasks.Task.Run(() => { string m; Knowledge.TryUpdate(out m); });
            Settings.LastUpdateCheck = DateTime.Now.ToString("s"); Settings.Save();
            if (info != null && Updates.IsNewer(info.Version, AppVersion) && info.Version != Settings.SkippedVersion)
                content.Controls.Add(UpdateBanner(info)); // docks on top, above the admin banner
        }

        Control UpdateBanner(UpdateInfo info)
        {
            var get = Theme.Button("See what's new", true);
            var later = Theme.Button("Not now");
            var bar = BannerBar(Color.FromArgb(222, 236, 252), "Featherweight " + info.Version + " is available (you have " + AppVersion + ").", later, get);
            get.Click += (s, e) => { try { System.Diagnostics.Process.Start(info.Url); } catch { } };
            later.Click += (s, e) => { Settings.SkippedVersion = info.Version; Settings.Save(); bar.Visible = false; };
            return bar;
        }

        Control Banner()
        {
            var btn = Theme.Button("Restart as administrator", true);
            btn.Click += (s, e) => { if (!Admin.RestartElevated(CurrentPage)) MessageBox.Show(this, "Windows did not grant administrator rights.", "Featherweight"); };
            return BannerBar(Theme.Banner, "Running without administrator rights. Viewing works; applying, undoing and cleaning RAM need admin.", btn);
        }

        /// <summary>
        /// A strip across the top with a message and buttons on the right. The buttons keep their natural size and the
        /// strip grows to fit them, so their text is never cut off (it was at some display scales).
        /// </summary>
        static Panel BannerBar(Color back, string text, params Button[] buttons)
        {
            // A table sizes itself to its contents: message on the left, buttons on the right, both vertically centred.
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = back,
                Padding = new Padding(20, 7, 16, 7), MinimumSize = new Size(0, Theme.S(46)), ColumnCount = buttons.Length + 1, RowCount = 1
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Theme.Text, UseMnemonic = false, Text = text, Margin = new Padding(0, 0, 12, 0) };
            bar.Controls.Add(lbl, 0, 0);
            for (int i = 0; i < buttons.Length; i++)
            {
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons[i].Anchor = AnchorStyles.Right; buttons[i].Margin = new Padding(8, 0, 0, 0);
                bar.Controls.Add(buttons[i], i + 1, 0);
            }
            return bar;
        }

        string CurrentPage { get { return pages.First(p => p.Value.Visible).Key; } }

        /// <summary>Version from build.ps1 (version.txt), without the "+commit" suffix.</summary>
        public static string AppVersion
        {
            get
            {
                var a = typeof(MainForm).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                string v = a.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)a[0]).InformationalVersion
                                        : typeof(MainForm).Assembly.GetName().Version.ToString();
                return v.Split('+')[0];
            }
        }

        Button NavButton(string key, string text)
        {
            var b = new Button
            {
                Dock = DockStyle.Top, Height = Theme.S(42), Text = "    " + text, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.SidebarText, BackColor = Theme.Sidebar, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                UseMnemonic = false // show "&" literally ("Settings & about")
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.SidebarHover;
            b.Click += (s, e) => ShowPage(key);
            b.Paint += (s, e) =>
            {
                if (b.BackColor == Theme.SidebarActive)
                    using (var br = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(br, 0, 6, 4, b.Height - 12);
            };
            nav[key] = b;
            return b;
        }

        public void ShowPage(string key)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == key;
            foreach (var kv in nav)
            {
                bool active = kv.Key == key;
                kv.Value.BackColor = active ? Theme.SidebarActive : Theme.Sidebar;
                kv.Value.ForeColor = active ? Color.White : Theme.SidebarText;
                kv.Value.Font = active ? Theme.Bold : Theme.Base;
            }
            pages[key].OnShow();
        }
    }
}

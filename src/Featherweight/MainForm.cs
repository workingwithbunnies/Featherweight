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
            Size = new Size(1220, 800); MinimumSize = new Size(1000, 660);
            StartPosition = FormStartPosition.CenterScreen; Font = Theme.Base; BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            content.Controls.Add(pageHost);
            if (!Admin.IsAdmin) content.Controls.Add(Banner());

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 212, BackColor = Theme.Sidebar };
            Controls.Add(content);
            Controls.Add(sidebar);

            pages["Dashboard"] = new DashboardPage();
            pages["Optimize"] = new OptimizePage();
            pages["Backups"] = new BackupsPage();
            pages["Clean"] = new CleanPage();
            pages["Snapshots"] = new SnapshotsPage();
            pages["Security"] = new SecurityPage();
            pages["Settings"] = new SettingsPage();
            foreach (var p in pages.Values) { p.Visible = false; pageHost.Controls.Add(p); }

            var footer = new Label
            {
                Dock = DockStyle.Bottom, Height = 54, ForeColor = Color.FromArgb(130, 138, 150), Font = Theme.Small,
                Padding = new Padding(20, 0, 10, 12), TextAlign = ContentAlignment.BottomLeft,
                // No PC or user name here: people share screenshots in bug reports.
                Text = (Admin.IsAdmin ? "● Administrator" : "○ Standard user") + "\nVersion " + AppVersion
            };
            sidebar.Controls.Add(footer);
            var labels = new[] {
                Tuple.Create("Dashboard", "Dashboard"), Tuple.Create("Clean", "Clean RAM"), Tuple.Create("Optimize", "Optimize"),
                Tuple.Create("Backups", "Backups / Undo"), Tuple.Create("Snapshots", "Snapshots"), Tuple.Create("Security", "Security check"),
                Tuple.Create("Settings", "Settings & about") };
            foreach (var t in labels.Reverse()) sidebar.Controls.Add(NavButton(t.Item1, t.Item2));
            var brand = new Label
            {
                Dock = DockStyle.Top, Height = 78, Text = "Featherweight", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17f),
                Padding = new Padding(18, 0, 0, 8), TextAlign = ContentAlignment.BottomLeft
            };
            var tag = new Label { Dock = DockStyle.Top, Height = 30, Text = "memory · startup · security", ForeColor = Color.FromArgb(130, 138, 150), Font = Theme.Small, Padding = new Padding(21, 0, 0, 0) };
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
            Settings.LastUpdateCheck = DateTime.Now.ToString("s"); Settings.Save();
            if (info != null && Updates.IsNewer(info.Version, AppVersion) && info.Version != Settings.SkippedVersion)
                content.Controls.Add(UpdateBanner(info)); // docks on top, above the admin banner
        }

        Control UpdateBanner(UpdateInfo info)
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(222, 236, 252), Padding = new Padding(20, 8, 16, 8) };
            var get = Theme.Button("See what's new", true); get.Dock = DockStyle.Right;
            var later = Theme.Button("Not now"); later.Dock = DockStyle.Right;
            get.Click += (s, e) => { try { System.Diagnostics.Process.Start(info.Url); } catch { } };
            later.Click += (s, e) => { Settings.SkippedVersion = info.Version; Settings.Save(); bar.Visible = false; };
            var lbl = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text, UseMnemonic = false,
                Text = "Featherweight " + info.Version + " is available (you have " + AppVersion + ")."
            };
            bar.Controls.Add(lbl); bar.Controls.Add(later); bar.Controls.Add(get);
            return bar;
        }

        Control Banner()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Banner, Padding = new Padding(20, 8, 16, 8) };
            var btn = Theme.Button("Restart as administrator", true);
            btn.Dock = DockStyle.Right;
            btn.Click += (s, e) => { if (!Admin.RestartElevated(CurrentPage)) MessageBox.Show(this, "Windows did not grant administrator rights.", "Featherweight"); };
            var lbl = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text,
                Text = "Running without administrator rights. Viewing works; applying, undoing and cleaning RAM need admin."
            };
            bar.Controls.Add(lbl); bar.Controls.Add(btn);
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
                Dock = DockStyle.Top, Height = 42, Text = "    " + text, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Featherweight
{
    /// <summary>
    /// First-start screen: a header band with the logo, three cards (what it does / never does / good to know),
    /// and the update-check choice. Sizes are designed at 96 DPI and scaled by WinForms on bigger display scales.
    /// </summary>
    class WelcomeForm : Form
    {
        const int W = 840; // design width at 100 % display scale
        static readonly Color HeroFrom = Theme.Sidebar, HeroTo = Color.FromArgb(0, 84, 158);
        static readonly Color Blue = Theme.Accent, Green = Theme.Ok, Amber = Color.FromArgb(202, 128, 0);

        readonly CheckBox updates = new CheckBox
        {
            Text = "Check GitHub for new versions when Featherweight starts", Font = Theme.Bold,
            AutoSize = true, Checked = Settings.CheckUpdates, BackColor = Theme.Card
        };
        readonly List<InfoCard> cards = new List<InfoCard>();
        Bitmap logo;

        public WelcomeForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Welcome to Featherweight"; Font = Theme.Base; BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            logo = LargeAppIcon(128);

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Bg };
            root.Controls.Add(Hero());
            root.Controls.Add(Cards());
            root.Controls.Add(Footer());
            Controls.Add(root);

            Load += (s, e) => FitCards(); // after WinForms has scaled everything for this display
            FormClosed += (s, e) => { Settings.CheckUpdates = updates.Checked; Settings.FirstRunDone = true; Settings.Save(); if (logo != null) logo.Dispose(); };
        }

        // ---------------------------------------------------------------- header band
        Control Hero()
        {
            var hero = new DoubleBufferedPanel { Size = new Size(W, 156), Margin = Padding.Empty };
            hero.Paint += (s, e) =>
            {
                var g = e.Graphics; float k = hero.Width / (float)W; // scale for this display
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(hero.ClientRectangle, HeroFrom, HeroTo, 15f)) g.FillRectangle(bg, hero.ClientRectangle);
                // soft circles for a bit of depth
                using (var c = new SolidBrush(Color.FromArgb(18, Color.White)))
                {
                    g.FillEllipse(c, hero.Width - 260 * k, -90 * k, 300 * k, 300 * k);
                    g.FillEllipse(c, hero.Width - 120 * k, 70 * k, 180 * k, 180 * k);
                }
                // logo on a white tile
                var tile = new RectangleF(32 * k, 36 * k, 84 * k, 84 * k);
                using (var path = Rounded(tile, 18 * k)) using (var w = new SolidBrush(Color.White)) g.FillPath(w, path);
                if (logo != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(logo, tile.X + 12 * k, tile.Y + 12 * k, 60 * k, 60 * k); }

                int x = (int)(140 * k);
                using (var title = new Font("Segoe UI Semibold", 20f))
                    TextRenderer.DrawText(g, "Welcome to Featherweight", title, new Point(x, (int)(30 * k)), Color.White, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "See what's using your memory, switch off the background apps you don't need, and undo anything.",
                    Theme.Base, new Rectangle(x, (int)(76 * k), hero.Width - x - (int)(32 * k), (int)(44 * k)), Color.FromArgb(214, 226, 242),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                // three small promise pills
                int px = x, py = (int)(116 * k);
                foreach (var pill in new[] { "Free and open source", "No tracking", "Every change can be undone" })
                {
                    var sz = TextRenderer.MeasureText(g, pill, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
                    var r = new RectangleF(px, py, sz.Width + 20 * k, sz.Height + 8 * k);
                    using (var path = Rounded(r, r.Height / 2)) using (var b = new SolidBrush(Color.FromArgb(38, Color.White))) g.FillPath(b, path);
                    TextRenderer.DrawText(g, pill, Theme.Small, new Point((int)(px + 10 * k), (int)(py + 4 * k)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    px = (int)(r.Right + 8 * k);
                }
            };
            return hero;
        }

        // ---------------------------------------------------------------- the three cards
        Control Cards()
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty,
                                            Padding = new Padding(24, 22, 24, 22), BackColor = Theme.Bg };
            int cw = (W - 48 - 2 * 16) / 3;
            cards.Add(new InfoCard("What it does", Blue, Glyph("", "»"), cw, new[] {
                "Shows live memory use, grouped by app and by the company that made it.",
                "Finds the background apps on this PC, from any brand, and suggests what's safe to switch off.",
                "Backs up every setting before changing it, plus a System Restore point when Windows has those turned on." }));
            cards.Add(new InfoCard("What it never does", Green, Glyph("", "✓"), cw, new[] {
                "Change anything without showing you first and asking for permission.",
                "Turn off Windows Update, Microsoft Defender or other security features.",
                "Collect or send data about you. No tracking, no accounts." }));
            cards.Add(new InfoCard("Good to know", Amber, Glyph("", "i"), cw, new[] {
                "Free up RAM (on the Dashboard) is a quick boost before a game. The lasting win is fewer background apps (Optimize).",
                "Looking around is safe without admin rights. Changes ask to restart Featherweight as administrator.",
                "Your backups and logs stay on this PC. Settings & about shows where." }));
            cards[2].Margin = Padding.Empty;
            foreach (var c in cards) row.Controls.Add(c);
            return row;
        }

        /// <summary>Wraps the card text to the card width and gives all three cards the height of the tallest.</summary>
        void FitCards()
        {
            foreach (var c in cards) c.Fit();
            int h = cards.Max(c => c.Needed);
            foreach (var c in cards) c.Height = h;
        }

        // ---------------------------------------------------------------- update choice and Get started
        Control Footer()
        {
            var foot = new DoubleBufferedPanel { Size = new Size(W, 96), Margin = Padding.Empty, BackColor = Theme.Card };
            foot.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
            updates.Location = new Point(24, 18);
            var note = Theme.Label("At most once a day it asks GitHub for the latest version number and the newest item descriptions. That's Featherweight's only internet connection; " +
                                   "nothing about you or your PC is sent. You can change this later in Settings & about.", Theme.Small, Theme.Muted);
            note.Location = new Point(42, 44); note.MaximumSize = new Size(560, 0); note.BackColor = Theme.Card;
            var start = Theme.Button("Get started  →", true);
            start.Font = new Font("Segoe UI Semibold", 11f); start.Padding = new Padding(22, 8, 22, 8);
            start.Click += (s, e) => Close();
            AcceptButton = start;
            foot.Controls.AddRange(new Control[] { updates, note, start });
            foot.Layout += (s, e) =>
            {
                float k = foot.Width / (float)W;
                start.Location = new Point(foot.ClientSize.Width - (int)(24 * k) - start.Width, (foot.ClientSize.Height - start.Height) / 2);
                note.MaximumSize = new Size(Math.Max(100, start.Left - note.Left - (int)(24 * k)), 0);
                int need = note.Bottom + (int)(16 * k);
                if (foot.Height < need) foot.Height = need;
            };
            return foot;
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Icon font glyph (Segoe MDL2 Assets, on every Windows 10/11), or a plain character if it's missing.</summary>
        static KeyValuePair<Font, string> Glyph(string mdl2, string fallback)
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                if (fonts.Families.Any(f => f.Name == "Segoe MDL2 Assets")) return new KeyValuePair<Font, string>(new Font("Segoe MDL2 Assets", 13f), mdl2);
            return new KeyValuePair<Font, string>(new Font("Segoe UI Semibold", 13f), fallback);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// <summary>The app's own icon at a large size (Windows decodes the PNG frames that System.Drawing can't).</summary>
        static Bitmap LargeAppIcon(int size)
        {
            try
            {
                var h = new IntPtr[1]; var ids = new uint[1];
                if (PrivateExtractIcons(Application.ExecutablePath, 0, size, size, h, ids, 1, 0) == 1 && h[0] != IntPtr.Zero)
                    try { using (var ic = Icon.FromHandle(h[0])) return ic.ToBitmap(); } finally { DestroyIcon(h[0]); }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { return null; }
        }
    }

    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// <summary>White card with a coloured top edge, an icon badge, a title and short bullet points.</summary>
    class InfoCard : DoubleBufferedPanel
    {
        readonly Color accent; readonly KeyValuePair<Font, string> glyph;
        readonly Label title; readonly List<Label> lines = new List<Label>();

        public InfoCard(string heading, Color accent, KeyValuePair<Font, string> glyph, int width, string[] bullets)
        {
            this.accent = accent; this.glyph = glyph;
            Size = new Size(width, 260); BackColor = Theme.Card; Margin = new Padding(0, 0, 16, 0);
            title = Theme.Label(heading, Theme.H2); title.BackColor = Theme.Card; title.Location = new Point(66, 26);
            Controls.Add(title);
            int y = 76;
            foreach (var b in bullets)
            {
                var l = Theme.Label(b); l.BackColor = Theme.Card; l.Location = new Point(34, y); l.MaximumSize = new Size(width - 34 - 18, 0);
                Controls.Add(l); lines.Add(l); y += 60;
            }
        }

        /// <summary>Stacks the bullet lines under each other at this display's scale.</summary>
        public void Fit()
        {
            int y = title.Bottom + title.Height;
            foreach (var l in lines)
            {
                l.MaximumSize = new Size(ClientSize.Width - l.Left - title.Height, 0);
                l.Top = y; y = l.Bottom + title.Height / 2;
            }
        }

        public int Needed { get { return (lines.Count > 0 ? lines[lines.Count - 1].Bottom : title.Bottom) + title.Height; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int u = title.Height; // one "unit" that scales with the display
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, Width, Math.Max(3, u / 6));
            // icon badge
            int d = (int)(u * 1.7), bx = title.Left - d - u / 3, by = title.Top + (title.Height - d) / 2;
            using (var b = new SolidBrush(Color.FromArgb(30, accent))) g.FillEllipse(b, bx, by, d, d);
            TextRenderer.DrawText(g, glyph.Value, glyph.Key, new Rectangle(bx, by, d, d), accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            // coloured dots in front of each line
            foreach (var l in lines)
            {
                int dot = Math.Max(5, u / 3);
                using (var b = new SolidBrush(accent)) g.FillEllipse(b, l.Left - dot - u / 2, l.Top + (int)(u * 0.42) - dot / 2, dot, dot);
            }
        }
    }

    /// <summary>Settings and information about the app: version, updates, data, diagnostics, links.</summary>
    class SettingsPage : Page
    {
        readonly CheckBox updates = new CheckBox { Text = "Check for new versions when Featherweight starts (at most once a day)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        readonly CheckBox restore = new CheckBox { Text = "Create a System Restore point before changing settings (recommended)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        readonly Label updateStatus = Theme.Label("", Theme.Base, Theme.Muted);
        readonly Button checkNow = Theme.Button("Check for updates now");
        readonly Label descriptions = Theme.Label("", Theme.Small, Theme.Muted);

        public SettingsPage()
        {
            AutoScroll = true;
            var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };

            stack.Controls.Add(Section("Version"));
            stack.Controls.Add(Theme.Label("Featherweight " + MainForm.AppVersion + (Paths.Portable ? "  (portable)" : ""), Theme.Bold));
            var row = Theme.ButtonRow(); row.Dock = DockStyle.None;
            checkNow.Click += async (s, e) => await CheckNow();
            var releases = Theme.Button("All releases and what's new"); releases.Click += (s, e) => Open(Updates.ReleasesPage);
            row.Controls.Add(checkNow); row.Controls.Add(releases);
            stack.Controls.Add(row);
            stack.Controls.Add(updateStatus);
            descriptions.Text = "Item descriptions: version " + Knowledge.Version + " (" + Knowledge.Origin + "), " + Knowledge.Entries.Count + " reviewed entries.";
            stack.Controls.Add(descriptions);

            stack.Controls.Add(Section("Options"));
            updates.Checked = Settings.CheckUpdates;
            restore.Checked = Settings.RestorePoints;
            updates.CheckedChanged += (s, e) => { Settings.CheckUpdates = updates.Checked; Settings.Save(); };
            restore.CheckedChanged += (s, e) => { Settings.RestorePoints = restore.Checked; Settings.Save(); };
            stack.Controls.Add(updates);
            stack.Controls.Add(Note("The update check is Featherweight's only internet connection. It asks GitHub for the latest version number and downloads the newest item descriptions (a small text file). Nothing about you or your PC is sent, and it never downloads or installs programs by itself."));
            stack.Controls.Add(restore);
            stack.Controls.Add(Note("Windows keeps at most one restore point per day, so a recent one is reused. Featherweight's own backups protect every change either way."));
            if (RestorePoint.ProtectionOff)
            {
                var off = Theme.Label("System Protection is turned off on this PC, so Windows can't make restore points. Turning it on uses a little disk space " +
                                      "(you choose how much) and lets you roll Windows back to an earlier point.", Theme.Small, Theme.Warn);
                off.MaximumSize = new Size(760, 0); off.Margin = new Padding(20, 0, 0, 4);
                stack.Controls.Add(off);
                var openRow = Theme.ButtonRow(); openRow.Dock = DockStyle.None; openRow.Margin = new Padding(20, 0, 0, 8);
                var open = Theme.Button("Open System Protection settings"); open.Click += (s, e) => RestorePoint.OpenSettings();
                openRow.Controls.Add(open);
                stack.Controls.Add(openRow);
            }

            stack.Controls.Add(Section("Your data"));
            stack.Controls.Add(Theme.Label(Paths.Display));
            stack.Controls.Add(Note(Paths.Portable ? "Portable mode: everything is kept next to Featherweight.exe." :
                "Backups, logs and settings. To make Featherweight portable, put an empty file named \"portable\" next to Featherweight.exe."));
            var dataRow = Theme.ButtonRow(); dataRow.Dock = DockStyle.None;
            var openData = Theme.Button("Open data folder"); openData.Click += (s, e) => Paths.Open(Paths.Root);
            dataRow.Controls.Add(openData);
            stack.Controls.Add(dataRow);

            stack.Controls.Add(Section("Help and feedback"));
            var helpRow = Theme.ButtonRow(); helpRow.Dock = DockStyle.None;
            var diag = Theme.Button("Export diagnostics…", true); diag.Click += async (s, e) => await Diagnostics.Export(FindForm());
            var report = Theme.Button("Report a problem"); report.Click += (s, e) => Open(Updates.NewIssuePage);
            var project = Theme.Button("Project page on GitHub"); project.Click += (s, e) => Open(Updates.ProjectPage);
            var welcome = Theme.Button("Show welcome screen"); welcome.Click += (s, e) => { using (var w = new WelcomeForm()) w.ShowDialog(FindForm()); updates.Checked = Settings.CheckUpdates; };
            helpRow.Controls.AddRange(new Control[] { diag, report, project, welcome });
            stack.Controls.Add(helpRow);
            stack.Controls.Add(Note("Diagnostics creates a text file for bug reports, with your user name and PC name removed. It opens so you can read it before sharing."));

            stack.Controls.Add(Section("About"));
            stack.Controls.Add(Note("Free and open source under the MIT License. Made by @workingwithbunnies together with Claude, Anthropic's AI coding assistant. No warranty: read what each change does before applying it."));

            Controls.Add(stack);
            Controls.Add(Theme.Header("Settings & about", "Options, updates, your data, and how to get help."));
        }

        static Label Section(string text) { var l = Theme.Label(text, Theme.H2); l.Margin = new Padding(0, 18, 0, 6); return l; }
        static Label Note(string text) { var l = Theme.Label(text, Theme.Small, Theme.Muted); l.MaximumSize = new Size(760, 0); l.Margin = new Padding(20, 0, 0, 8); return l; }
        static void Open(string url) { try { Process.Start(url); } catch { } }

        async Task CheckNow()
        {
            checkNow.Enabled = false; updateStatus.Text = "Checking…"; updateStatus.ForeColor = Theme.Muted;
            string error = null;
            var info = await Task.Run(() => Updates.Check(out error));
            string kmsg = null;
            await Task.Run(() => Knowledge.TryUpdate(out kmsg));
            checkNow.Enabled = true;
            descriptions.Text = "Item descriptions: version " + Knowledge.Version + " (" + Knowledge.Origin + "), " + Knowledge.Entries.Count + " reviewed entries. Last check: " + kmsg + ".";
            Settings.LastUpdateCheck = DateTime.Now.ToString("s"); Settings.Save();
            if (info == null) { updateStatus.Text = error; return; }
            if (Updates.IsNewer(info.Version, MainForm.AppVersion))
            {
                updateStatus.Text = "Version " + info.Version + " is available."; updateStatus.ForeColor = Theme.Info;
                if (MessageBox.Show(FindForm(), "Featherweight " + info.Version + " is available (you have " + MainForm.AppVersion + ").\n\nOpen the download page?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Open(info.Url);
            }
            else { updateStatus.Text = "You have the latest version."; updateStatus.ForeColor = Theme.Ok; }
        }
    }
}

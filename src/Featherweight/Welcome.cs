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
    /// and the update-check choice. Sizes are designed at 100 % and multiplied by the display scale (Theme.S), like the
    /// main window. If the result is taller than the screen, the window gets wider (less wrapping) and, as a last
    /// resort, scrolls, so it never runs under the taskbar.
    /// </summary>
    class WelcomeForm : Form
    {
        const int W = 840; // design width at 100 % display scale
        static readonly Color HeroFrom = Theme.Sidebar, HeroTo = Color.FromArgb(0, 84, 158);
        static readonly Color Blue = Theme.Accent, Green = Theme.Ok, Amber = Color.FromArgb(202, 128, 0);
        const string Heading = "Welcome to Featherweight";
        const string Lead = "See what's using your memory, switch off the background apps you don't need, and undo anything.";
        static readonly string[] Pills = { "Free and open source", "No tracking", "Every change can be undone" };
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        readonly CheckBox updates = new CheckBox
        {
            Text = "Check GitHub for new versions when Featherweight starts", Font = Theme.Bold,
            AutoSize = true, Checked = Settings.CheckUpdates, BackColor = Theme.Card
        };
        readonly List<InfoCard> cards = new List<InfoCard>();
        readonly Font titleFont = new Font(Theme.H1.FontFamily, Theme.H1.SizeInPoints * 20f / 17f, Theme.H1.Style);
        readonly FlowLayoutPanel root;
        readonly Panel hero, foot;
        Bitmap logo;

        public WelcomeForm()
        {
            // Featherweight scales its own sizes; WinForms' automatic scaling stays off so nothing is scaled twice.
            AutoScaleMode = AutoScaleMode.None;
            Text = "Welcome to Featherweight"; Font = Theme.Base; BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.Manual;
            AutoScroll = true; // only used when even the widest layout is taller than the screen
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            logo = LargeAppIcon(128);

            root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Bg };
            hero = Hero(); foot = Footer();
            root.Controls.Add(hero);
            root.Controls.Add(Cards());
            root.Controls.Add(foot);
            Controls.Add(root);

            Load += (s, e) => FitToScreen();
            FormClosed += (s, e) => { Settings.CheckUpdates = updates.Checked; Settings.FirstRunDone = true; Settings.Save(); if (logo != null) logo.Dispose(); titleFont.Dispose(); };
        }

        // ---------------------------------------------------------------- header band
        /// <summary>Where the heading, the line under it and the pills go for a given width; returns the band's height.</summary>
        int HeroLayout(int width, out Rectangle lead, out int pillsTop)
        {
            int x = Theme.S(140), textW = width - x - Theme.S(32);
            int titleBottom = Theme.S(30) + TextRenderer.MeasureText(Heading, titleFont, Size.Empty, TextFormatFlags.NoPrefix).Height;
            int leadTop = Math.Max(Theme.S(76), titleBottom + Theme.S(2));
            int leadH = TextRenderer.MeasureText(Lead, Theme.Base, new Size(textW, 0), Wrap).Height;
            lead = new Rectangle(x, leadTop, textW, leadH);
            pillsTop = Math.Max(Theme.S(116), lead.Bottom + Theme.S(10));
            int pillH = TextRenderer.MeasureText(Pills[0], Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Height + Theme.S(8);
            return Math.Max(Theme.S(156), pillsTop + pillH + Theme.S(22));
        }

        Panel Hero()
        {
            var band = new DoubleBufferedPanel { Size = new Size(Theme.S(W), Theme.S(156)), Margin = Padding.Empty };
            band.Paint += (s, e) =>
            {
                var g = e.Graphics; float k = Theme.Dpi; // design sizes × display scale
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(band.ClientRectangle, HeroFrom, HeroTo, 15f)) g.FillRectangle(bg, band.ClientRectangle);
                // soft circles for a bit of depth
                using (var c = new SolidBrush(Color.FromArgb(18, Color.White)))
                {
                    g.FillEllipse(c, band.Width - 260 * k, -90 * k, 300 * k, 300 * k);
                    g.FillEllipse(c, band.Width - 120 * k, 70 * k, 180 * k, 180 * k);
                }
                // logo on a white tile, centred vertically
                float tileY = Math.Max(36 * k, (band.Height - 84 * k) / 2);
                var tile = new RectangleF(32 * k, tileY, 84 * k, 84 * k);
                using (var path = Rounded(tile, 18 * k)) using (var w = new SolidBrush(Color.White)) g.FillPath(w, path);
                if (logo != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(logo, tile.X + 12 * k, tile.Y + 12 * k, 60 * k, 60 * k); }

                Rectangle lead; int py;
                HeroLayout(band.Width, out lead, out py);
                TextRenderer.DrawText(g, Heading, titleFont, new Point(lead.X, Theme.S(30)), Color.White, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Lead, Theme.Base, lead, Color.FromArgb(214, 226, 242), Wrap);
                // three small promise pills
                int px = lead.X;
                foreach (var pill in Pills)
                {
                    var sz = TextRenderer.MeasureText(g, pill, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
                    var r = new RectangleF(px, py, sz.Width + 20 * k, sz.Height + 8 * k);
                    using (var path = Rounded(r, r.Height / 2)) using (var b = new SolidBrush(Color.FromArgb(38, Color.White))) g.FillPath(b, path);
                    TextRenderer.DrawText(g, pill, Theme.Small, new Point((int)(px + 10 * k), (int)(py + 4 * k)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    px = (int)(r.Right + 8 * k);
                }
            };
            return band;
        }

        // ---------------------------------------------------------------- the three cards
        Control Cards()
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty,
                                            Padding = new Padding(Theme.S(24), Theme.S(22), Theme.S(24), Theme.S(22)), BackColor = Theme.Bg };
            int cw = Theme.S((W - 48 - 2 * 16) / 3); // SetWidth adjusts this to the window
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

        /// <summary>Lays everything out for a window this wide: header height, card widths (text wrapped, all as tall as the tallest), footer.</summary>
        void SetWidth(int w)
        {
            Rectangle lead; int pillsTop;
            hero.Size = new Size(w, HeroLayout(w, out lead, out pillsTop));
            int pad = Theme.S(24), gap = Theme.S(16), inner = w - 2 * pad - 2 * gap, cw = inner / 3;
            for (int i = 0; i < cards.Count; i++) cards[i].Width = i < cards.Count - 1 ? cw : inner - 2 * cw;
            foreach (var c in cards) c.Fit();
            int h = cards.Max(c => c.Needed);
            foreach (var c in cards) c.Height = h;
            foot.Width = w; foot.PerformLayout();
            hero.Invalidate();
        }

        /// <summary>
        /// Picks the narrowest width (from the design width up) at which the whole screen fits on this monitor above the
        /// taskbar. Wider cards wrap into fewer lines, so the window gets shorter. If even the widest doesn't fit, it scrolls.
        /// </summary>
        void FitToScreen()
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var chrome = Size - ClientSize; // title bar and borders
            int maxW = area.Width - chrome.Width - Theme.S(24), maxH = area.Height - chrome.Height - Theme.S(12);
            int w = Math.Min(Theme.S(W), maxW);
            SetWidth(w);
            while (root.GetPreferredSize(Size.Empty).Height > maxH && w < maxW) { w = Math.Min(maxW, w + Theme.S(60)); SetWidth(w); }
            var need = root.GetPreferredSize(Size.Empty);
            root.Size = need;
            bool scroll = need.Height > maxH;
            ClientSize = new Size(need.Width + (scroll ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(need.Height, maxH));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        // ---------------------------------------------------------------- update choice and Get started
        Panel Footer()
        {
            var bar = new DoubleBufferedPanel { Size = new Size(Theme.S(W), Theme.S(96)), Margin = Padding.Empty, BackColor = Theme.Card };
            bar.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
            updates.Location = new Point(Theme.S(24), Theme.S(18));
            var note = Theme.Label("At most once a day it asks GitHub for the latest version number and the newest item descriptions. That's Featherweight's only internet connection; " +
                                   "nothing about you or your PC is sent. You can change this later in Settings & about.", Theme.Small, Theme.Muted);
            note.Location = new Point(Theme.S(42), Theme.S(44)); note.MaximumSize = new Size(Theme.S(560), 0); note.BackColor = Theme.Card;
            var start = Theme.Button("Get started  →", true);
            start.Font = Theme.H2; start.Padding = new Padding(Theme.S(22), Theme.S(8), Theme.S(22), Theme.S(8));
            start.Click += (s, e) => Close();
            AcceptButton = start;
            bar.Controls.AddRange(new Control[] { updates, note, start });
            bar.Layout += (s, e) =>
            {
                note.Top = updates.Bottom + Theme.S(4);
                start.Location = new Point(bar.ClientSize.Width - Theme.S(24) - start.Width, (bar.ClientSize.Height - start.Height) / 2);
                note.MaximumSize = new Size(Math.Max(Theme.S(100), start.Left - note.Left - Theme.S(24)), 0);
                int need = Math.Max(Theme.S(96), Math.Max(note.Bottom, start.Height + Theme.S(8)) + Theme.S(16));
                if (bar.Height != need) bar.Height = need;
            };
            return bar;
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Icon font glyph (Segoe MDL2 Assets, on every Windows 10/11), or a plain character if it's missing.</summary>
        static KeyValuePair<Font, string> Glyph(string mdl2, string fallback)
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                if (fonts.Families.Any(f => f.Name == "Segoe MDL2 Assets")) return new KeyValuePair<Font, string>(new Font("Segoe MDL2 Assets", Theme.Base.SizeInPoints * 4 / 3), mdl2);
            return new KeyValuePair<Font, string>(new Font("Segoe UI Semibold", Theme.Base.SizeInPoints * 4 / 3), fallback);
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
            Size = new Size(width, Theme.S(260)); BackColor = Theme.Card; Margin = new Padding(0, 0, Theme.S(16), 0);
            title = Theme.Label(heading, Theme.H2); title.BackColor = Theme.Card; title.Location = new Point(Theme.S(66), Theme.S(26));
            Controls.Add(title);
            int y = Theme.S(76);
            foreach (var b in bullets)
            {
                var l = Theme.Label(b); l.BackColor = Theme.Card; l.Location = new Point(Theme.S(34), y); l.MaximumSize = new Size(width - Theme.S(34 + 18), 0);
                Controls.Add(l); lines.Add(l); y += Theme.S(60);
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

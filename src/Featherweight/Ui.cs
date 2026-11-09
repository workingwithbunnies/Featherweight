using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Featherweight
{
    static class Theme
    {
        // The current theme's colours (see Themes.cs). Set by Use() before a window is built.
        public static Palette Current;
        public static Look Look;
        public static Color Sidebar, SidebarTo, SidebarHover, SidebarActive, SidebarText, SidebarActiveText, SidebarMuted, SidebarBar;
        public static Color Accent, Bg, Card, Border, Field, Text, Muted, Ok, Warn, Bad, Info, Banner, BannerText, InfoBanner, Commit;

        /// <summary>Switches every colour and font to this theme. Windows built afterwards use it.</summary>
        public static void Use(Palette p)
        {
            Current = p; Look = p.Look;
            Sidebar = p.Sidebar; SidebarTo = p.SidebarTo; SidebarHover = p.SidebarHover; SidebarActive = p.SidebarActive;
            SidebarText = p.SidebarText; SidebarActiveText = p.SidebarActiveText; SidebarMuted = p.SidebarMuted; SidebarBar = p.SidebarBar;
            Accent = p.Accent; Bg = p.Bg; Card = p.Card; Border = p.Border; Field = p.Field; Text = p.Text; Muted = p.Muted;
            Ok = p.Ok; Warn = p.Warn; Bad = p.Bad; Info = p.Info; Banner = p.Banner; BannerText = p.BannerText; InfoBanner = p.InfoBanner; Commit = p.Commit;
            Base = new Font(p.Face, 9.75f);
            Small = new Font(p.Face, 8.75f);
            Bold = new Font(p.BoldFace, 9.75f, p.BoldStyle);
            H1 = new Font(p.BoldFace, 17f, p.BoldStyle);
            H2 = new Font(p.BoldFace, 11.5f, p.BoldStyle);
            Big = new Font(p.BoldFace, 22f, p.BoldStyle);
        }

        static Theme() { Use(Palettes.All[0]); }

        /// <summary>
        /// Display scale: 1.0 at 100 %, 1.25 at 125 %… Featherweight draws itself sharply at any scale (DPI-aware), so text
        /// grows with the display but plain pixel numbers don't: fixed widths and heights go through S() to grow with it.
        /// </summary>
        public static float Dpi = ReadDpi();
        static float ReadDpi() { try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }
        public static int S(int px) { return (int)Math.Round(px * Dpi); }
        public static Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        public static Font Base, Small, Bold, H1, H2, Big;
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        public static Button Button(string text, bool primary = false)
        {
            var p = Current;
            var b = new ThemedButton
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Primary = primary,
                Font = primary ? Bold : Base, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0),
                BackColor = primary ? p.PrimaryTop : p.ButtonTop, ForeColor = primary ? p.PrimaryText : p.ButtonText, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            if (Look == Look.Classic) b.Padding = new Padding(12, 5, 12, 5); // room for the bevel
            b.FlatAppearance.BorderColor = primary ? p.PrimaryBorder : p.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? p.PrimaryHover : p.ButtonHover;
            // Flat buttons with custom colours don't grey out by themselves; make disabled ones look disabled.
            b.EnabledChanged += (s, e) =>
            {
                b.BackColor = b.Enabled ? (primary ? p.PrimaryTop : p.ButtonTop) : (primary ? p.Disabled : p.ButtonTop);
                b.ForeColor = b.Enabled ? (primary ? p.PrimaryText : p.ButtonText) : p.DisabledText;
                b.FlatAppearance.BorderColor = b.Enabled ? (primary ? p.PrimaryBorder : p.ButtonBorder) : p.Disabled;
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
                b.Invalidate();
            };
            return b;
        }

        public static Label Label(string text, Font font = null, Color? color = null)
        {
            // UseMnemonic off so "&" shows literally (e.g. "Dell & Alienware").
            return new Label { Text = text, AutoSize = true, Font = font ?? Base, ForeColor = color ?? Text, Margin = new Padding(0, 0, 0, 4), UseMnemonic = false };
        }

        /// <summary>Page header: big title + one line of explanation.</summary>
        public static Control Header(string title, string subtitle)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 0, 10) };
            p.Controls.Add(Label(title, H1));
            var sub = Label(subtitle, Base, Muted); sub.MaximumSize = new Size(S(900), 0);
            p.Controls.Add(sub);
            return p;
        }

        public static FlowLayoutPanel ButtonRow()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 8) };
        }

        public static ListView List(params object[] columns) // name, width, name, width…
        {
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, Font = Base,
                BorderStyle = BorderStyle.FixedSingle, GridLines = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Field, ForeColor = Text, OwnerDraw = true
            };
            // Only the column headers are drawn by Featherweight (in the theme's style); rows are drawn by Windows as usual.
            lv.DrawColumnHeader += (s, e) => Ui.PaintHeader(lv, e);
            lv.DrawItem += (s, e) => e.DrawDefault = !(Current.Dark && e.Item.Selected);
            lv.DrawSubItem += (s, e) => Ui.PaintSubItem(lv, e);
            for (int i = 0; i < columns.Length; i += 2)
            {
                int w = (int)columns[i + 1];
                var ch = lv.Columns.Add((string)columns[i], w < 0 ? S(100) : S(w));
                if (w < 0) ch.Tag = "fill";
            }
            // Last "fill" column takes the remaining width.
            lv.Resize += (s, e) => FillColumn(lv);
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(lv, true, null);
            return lv;
        }

        public static void FillColumn(ListView lv)
        {
            var fill = lv.Columns.Cast<ColumnHeader>().FirstOrDefault(c => (c.Tag as string) == "fill");
            if (fill == null) return;
            int other = lv.Columns.Cast<ColumnHeader>().Where(c => c != fill).Sum(c => c.Width);
            fill.Width = Math.Max(S(120), lv.ClientSize.Width - other); // up to the edge, so the themed header has no gap
        }

        public static TextBox LogBox()
        {
            return new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 150,
                Font = Mono, BackColor = Field, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, WordWrap = false
            };
        }

        public static void Append(TextBox box, string line)
        {
            if (box.InvokeRequired) { box.BeginInvoke(new Action(() => Append(box, line))); return; }
            box.AppendText(line + Environment.NewLine);
        }

        public static string MB(double mb)
        {
            if (double.IsNaN(mb)) return "–"; // value not available (e.g. needs admin)
            return mb >= 1024 ? (mb / 1024).ToString("0.0") + " GB" : mb.ToString("0") + " MB";
        }
        public static string Delta(double mb) { return double.IsNaN(mb) ? "–" : (mb > 0 ? "+" : mb < 0 ? "−" : "") + MB(Math.Abs(mb)); }
    }

    /// <summary>White rounded card with a caption, a big value and a sub-line.</summary>
    class Card : Panel
    {
        readonly Label caption, value, sub;
        public Card(string title)
        {
            BackColor = Ui.Rounds ? Theme.Bg : Theme.Card; Padding = Theme.Pad(14, 10, 14, 10); Margin = Theme.Pad(0, 0, 12, 12); Dock = DockStyle.Fill;
            ResizeRedraw = true; // otherwise the old border stays painted inside the card after the window grows
            caption = Theme.Label(title.ToUpper(), Theme.Small, Theme.Muted);
            value = Theme.Label("–", Theme.Big); value.Margin = new Padding(0, 2, 0, 2);
            sub = Theme.Label("", Theme.Small, Theme.Muted);
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card };
            stack.Controls.AddRange(new Control[] { caption, value, sub });
            Controls.Add(stack);
        }
        public void Set(string v, string s, Color? color = null) { value.Text = v; sub.Text = s; value.ForeColor = color ?? Theme.Text; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.PaintFrame(this, e.Graphics);
        }
    }

    /// <summary>Scrolling line chart of memory-in-use % and commit % (last few minutes).</summary>
    class Graph : Control
    {
        readonly List<double> used = new List<double>(), commit = new List<double>();
        const int Max = 180;
        public Graph() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Current.GraphBack; Height = Theme.S(150); Dock = DockStyle.Top; Margin = new Padding(0, 0, 0, 12); }

        public void Push(double usedPct, double commitPct)
        {
            used.Add(usedPct); commit.Add(commitPct);
            if (used.Count > Max) { used.RemoveAt(0); commit.RemoveAt(0); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var p = Theme.Current;
            Func<int, int> k = Theme.S;
            var r = new Rectangle(k(44), k(26), Width - k(58), Height - k(38));
            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (var grid = new Pen(p.GraphGrid))
            using (var muted = new SolidBrush(p.GraphText))
                for (int pct = 0; pct <= 100; pct += 25)
                {
                    int y = r.Bottom - r.Height * pct / 100;
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                    g.DrawString(pct + "%", Theme.Small, muted, k(8), y - k(8));
                }
            Draw(g, r, commit, p.GraphCommit, false);
            Draw(g, r, used, p.GraphUsed, true);
            using (var a = new SolidBrush(p.GraphUsed)) using (var c = new SolidBrush(p.GraphCommit)) using (var t = new SolidBrush(p.GraphText))
            {
                const string usedLabel = "Memory in use", commitLabel = "Committed (of limit)";
                string span = "last " + Max + " s";
                int x = r.Left + k(2);
                g.FillRectangle(a, x, k(9), k(10), k(10)); x += k(14);
                g.DrawString(usedLabel, Theme.Small, t, x, k(6)); x += (int)g.MeasureString(usedLabel, Theme.Small).Width + k(14);
                g.FillRectangle(c, x, k(9), k(10), k(10)); x += k(14);
                g.DrawString(commitLabel, Theme.Small, t, x, k(6));
                g.DrawString(span, Theme.Small, t, Width - g.MeasureString(span, Theme.Small).Width - k(10), k(6));
            }
        }

        static void Draw(Graphics g, Rectangle r, List<double> data, Color color, bool fill)
        {
            if (data.Count < 2) return;
            var pts = new PointF[data.Count];
            for (int i = 0; i < data.Count; i++)
                pts[i] = new PointF(r.Right - (data.Count - 1 - i) * (float)r.Width / (Max - 1), r.Bottom - (float)(r.Height * Math.Min(100, data[i]) / 100));
            if (fill)
            {
                var poly = new List<PointF>(pts) { new PointF(pts[pts.Length - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom) };
                using (var b = new SolidBrush(Color.FromArgb(40, color))) g.FillPolygon(b, poly.ToArray());
            }
            using (var pen = new Pen(color, 2f)) g.DrawLines(pen, pts);
        }
    }

    /// <summary>Base class for pages; OnShow runs every time the page is navigated to.</summary>
    class Page : UserControl
    {
        public Page() { Dock = DockStyle.Fill; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Base; Padding = new Padding(24, 18, 24, 18); }
        public virtual void OnShow() { }
    }
}

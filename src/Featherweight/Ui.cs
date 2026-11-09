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
        public static readonly Color Sidebar = Color.FromArgb(24, 28, 36), SidebarHover = Color.FromArgb(38, 44, 56), SidebarActive = Color.FromArgb(48, 56, 72);
        public static readonly Color SidebarText = Color.FromArgb(200, 206, 216);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212), AccentDark = Color.FromArgb(0, 95, 170);
        public static readonly Color Bg = Color.FromArgb(244, 246, 249), Card = Color.White, Border = Color.FromArgb(222, 226, 232);
        public static readonly Color Text = Color.FromArgb(28, 30, 34), Muted = Color.FromArgb(105, 112, 122);
        public static readonly Color Ok = Color.FromArgb(16, 124, 16), Warn = Color.FromArgb(196, 98, 0), Bad = Color.FromArgb(196, 43, 28), Info = Color.FromArgb(0, 99, 177);
        public static readonly Color Banner = Color.FromArgb(255, 244, 206), Commit = Color.FromArgb(230, 140, 20);

        /// <summary>
        /// Display scale: 1.0 at 100 %, 1.25 at 125 %… Featherweight draws itself sharply at any scale (DPI-aware), so text
        /// grows with the display but plain pixel numbers don't: fixed widths and heights go through S() to grow with it.
        /// </summary>
        public static float Dpi = ReadDpi();
        static float ReadDpi() { try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }
        public static int S(int px) { return (int)Math.Round(px * Dpi); }

        public static readonly Font Base = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 17f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 22f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        public static Button Button(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Font = primary ? Bold : Base, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0),
                BackColor = primary ? Accent : Card, ForeColor = primary ? Color.White : Text, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentDark : Color.FromArgb(234, 238, 244);
            // Flat buttons with custom colours don't grey out by themselves; make disabled ones look disabled.
            b.EnabledChanged += (s, e) =>
            {
                Color disabledBack = Color.FromArgb(226, 230, 236);
                b.BackColor = b.Enabled ? (primary ? Accent : Card) : (primary ? disabledBack : Card);
                b.FlatAppearance.BorderColor = b.Enabled ? (primary ? Accent : Border) : disabledBack;
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
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
                BorderStyle = BorderStyle.FixedSingle, GridLines = false, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
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
            fill.Width = Math.Max(S(120), lv.ClientSize.Width - other - 4);
        }

        public static TextBox LogBox()
        {
            return new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 150,
                Font = Mono, BackColor = Color.FromArgb(250, 251, 253), BorderStyle = BorderStyle.FixedSingle, WordWrap = false
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
            BackColor = Theme.Card; Padding = new Padding(14, 10, 14, 10); Margin = new Padding(0, 0, 12, 12); Dock = DockStyle.Fill;
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
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Scrolling line chart of memory-in-use % and commit % (last few minutes).</summary>
    class Graph : Control
    {
        readonly List<double> used = new List<double>(), commit = new List<double>();
        const int Max = 180;
        public Graph() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Card; Height = 150; Dock = DockStyle.Top; Margin = new Padding(0, 0, 0, 12); }

        public void Push(double usedPct, double commitPct)
        {
            used.Add(usedPct); commit.Add(commitPct);
            if (used.Count > Max) { used.RemoveAt(0); commit.RemoveAt(0); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(44, 26, Width - 58, Height - 38);
            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (var grid = new Pen(Color.FromArgb(236, 239, 243)))
            using (var muted = new SolidBrush(Theme.Muted))
                for (int p = 0; p <= 100; p += 25)
                {
                    int y = r.Bottom - r.Height * p / 100;
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                    g.DrawString(p + "%", Theme.Small, muted, 8, y - 8);
                }
            Draw(g, r, commit, Theme.Commit, false);
            Draw(g, r, used, Theme.Accent, true);
            using (var a = new SolidBrush(Theme.Accent)) using (var c = new SolidBrush(Theme.Commit)) using (var t = new SolidBrush(Theme.Text))
            {
                g.FillRectangle(a, 46, 9, 10, 10); g.DrawString("Memory in use", Theme.Small, t, 60, 6);
                g.FillRectangle(c, 170, 9, 10, 10); g.DrawString("Committed (of limit)", Theme.Small, t, 184, 6);
                g.DrawString("last " + Max + " s", Theme.Small, new SolidBrush(Theme.Muted), Width - 80, 6);
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
        public Page() { Dock = DockStyle.Fill; BackColor = Theme.Bg; Font = Theme.Base; Padding = new Padding(24, 18, 24, 18); }
        public virtual void OnShow() { }
    }
}

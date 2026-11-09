using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Featherweight
{
    /// <summary>How buttons, cards and list headers are drawn: flat (modern), bevelled (95/98), rounded gradient (XP), glossy (7).</summary>
    enum Look { Flat, Classic, Luna, Aero }

    /// <summary>
    /// One theme: every colour and font the app uses. Themes only change how things look, never what the app does.
    /// The look is applied when the window is built; switching rebuilds the window in place (Settings &amp; about).
    /// </summary>
    class Palette
    {
        public string Id, Name, Blurb;
        public Look Look = Look.Flat;
        public bool Dark;                       // dark scrollbars and menus
        public string Face = "Segoe UI", BoldFace = "Segoe UI Semibold"; public FontStyle BoldStyle = FontStyle.Regular;

        public Color Bg, Card, Border, Field, Text, Muted;
        public Color Ok, Warn, Bad, Info, Accent, Commit;
        public Color Banner, BannerText, InfoBanner;
        public Color Sidebar, SidebarTo, SidebarHover, SidebarActive, SidebarText, SidebarActiveText, SidebarMuted, SidebarBar;
        public bool BrandBand;                  // app name on a title-bar style band (Classic)
        public Color HeroFrom, HeroTo;          // welcome screen header, brand band
        public Color PrimaryTop, PrimaryBottom, PrimaryHover, PrimaryText, PrimaryBorder;
        public Color ButtonTop, ButtonBottom, ButtonHover, ButtonText, ButtonBorder;
        public Color Disabled, DisabledText;
        public Color Select;                    // selected list row (dark themes draw it themselves)
        public Color HeaderTop, HeaderBottom, HeaderText;
        public Color GraphBack, GraphGrid, GraphUsed, GraphCommit, GraphText;
    }

    static class Palettes
    {
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }

        public static readonly List<Palette> All = new List<Palette>
        {
            // The original look.
            new Palette {
                Id = "featherweight", Name = "Featherweight", Blurb = "The standard look: clean and light.",
                Bg = C(244, 246, 249), Card = Color.White, Border = C(222, 226, 232), Field = Color.White, Text = C(28, 30, 34), Muted = C(105, 112, 122),
                Ok = C(16, 124, 16), Warn = C(196, 98, 0), Bad = C(196, 43, 28), Info = C(0, 99, 177), Accent = C(0, 120, 212), Commit = C(230, 140, 20),
                Banner = C(255, 244, 206), BannerText = C(28, 30, 34), InfoBanner = C(222, 236, 252),
                Sidebar = C(24, 28, 36), SidebarTo = C(24, 28, 36), SidebarHover = C(38, 44, 56), SidebarActive = C(48, 56, 72),
                SidebarText = C(200, 206, 216), SidebarActiveText = Color.White, SidebarMuted = C(130, 138, 150), SidebarBar = C(0, 120, 212),
                HeroFrom = C(24, 28, 36), HeroTo = C(0, 84, 158),
                PrimaryTop = C(0, 120, 212), PrimaryBottom = C(0, 120, 212), PrimaryHover = C(0, 95, 170), PrimaryText = Color.White, PrimaryBorder = C(0, 120, 212),
                ButtonTop = Color.White, ButtonBottom = Color.White, ButtonHover = C(234, 238, 244), ButtonText = C(28, 30, 34), ButtonBorder = C(222, 226, 232),
                Disabled = C(226, 230, 236), DisabledText = C(150, 156, 165),
                HeaderTop = Color.White, HeaderBottom = Color.White, HeaderText = C(28, 30, 34),
                GraphBack = Color.White, GraphGrid = C(236, 239, 243), GraphUsed = C(0, 120, 212), GraphCommit = C(230, 140, 20), GraphText = C(105, 112, 122) },

            // Windows 95/98: grey bevels, navy highlights, a title-bar band, and a Task Manager style graph.
            new Palette {
                Id = "classic", Name = "Classic 9x", Blurb = "Grey bevelled buttons and navy highlights, inspired by Windows 95 and 98.",
                Look = Look.Classic, Face = "Microsoft Sans Serif", BoldFace = "Microsoft Sans Serif", BoldStyle = FontStyle.Bold,
                Bg = C(192, 192, 192), Card = C(192, 192, 192), Border = C(128, 128, 128), Field = Color.White, Text = Color.Black, Muted = C(64, 64, 64),
                Ok = C(0, 128, 0), Warn = C(160, 80, 0), Bad = C(192, 0, 0), Info = C(0, 0, 128), Accent = C(0, 0, 128), Commit = C(128, 0, 128),
                Banner = C(255, 255, 225), BannerText = Color.Black, InfoBanner = C(255, 255, 225),
                Sidebar = C(192, 192, 192), SidebarTo = C(192, 192, 192), SidebarHover = C(224, 224, 224), SidebarActive = C(0, 0, 128),
                SidebarText = Color.Black, SidebarActiveText = Color.White, SidebarMuted = C(64, 64, 64), SidebarBar = C(0, 0, 128),
                BrandBand = true, HeroFrom = C(0, 0, 128), HeroTo = C(16, 132, 208),
                PrimaryTop = C(192, 192, 192), PrimaryBottom = C(192, 192, 192), PrimaryHover = C(192, 192, 192), PrimaryText = Color.Black, PrimaryBorder = Color.Black,
                ButtonTop = C(192, 192, 192), ButtonBottom = C(192, 192, 192), ButtonHover = C(192, 192, 192), ButtonText = Color.Black, ButtonBorder = Color.Black,
                Disabled = C(192, 192, 192), DisabledText = C(128, 128, 128),
                HeaderTop = C(192, 192, 192), HeaderBottom = C(192, 192, 192), HeaderText = Color.Black,
                GraphBack = Color.Black, GraphGrid = C(0, 128, 64), GraphUsed = C(0, 255, 0), GraphCommit = C(255, 255, 0), GraphText = C(0, 220, 0) },

            // Windows XP: beige dialogs, a blue task-pane sidebar, rounded buttons and a green Start-button style main action.
            new Palette {
                Id = "xp", Name = "XP Blue", Blurb = "Beige windows, a blue side pane and rounded buttons, inspired by Windows XP.",
                Look = Look.Luna, Face = "Tahoma", BoldFace = "Tahoma", BoldStyle = FontStyle.Bold,
                Bg = C(236, 233, 216), Card = Color.White, Border = C(127, 157, 185), Field = Color.White, Text = Color.Black, Muted = C(90, 90, 90),
                Ok = C(0, 128, 0), Warn = C(196, 98, 0), Bad = C(192, 0, 0), Info = C(0, 70, 213), Accent = C(0, 84, 227), Commit = C(255, 140, 0),
                Banner = C(255, 255, 225), BannerText = Color.Black, InfoBanner = C(255, 255, 225),
                Sidebar = C(123, 162, 231), SidebarTo = C(99, 117, 214), SidebarHover = A(60, 255, 255, 255), SidebarActive = C(49, 106, 197),
                SidebarText = Color.White, SidebarActiveText = Color.White, SidebarMuted = C(222, 230, 250), SidebarBar = C(248, 179, 48),
                HeroFrom = C(0, 84, 227), HeroTo = C(61, 149, 255),
                PrimaryTop = C(110, 196, 84), PrimaryBottom = C(49, 137, 38), PrimaryHover = C(130, 210, 100), PrimaryText = Color.White, PrimaryBorder = C(33, 95, 27),
                ButtonTop = Color.White, ButtonBottom = C(236, 235, 230), ButtonHover = C(248, 179, 48), ButtonText = Color.Black, ButtonBorder = C(0, 60, 116),
                Disabled = C(245, 244, 234), DisabledText = C(161, 161, 146),
                HeaderTop = Color.White, HeaderBottom = C(235, 234, 219), HeaderText = Color.Black,
                GraphBack = Color.White, GraphGrid = C(226, 226, 226), GraphUsed = C(0, 84, 227), GraphCommit = C(255, 140, 0), GraphText = C(90, 90, 90) },

            // Windows 7 / Frutiger Aero: sky blues, glass sidebar, glossy buttons, a touch of green.
            new Palette {
                Id = "aero", Name = "Frutiger Aero", Blurb = "Sky blue glass and glossy buttons, inspired by Windows 7 and Vista.",
                Look = Look.Aero,
                Bg = C(233, 242, 251), Card = Color.White, Border = C(170, 200, 230), Field = Color.White, Text = C(30, 40, 60), Muted = C(80, 100, 125),
                Ok = C(30, 140, 40), Warn = C(200, 110, 0), Bad = C(200, 40, 30), Info = C(20, 110, 200), Accent = C(30, 144, 255), Commit = C(90, 190, 60),
                Banner = C(255, 248, 215), BannerText = C(30, 40, 60), InfoBanner = C(225, 240, 253),
                Sidebar = C(19, 94, 165), SidebarTo = C(76, 170, 229), SidebarHover = A(50, 255, 255, 255), SidebarActive = A(95, 255, 255, 255),
                SidebarText = Color.White, SidebarActiveText = Color.White, SidebarMuted = C(205, 228, 250), SidebarBar = C(150, 230, 110),
                HeroFrom = C(23, 98, 173), HeroTo = C(84, 180, 231),
                PrimaryTop = C(120, 195, 250), PrimaryBottom = C(20, 112, 205), PrimaryHover = C(150, 210, 255), PrimaryText = Color.White, PrimaryBorder = C(16, 80, 150),
                ButtonTop = C(242, 242, 242), ButtonBottom = C(207, 207, 207), ButtonHover = C(190, 230, 253), ButtonText = C(30, 40, 60), ButtonBorder = C(112, 112, 112),
                Disabled = C(244, 244, 244), DisabledText = C(160, 160, 160),
                HeaderTop = Color.White, HeaderBottom = C(238, 243, 250), HeaderText = C(30, 40, 60),
                GraphBack = Color.White, GraphGrid = C(225, 235, 245), GraphUsed = C(30, 144, 255), GraphCommit = C(90, 190, 60), GraphText = C(80, 100, 125) },

            // Dark mode.
            new Palette {
                Id = "midnight", Name = "Midnight", Blurb = "Dark mode: easy on the eyes at night.", Dark = true,
                Bg = C(24, 26, 31), Card = C(34, 37, 44), Border = C(58, 63, 72), Field = C(30, 33, 39), Text = C(230, 233, 238), Muted = C(150, 158, 170),
                Ok = C(80, 200, 120), Warn = C(240, 170, 60), Bad = C(255, 105, 95), Info = C(110, 175, 255), Accent = C(77, 163, 255), Commit = C(240, 170, 60),
                Banner = C(62, 50, 22), BannerText = C(255, 230, 170), InfoBanner = C(28, 48, 78),
                Sidebar = C(16, 18, 22), SidebarTo = C(16, 18, 22), SidebarHover = C(32, 36, 44), SidebarActive = C(44, 50, 62),
                SidebarText = C(190, 196, 206), SidebarActiveText = Color.White, SidebarMuted = C(120, 128, 140), SidebarBar = C(77, 163, 255),
                HeroFrom = C(16, 18, 22), HeroTo = C(20, 60, 110),
                PrimaryTop = C(37, 117, 220), PrimaryBottom = C(37, 117, 220), PrimaryHover = C(60, 135, 235), PrimaryText = Color.White, PrimaryBorder = C(37, 117, 220),
                ButtonTop = C(44, 48, 56), ButtonBottom = C(44, 48, 56), ButtonHover = C(58, 63, 74), ButtonText = C(230, 233, 238), ButtonBorder = C(64, 70, 80),
                Disabled = C(38, 41, 48), DisabledText = C(128, 134, 145), Select = C(44, 74, 120),
                HeaderTop = C(34, 37, 44), HeaderBottom = C(34, 37, 44), HeaderText = C(200, 205, 215),
                GraphBack = C(34, 37, 44), GraphGrid = C(50, 54, 62), GraphUsed = C(77, 163, 255), GraphCommit = C(240, 170, 60), GraphText = C(150, 158, 170) },

            // Neon retro for late-night gaming.
            new Palette {
                Id = "synthwave", Name = "Synthwave", Blurb = "Neon pink and cyan on deep purple, straight out of the 80s.", Dark = true,
                Bg = C(24, 14, 42), Card = C(36, 22, 62), Border = C(104, 58, 156), Field = C(30, 18, 52), Text = C(245, 235, 255), Muted = C(182, 162, 215),
                Ok = C(57, 255, 170), Warn = C(255, 200, 70), Bad = C(255, 92, 122), Info = C(0, 229, 255), Accent = C(255, 56, 172), Commit = C(0, 229, 255),
                Banner = C(72, 30, 96), BannerText = C(255, 225, 250), InfoBanner = C(38, 30, 92),
                Sidebar = C(16, 8, 32), SidebarTo = C(72, 18, 96), SidebarHover = A(60, 255, 56, 172), SidebarActive = A(110, 255, 56, 172),
                SidebarText = C(232, 214, 255), SidebarActiveText = Color.White, SidebarMuted = C(170, 140, 210), SidebarBar = C(0, 229, 255),
                HeroFrom = C(60, 14, 110), HeroTo = C(255, 56, 172),
                PrimaryTop = C(255, 56, 172), PrimaryBottom = C(255, 56, 172), PrimaryHover = C(255, 100, 195), PrimaryText = Color.White, PrimaryBorder = C(255, 56, 172),
                ButtonTop = C(36, 22, 62), ButtonBottom = C(36, 22, 62), ButtonHover = C(56, 30, 92), ButtonText = C(245, 235, 255), ButtonBorder = C(0, 229, 255),
                Disabled = C(40, 28, 64), DisabledText = C(150, 130, 180), Select = C(96, 32, 112),
                HeaderTop = C(36, 22, 62), HeaderBottom = C(36, 22, 62), HeaderText = C(0, 229, 255),
                GraphBack = C(18, 8, 34), GraphGrid = C(70, 30, 104), GraphUsed = C(255, 56, 172), GraphCommit = C(0, 229, 255), GraphText = C(182, 162, 215) },
        };

        public static Palette Find(string id) { return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0]; }
    }

    /// <summary>Button that draws itself in the theme's style (bevelled, XP rounded, Windows 7 glossy); the flat look uses the normal flat button.</summary>
    class ThemedButton : Button
    {
        public bool Primary;
        bool hover, down;

        public ThemedButton() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Theme.Look == Look.Flat) { base.OnPaint(e); return; }
            var p = Theme.Current; var g = e.Graphics; var r = ClientRectangle;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, r);
            Color text = !Enabled ? p.DisabledText : Primary ? p.PrimaryText : p.ButtonText;
            var textRect = r;

            if (Theme.Look == Look.Classic)
            {
                using (var face = new SolidBrush(p.ButtonTop)) g.FillRectangle(face, r);
                var inner = r;
                if ((Primary || Focused) && Enabled) { g.DrawRectangle(Pens.Black, 0, 0, Width - 1, Height - 1); inner.Inflate(-1, -1); }
                if (down) { using (var dark = new Pen(Color.FromArgb(128, 128, 128))) g.DrawRectangle(dark, inner.X, inner.Y, inner.Width - 1, inner.Height - 1); textRect.Offset(1, 1); }
                else Ui.Bevel(g, inner, true);
                if (!Enabled)
                {   // the classic engraved look for disabled text
                    var shifted = textRect; shifted.Offset(1, 1);
                    TextRenderer.DrawText(g, Text, Font, shifted, Color.White, Flags);
                }
                TextRenderer.DrawText(g, Text, Font, textRect, text, Flags);
                if (Focused && ShowFocusCues && Enabled) { var f = r; f.Inflate(-Theme.S(4), -Theme.S(4)); ControlPaint.DrawFocusRectangle(g, f, Color.Black, p.ButtonTop); }
                return;
            }

            // XP (Luna) and Windows 7 (Aero): rounded, gradient or glossy
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Theme.S(3);
            Color top, bottom, border;
            if (!Enabled) { top = bottom = p.Disabled; border = Color.FromArgb(201, 199, 186); }
            else if (Primary) { top = hover ? p.PrimaryHover : p.PrimaryTop; bottom = p.PrimaryBottom; border = p.PrimaryBorder; }
            else if (Theme.Look == Look.Aero && (hover || down)) { top = down ? Color.FromArgb(196, 229, 246) : Color.FromArgb(234, 246, 253); bottom = down ? Color.FromArgb(104, 179, 219) : p.ButtonHover; border = Color.FromArgb(60, 127, 177); }
            else { top = down ? p.ButtonBottom : p.ButtonTop; bottom = down ? p.ButtonTop : p.ButtonBottom; border = p.ButtonBorder; }

            using (var path = Ui.Rounded(box, radius))
            {
                if (Theme.Look == Look.Aero && Enabled)
                {   // glossy: a lighter top half over the colour
                    using (var b = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(b, path);
                    var shine = new RectangleF(box.X + 1, box.Y + 1, box.Width - 2, box.Height / 2 - 1);
                    using (var sp = Ui.Rounded(shine, radius)) using (var sb = new LinearGradientBrush(shine, Color.FromArgb(150, Color.White), Color.FromArgb(40, Color.White), 90f)) g.FillPath(sb, sp);
                }
                else using (var b = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(b, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            if (Theme.Look == Look.Luna && hover && Enabled && !Primary)
            {   // XP's orange hover glow
                var glow = new RectangleF(box.X + 1.5f, box.Y + 1.5f, box.Width - 3, box.Height - 3);
                using (var gp = Ui.Rounded(glow, radius)) using (var pen = new Pen(p.ButtonHover, 2f)) g.DrawPath(pen, gp);
            }
            if (down) textRect.Offset(0, 1);
            TextRenderer.DrawText(g, Text, Font, textRect, text, Flags);
            if (Focused && ShowFocusCues && Enabled) { var f = r; f.Inflate(-Theme.S(3), -Theme.S(3)); ControlPaint.DrawFocusRectangle(g, f); }
        }

        TextFormatFlags Flags
        {
            get
            {
                var f = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                return UseMnemonic ? f : f | TextFormatFlags.NoPrefix;
            }
        }
    }

    /// <summary>Menu colours for the dark themes (the light ones keep Windows' own menu look).</summary>
    class ThemeMenuColors : ProfessionalColorTable
    {
        Palette P { get { return Theme.Current; } }
        public override Color ToolStripDropDownBackground { get { return P.Card; } }
        public override Color MenuBorder { get { return P.Border; } }
        public override Color MenuItemBorder { get { return P.Border; } }
        public override Color MenuItemSelected { get { return P.ButtonHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return P.ButtonHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return P.ButtonHover; } }
        public override Color ImageMarginGradientBegin { get { return P.Card; } }
        public override Color ImageMarginGradientMiddle { get { return P.Card; } }
        public override Color ImageMarginGradientEnd { get { return P.Card; } }
        public override Color SeparatorDark { get { return P.Border; } }
        public override Color SeparatorLight { get { return P.Card; } }
    }

    /// <summary>A small picture of a theme for the picker in Settings &amp; about: sidebar, page, a card and a button.</summary>
    class ThemeTile : Control
    {
        public readonly Palette Palette;
        public bool Selected;
        bool hover;

        public ThemeTile(Palette p)
        {
            Palette = p;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(140), Theme.S(116)); Margin = Theme.Pad(0, 0, 12, 12); Cursor = Cursors.Hand;
            BackColor = Theme.Bg; TabStop = true;
            AccessibleName = p.Name + " theme"; AccessibleRole = AccessibleRole.RadioButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Palette; int k(int v) => Theme.S(v);
            g.Clear(Theme.Bg);
            int nameH = k(26);
            var win = new Rectangle(k(3), k(3), Width - k(6), Height - nameH - k(6));
            // page and sidebar
            using (var b = new SolidBrush(p.Bg)) g.FillRectangle(b, win);
            var side = new Rectangle(win.X, win.Y, win.Width * 30 / 100, win.Height);
            using (var b = new LinearGradientBrush(new Rectangle(side.X, side.Y - 1, side.Width, side.Height + 2), p.Sidebar, p.SidebarTo, 90f)) g.FillRectangle(b, side);
            if (p.BrandBand) using (var b = new LinearGradientBrush(new Rectangle(side.X, side.Y, side.Width + 1, k(14)), p.HeroFrom, p.HeroTo, 0f)) g.FillRectangle(b, side.X, side.Y, side.Width, k(14));
            for (int i = 0; i < 4; i++)
            {
                var line = new Rectangle(side.X + k(7), side.Y + k(22) + i * k(13), side.Width - k(16), k(5));
                if (i == 1) using (var a = new SolidBrush(p.SidebarActive.A < 255 ? Blend(p.Sidebar, p.SidebarActive) : p.SidebarActive)) g.FillRectangle(a, side.X, line.Y - k(4), side.Width, k(13));
                using (var t = new SolidBrush(i == 1 ? p.SidebarActiveText : p.SidebarText)) g.FillRectangle(t, line);
            }
            // a title line, a card, a button
            int x = side.Right + k(8);
            using (var t = new SolidBrush(p.Text)) g.FillRectangle(t, x, win.Y + k(8), k(46), k(6));
            using (var t = new SolidBrush(p.Muted)) g.FillRectangle(t, x, win.Y + k(19), k(66), k(3));
            var card = new Rectangle(x, win.Y + k(28), win.Right - x - k(8), k(30));
            using (var b = new SolidBrush(p.Card)) g.FillRectangle(b, card);
            using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, card);
            using (var u = new SolidBrush(p.GraphUsed)) g.FillRectangle(u, card.X + k(6), card.Y + k(18), card.Width - k(30), k(4));
            using (var t = new SolidBrush(p.Text)) g.FillRectangle(t, card.X + k(6), card.Y + k(7), k(28), k(6));
            var btn = new Rectangle(x, card.Bottom + k(8), k(52), k(16));
            using (var b = new LinearGradientBrush(new Rectangle(btn.X, btn.Y - 1, btn.Width, btn.Height + 2), p.PrimaryTop, p.PrimaryBottom, 90f)) g.FillRectangle(b, btn);
            using (var pen = new Pen(p.PrimaryBorder)) g.DrawRectangle(pen, btn);
            using (var t = new SolidBrush(p.PrimaryText)) g.FillRectangle(t, btn.X + k(12), btn.Y + k(6), btn.Width - k(24), k(4));
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, win);
            // selection / hover frame and the name
            if (Selected || hover || Focused)
                using (var pen = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? k(3) : k(2))) g.DrawRectangle(pen, k(1), k(1), Width - k(3), Height - nameH - k(2));
            var nameRect = new Rectangle(0, Height - nameH, Width, nameH);
            TextRenderer.DrawText(g, (Selected ? "✓ " : "") + p.Name, Selected ? Theme.Bold : Theme.Base, nameRect, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        static Color Blend(Color under, Color over)
        {
            float a = over.A / 255f;
            return Color.FromArgb((int)(under.R + (over.R - under.R) * a), (int)(under.G + (over.G - under.G) * a), (int)(under.B + (over.B - under.B) * a));
        }
    }

    /// <summary>Things that can only be themed once a control exists: dark scrollbars, text box and list colours.</summary>
    static class ThemeApply
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Styles this control and everything inside it, including controls added later.</summary>
        public static void To(Control root)
        {
            Style(root);
            root.ControlAdded += (s, e) => To(e.Control);
            foreach (Control c in root.Controls) To(c);
        }

        static void Style(Control c)
        {
            var p = Theme.Current;
            if (c is TextBox) { c.BackColor = p.Field; c.ForeColor = p.Text; }
            else if (c is ListBox) { c.BackColor = p.Field; c.ForeColor = p.Text; }
            else if (c is ComboBox && p.Dark) { ((ComboBox)c).FlatStyle = FlatStyle.Flat; c.BackColor = p.Field; c.ForeColor = p.Text; }
            bool scrolls = c is ListView || c is TextBox || c is ListBox || (c is ScrollableControl && ((ScrollableControl)c).AutoScroll);
            if (p.Dark && scrolls)
            {
                // Windows 10 1809+ and 11: dark scrollbars. Older Windows ignores this and keeps light ones.
                if (c.IsHandleCreated) Dark(c); else c.HandleCreated += (s, e) => Dark(c);
            }
        }

        static void Dark(Control c) { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
    }
}

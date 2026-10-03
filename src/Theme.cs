using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>Colours, fonts and helpers for PadMouse's dark look.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(20, 21, 26);
        public static readonly Color Sidebar = Color.FromArgb(15, 16, 20);
        public static readonly Color Surface = Color.FromArgb(30, 32, 39);
        public static readonly Color Surface2 = Color.FromArgb(40, 43, 52);
        public static readonly Color Border = Color.FromArgb(56, 60, 72);
        public static readonly Color Text = Color.FromArgb(236, 237, 241);
        public static readonly Color TextMuted = Color.FromArgb(148, 152, 166);
        public static readonly Color Accent = Color.FromArgb(16, 185, 129);     // PadMouse green
        public static readonly Color AccentDim = Color.FromArgb(14, 90, 68);
        public static readonly Color Selection = Color.FromArgb(59, 130, 246);  // blue highlight
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);
        public static readonly Color PadBody = Color.FromArgb(52, 56, 68);
        public static readonly Color PadBodyEdge = Color.FromArgb(70, 75, 90);

        public static readonly Color FaceA = Color.FromArgb(34, 197, 94);
        public static readonly Color FaceB = Color.FromArgb(239, 68, 68);
        public static readonly Color FaceX = Color.FromArgb(59, 130, 246);
        public static readonly Color FaceY = Color.FromArgb(234, 179, 8);

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style);
        }

        public static Font Semibold(float size)
        {
            try { return new Font("Segoe UI Semibold", size); }
            catch { return new Font("Segoe UI", size, FontStyle.Bold); }
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Recursively applies dark colours to standard WinForms controls.</summary>
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var lbl = c as Label;
                if (lbl != null) lbl.UseMnemonic = false;   // show "&" literally
                if (c is TextBox || c is NumericUpDown)
                {
                    c.BackColor = Surface2; c.ForeColor = Text;
                    var tb = c as TextBox; if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
                    var nud = c as NumericUpDown; if (nud != null) nud.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ComboBox)
                {
                    var cb = (ComboBox)c;
                    cb.BackColor = Surface2; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat;
                }
                else if (c is ListBox)
                {
                    var lb = (ListBox)c;
                    lb.BackColor = Surface; lb.ForeColor = Text; lb.BorderStyle = BorderStyle.None;
                }
                else if (c is Button && !(c is FlatButton))
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = Surface2; b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border;
                }
                else if (c is LinkLabel)
                {
                    var l = (LinkLabel)c;
                    l.LinkColor = Selection; l.ActiveLinkColor = Accent; l.VisitedLinkColor = Selection;
                }
                else if (c is CheckBox || c is RadioButton)
                {
                    c.ForeColor = Text;
                    var bb = c as ButtonBase; if (bb != null) bb.FlatStyle = FlatStyle.Flat;
                }
                if (c.HasChildren) Apply(c);
            }
        }
    }

    // ====================================================================== buttons

    /// <summary>A rounded, flat button. Primary = filled green.</summary>
    public class FlatButton : Button
    {
        public bool Primary { get; set; }
        public bool Danger { get; set; }
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Height = 34;
            Font = Theme.UiFont(9.5f);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color fill = Primary ? Theme.Accent : Danger ? Color.FromArgb(70, 30, 34) : Theme.Surface2;
            if (!Enabled) fill = Theme.Surface;
            else if (down) fill = ControlPaint.Dark(fill, 0.1f);
            else if (hover) fill = ControlPaint.Light(fill, Primary ? 0.15f : 0.25f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, 7))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!Primary)
                    using (var p = new Pen(Danger ? Theme.Danger : Theme.Border)) g.DrawPath(p, path);
            }
            Color fg = !Enabled ? Theme.TextMuted : Primary ? Color.FromArgb(6, 30, 22) : Danger ? Color.FromArgb(252, 165, 165) : Theme.Text;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Selection, 1.5f)) using (var path = Theme.RoundRect(new RectangleF(2, 2, Width - 5, Height - 5), 6)) g.DrawPath(p, path);
        }
    }

    // ====================================================================== toggle switch

    public class ToggleSwitch : Control
    {
        bool isOn;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 30;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(9.75f);
            ForeColor = Theme.Text;
            TabStop = true;
        }

        public string Description { get; set; }

        public bool Checked
        {
            get { return isOn; }
            set { if (isOn == value) return; isOn = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Checked = !Checked;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            float h = 20, w = 38, y = (Height - h) / 2f;
            var track = new RectangleF(1, y, w, h);
            using (var path = Theme.RoundRect(track, h / 2))
            {
                using (var b = new SolidBrush(isOn ? Theme.Accent : Theme.Surface2)) g.FillPath(b, path);
                using (var p = new Pen(isOn ? Theme.Accent : Theme.Border)) g.DrawPath(p, path);
                if (Focused) using (var p = new Pen(Theme.Selection, 1.5f)) g.DrawPath(p, path);
            }
            float k = h - 6;
            float kx = isOn ? track.Right - k - 3 : track.X + 3;
            using (var b = new SolidBrush(isOn ? Color.White : Theme.TextMuted)) g.FillEllipse(b, kx, y + 3, k, k);
            var textRect = new Rectangle((int)(w + 12), 0, Width - (int)(w + 12), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ====================================================================== slider

    /// <summary>A labelled slider: "Name ............ value".</summary>
    public class Slider : Control
    {
        double min, max = 1, value, step = 0.01;
        bool dragging;
        public Func<double, string> Format = v => v.ToString("0.00");
        public event EventHandler ValueChanged;
        public string Hint { get; set; }

        public Slider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 52;
            Font = Theme.UiFont(9.75f);
            ForeColor = Theme.Text;
            TabStop = true;
        }

        public void Setup(double min, double max, double step, double value)
        {
            this.min = min; this.max = max; this.step = step;
            Value = value;
        }

        public double Value
        {
            get { return value; }
            set
            {
                double v = Math.Max(min, Math.Min(max, value));
                if (step > 0) v = Math.Round((v - min) / step) * step + min;
                v = Math.Round(v, 6);
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        RectangleF Track { get { return new RectangleF(8, Height - 18, Width - 16, 4); } }

        void SetFromX(int x)
        {
            var t = Track;
            double f = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
            Value = min + f * (max - min);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); dragging = true; Capture = true; SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Value -= step;
            if (e.KeyCode == Keys.Right) Value += step;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            TextRenderer.DrawText(g, Text, Font, new Point(4, 4), ForeColor, TextFormatFlags.NoPadding);
            string val = Format(value);
            var vs = TextRenderer.MeasureText(val, Font);
            TextRenderer.DrawText(g, val, Font, new Point(Width - vs.Width - 4, 4), Theme.Accent, TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(Hint))
            {
                var ts = TextRenderer.MeasureText(Text, Font);
                using (var f = Theme.UiFont(8.5f))
                    TextRenderer.DrawText(g, Hint, f, new Rectangle(ts.Width + 12, 5, Width - ts.Width - vs.Width - 24, 20), Theme.TextMuted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            var t = Track;
            float f2 = (float)((value - min) / (max - min));
            using (var path = Theme.RoundRect(t, 2)) using (var b = new SolidBrush(Theme.Surface2)) g.FillPath(b, path);
            var filled = new RectangleF(t.X, t.Y, Math.Max(4, t.Width * f2), t.Height);
            using (var path = Theme.RoundRect(filled, 2)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            float kx = t.X + t.Width * f2;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - 8, t.Y + 2 - 8, 16, 16);
            if (Focused) using (var p = new Pen(Theme.Selection, 2)) g.DrawEllipse(p, kx - 9, t.Y + 2 - 9, 18, 18);
        }
    }

    // ====================================================================== sidebar nav

    public class NavList : Control
    {
        public readonly System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
        public readonly System.Collections.Generic.List<string> Glyphs = new System.Collections.Generic.List<string>();
        int selected, hover = -1;
        const int RowH = 42, ListTop = 84;
        public event EventHandler SelectedChanged;

        public NavList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
            Font = Theme.UiFont(10f);
            Cursor = Cursors.Hand;
        }

        public void Add(string glyph, string text) { Glyphs.Add(glyph); Items.Add(text); Invalidate(); }

        public int Selected
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); var h = SelectedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        int IndexAt(int y) { int i = (y - ListTop) / RowH; return y >= ListTop && i >= 0 && i < Items.Count ? i : -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = IndexAt(e.Y); if (i != hover) { hover = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int i = IndexAt(e.Y); if (i >= 0) Selected = i; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            // logo
            using (var ico = TrayIcons.Render(28, Theme.Accent)) g.DrawImage(ico, 20, 26, 28, 28);
            using (var f = Theme.Semibold(13f)) TextRenderer.DrawText(g, "PadMouse", f, new Point(56, 27), Theme.Text, TextFormatFlags.NoPadding);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = new Rectangle(10, ListTop + i * RowH, Width - 20, RowH - 6);
                if (i == selected || i == hover)
                    using (var path = Theme.RoundRect(r, 7)) using (var b = new SolidBrush(i == selected ? Theme.Surface2 : Theme.Surface)) g.FillPath(b, path);
                if (i == selected)
                    using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y + 9, 3, r.Height - 18);
                using (var gf = new Font("Segoe UI Symbol", 11f))
                    TextRenderer.DrawText(g, Glyphs[i], gf, new Rectangle(r.X + 12, r.Y, 26, r.Height), i == selected ? Theme.Accent : Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, Items[i], Font, new Rectangle(r.X + 44, r.Y, r.Width - 44, r.Height), i == selected ? Theme.Text : Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
            using (var f = Theme.UiFont(8.5f))
                TextRenderer.DrawText(g, "v" + AppInfo.VersionText, f, new Point(22, Height - 30), Theme.TextMuted, TextFormatFlags.NoPadding);
        }
    }
}

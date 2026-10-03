using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>
    /// A controller-friendly keyboard that never takes focus, so keystrokes go to
    /// whichever window you were typing in. Can also be clicked with the mouse.
    /// </summary>
    public class OnScreenKeyboard : Form
    {
        enum KeyKind { Char, Backspace, Enter, Tab, Esc, Shift, Caps, Space, Left, Right, Up, Down, Hide }

        class KeyDef
        {
            public KeyKind Kind;
            public string Normal, Shifted, Label;
            public float W = 1f;
            public int Row;
            public RectangleF Unit;   // position in key units
            public RectangleF Px;     // position in pixels
        }

        const float Unit = 52f, Gap = 6f, Pad = 12f, HintH = 26f;

        readonly List<KeyDef> keys = new List<KeyDef>();
        readonly List<List<int>> rows = new List<List<int>>();
        int selected;
        int shiftState;           // 0 = off, 1 = next char only, 2 = caps lock
        int flashKey = -1;
        float scale = 1f;
        readonly Timer flashTimer = new Timer { Interval = 110 };
        Font keyFont, smallFont, hintFont;

        static readonly Color Bg = Color.FromArgb(24, 25, 30);
        static readonly Color KeyBg = Color.FromArgb(44, 46, 54);
        static readonly Color KeyBgSpecial = Color.FromArgb(34, 36, 43);
        static readonly Color KeyFg = Color.FromArgb(232, 233, 237);
        static readonly Color Accent = Color.FromArgb(59, 130, 246);
        static readonly Color Muted = Color.FromArgb(140, 144, 156);

        public OnScreenKeyboard()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Bg;
            DoubleBuffered = true;
            Text = "PadMouse keyboard";
            BuildLayout();
            flashTimer.Tick += delegate { flashTimer.Stop(); flashKey = -1; Invalidate(); };
            selected = IndexOfLabel("g");
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*WS_EX_NOACTIVATE*/ | 0x00000080 /*WS_EX_TOOLWINDOW*/ | 0x00000008 /*WS_EX_TOPMOST*/;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ layout

        void Row(params object[] defs)
        {
            int r = rows.Count;
            var idx = new List<int>();
            float x = 0;
            foreach (object o in defs)
            {
                KeyDef k = o as KeyDef;
                if (k == null)
                {
                    string s = (string)o; // two chars: normal + shifted
                    k = new KeyDef { Kind = KeyKind.Char, Normal = s.Substring(0, 1), Shifted = s.Substring(1, 1) };
                }
                k.Row = r;
                k.Unit = new RectangleF(x, r, k.W, 1);
                x += k.W;
                idx.Add(keys.Count);
                keys.Add(k);
            }
            rows.Add(idx);
        }

        static KeyDef Special(KeyKind kind, string label, float w)
        {
            return new KeyDef { Kind = kind, Label = label, W = w };
        }

        void BuildLayout()
        {
            Row("`¬", "1!", "2\"", "3£", "4$", "5%", "6^", "7&", "8*", "9(", "0)", "-_", "=+", Special(KeyKind.Backspace, "⌫ Bksp", 2f));
            Row(Special(KeyKind.Tab, "Tab", 1.5f), "qQ", "wW", "eE", "rR", "tT", "yY", "uU", "iI", "oO", "pP", "[{", "]}", Ch("#~", 1.5f));
            Row(Special(KeyKind.Caps, "Caps", 1.75f), "aA", "sS", "dD", "fF", "gG", "hH", "jJ", "kK", "lL", ";:", "'@", Special(KeyKind.Enter, "Enter ⏎", 2.25f));
            Row(Special(KeyKind.Shift, "⇧ Shift", 2.25f), "zZ", "xX", "cC", "vV", "bB", "nN", "mM", ",<", ".>", "/?", "\\|", Special(KeyKind.Shift, "⇧ Shift", 1.75f));
            Row(Special(KeyKind.Hide, "Hide", 2f), Special(KeyKind.Esc, "Esc", 1.5f), Special(KeyKind.Space, "Space", 6.5f),
                Special(KeyKind.Left, "◀", 1.25f), Special(KeyKind.Up, "▲", 1.25f), Special(KeyKind.Down, "▼", 1.25f), Special(KeyKind.Right, "▶", 1.25f));
        }

        static KeyDef Ch(string normalAndShifted, float w)
        {
            return new KeyDef { Kind = KeyKind.Char, Normal = normalAndShifted.Substring(0, 1), Shifted = normalAndShifted.Substring(1, 1), W = w };
        }

        int IndexOfLabel(string normal)
        {
            for (int i = 0; i < keys.Count; i++) if (keys[i].Normal == normal) return i;
            return 0;
        }

        void LayoutPixels()
        {
            float u = Unit * scale, gap = Gap * scale, pad = Pad * scale;
            foreach (var k in keys)
            {
                k.Px = new RectangleF(pad + k.Unit.X * u + gap / 2, pad + k.Unit.Y * u + gap / 2, k.Unit.Width * u - gap, u - gap);
            }
            int w = (int)Math.Ceiling(pad * 2 + 15 * u);
            int h = (int)Math.Ceiling(pad * 2 + rows.Count * u + HintH * scale);
            Size = new Size(w, h);

            if (keyFont != null) { keyFont.Dispose(); smallFont.Dispose(); hintFont.Dispose(); }
            keyFont = new Font("Segoe UI", 17f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            hintFont = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ------------------------------------------------------------------ show / hide

        public void ToggleShow()
        {
            if (Visible) Hide(); else ShowAtBottom();
        }

        public void ShowAtBottom()
        {
            if (!IsHandleCreated) CreateHandle();
            scale = Win32.DpiScale(Handle);
            LayoutPixels();
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(16 * scale));
            shiftState = 0;
            Show();
            Invalidate();
        }

        // ------------------------------------------------------------------ input from controller

        public void Command(OskCommand c)
        {
            if (!Visible) return;
            switch (c)
            {
                case OskCommand.Up: MoveVertical(-1); break;
                case OskCommand.Down: MoveVertical(1); break;
                case OskCommand.Left: MoveHorizontal(-1); break;
                case OskCommand.Right: MoveHorizontal(1); break;
                case OskCommand.Press: Press(selected); break;
                case OskCommand.Backspace: InputSender.TapKey(0x08); break;
                case OskCommand.Space: InputSender.TapKey(0x20); break;
                case OskCommand.Enter: InputSender.TapKey(0x0D); break;
                case OskCommand.Shift: shiftState = shiftState == 1 ? 0 : 1; break;
                case OskCommand.CaretLeft: InputSender.TapKey(0x25); break;
                case OskCommand.CaretRight: InputSender.TapKey(0x27); break;
                case OskCommand.Close: Hide(); break;
            }
            Invalidate();
        }

        void MoveHorizontal(int d)
        {
            var row = rows[keys[selected].Row];
            int pos = row.IndexOf(selected);
            pos = (pos + d + row.Count) % row.Count;
            selected = row[pos];
        }

        void MoveVertical(int d)
        {
            KeyDef cur = keys[selected];
            float cx = cur.Unit.X + cur.Unit.Width / 2;
            int r = (cur.Row + d + rows.Count) % rows.Count;
            int best = rows[r][0];
            float bestDist = float.MaxValue;
            foreach (int i in rows[r])
            {
                KeyDef k = keys[i];
                float dist;
                if (cx >= k.Unit.X && cx <= k.Unit.Right) dist = 0;
                else dist = Math.Min(Math.Abs(cx - k.Unit.X), Math.Abs(cx - k.Unit.Right));
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            selected = best;
        }

        void Press(int index)
        {
            KeyDef k = keys[index];
            flashKey = index;
            flashTimer.Stop(); flashTimer.Start();
            switch (k.Kind)
            {
                case KeyKind.Char:
                    string s = shiftState > 0 ? k.Shifted : k.Normal;
                    InputSender.TypeChar(s[0]);
                    if (shiftState == 1) shiftState = 0;
                    break;
                case KeyKind.Backspace: InputSender.TapKey(0x08); break;
                case KeyKind.Enter: InputSender.TapKey(0x0D); break;
                case KeyKind.Tab: InputSender.TapKey(0x09); break;
                case KeyKind.Esc: InputSender.TapKey(0x1B); break;
                case KeyKind.Space: InputSender.TapKey(0x20); break;
                case KeyKind.Left: InputSender.TapKey(0x25); break;
                case KeyKind.Right: InputSender.TapKey(0x27); break;
                case KeyKind.Up: InputSender.TapKey(0x26); break;
                case KeyKind.Down: InputSender.TapKey(0x28); break;
                case KeyKind.Shift: shiftState = shiftState == 1 ? 0 : 1; break;
                case KeyKind.Caps: shiftState = shiftState == 2 ? 0 : 2; break;
                case KeyKind.Hide: Hide(); break;
            }
            Invalidate();
        }

        // ------------------------------------------------------------------ mouse

        int HitTest(Point p)
        {
            for (int i = 0; i < keys.Count; i++) if (keys[i].Px.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i >= 0) { selected = i; Press(i); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i >= 0 && i != selected) { selected = i; Invalidate(); }
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            if (keyFont == null) LayoutPixels();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Bg);

            using (var border = new Pen(Color.FromArgb(60, 63, 74), 1f))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float radius = 7f * scale;
            for (int i = 0; i < keys.Count; i++)
            {
                KeyDef k = keys[i];
                bool isSel = i == selected;
                bool active = (k.Kind == KeyKind.Shift && shiftState == 1) || (k.Kind == KeyKind.Caps && shiftState == 2);
                Color fill = k.Kind == KeyKind.Char ? KeyBg : KeyBgSpecial;
                if (active) fill = Color.FromArgb(40, 70, 120);
                if (i == flashKey) fill = Accent;
                using (var path = RoundRect(k.Px, radius))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (isSel)
                        using (var p = new Pen(Accent, 3f * scale)) g.DrawPath(p, path);
                }
                string label = k.Kind == KeyKind.Char ? (shiftState > 0 ? k.Shifted : k.Normal) : k.Label;
                Font f = k.Kind == KeyKind.Char ? keyFont : smallFont;
                using (var tb = new SolidBrush(KeyFg)) g.DrawString(label, f, tb, k.Px, center);
            }

            string hint = "A type   B ⌫   X space   LB shift   RB/Start enter   LT/RT ◀ ▶   Y/Back close   Right stick = mouse";
            var hintRect = new RectangleF(0, Height - (HintH + Pad * 0.6f) * scale, Width, HintH * scale);
            using (var hb = new SolidBrush(Muted)) g.DrawString(hint, hintFont, hb, hintRect, center);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            float d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}

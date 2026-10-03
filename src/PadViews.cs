using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>Draws the gamepad glyph used for the tray icon, sidebar logo and installer.</summary>
    public static class TrayIcons
    {
        public static Bitmap Render(int size, Color body)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                g.ScaleTransform(s, s);
                using (var b = new SolidBrush(body))
                {
                    g.FillEllipse(b, 1, 8, 14, 18);
                    g.FillEllipse(b, 17, 8, 14, 18);
                    g.FillRectangle(b, 8, 8, 16, 13);
                }
                using (var w = new SolidBrush(Color.White))
                {
                    g.FillRectangle(w, 5, 14, 7, 2);
                    g.FillRectangle(w, 7.5f, 11.5f, 2, 7);
                    g.FillEllipse(w, 21, 11, 4, 4);
                    g.FillEllipse(w, 24, 15, 4, 4);
                }
            }
            return bmp;
        }

        public static Icon MakeIcon(Color body)
        {
            using (var bmp = Render(32, body)) return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // ====================================================================== controller diagram

    /// <summary>
    /// A clickable Xbox-style controller. Highlights buttons that are physically pressed and the
    /// selected one. In annotated mode it draws labelled call-outs for every button.
    /// </summary>
    public class ControllerView : Control
    {
        public PadButton? SelectedButton;
        public uint PressedMask;
        public bool Annotated;
        public Func<PadButton, string> LabelFor;      // annotated mode text
        public string LeftStickLabel = "", RightStickLabel = "";
        public event Action<PadButton> ButtonClicked;

        PadButton? hover;
        readonly Dictionary<PadButton, RectangleF> hit = new Dictionary<PadButton, RectangleF>();
        readonly ToolTip tip = new ToolTip();

        // Design space for the pad itself (600 x 330); annotated mode adds 230 px either side.
        const float PadW = 600, PadH = 330, Side = 240;

        public ControllerView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Font = Theme.UiFont(9f);

            hit[PadButton.LT] = new RectangleF(140, 2, 86, 30);
            hit[PadButton.RT] = new RectangleF(374, 2, 86, 30);
            hit[PadButton.LB] = new RectangleF(112, 40, 132, 26);
            hit[PadButton.RB] = new RectangleF(356, 40, 132, 26);
            hit[PadButton.L3] = Circle(195, 150, 36);
            hit[PadButton.R3] = Circle(365, 235, 36);
            hit[PadButton.Back] = new RectangleF(256, 142, 30, 18);
            hit[PadButton.Start] = new RectangleF(314, 142, 30, 18);
            hit[PadButton.Y] = Circle(420, 112, 18);
            hit[PadButton.X] = Circle(383, 150, 18);
            hit[PadButton.B] = Circle(457, 150, 18);
            hit[PadButton.A] = Circle(420, 188, 18);
            hit[PadButton.DPadUp] = new RectangleF(222, 197, 26, 28);
            hit[PadButton.DPadDown] = new RectangleF(222, 247, 26, 28);
            hit[PadButton.DPadLeft] = new RectangleF(194, 223, 28, 26);
            hit[PadButton.DPadRight] = new RectangleF(248, 223, 28, 26);
        }

        static RectangleF Circle(float cx, float cy, float r) { return new RectangleF(cx - r, cy - r, r * 2, r * 2); }

        float DesignW { get { return Annotated ? PadW + Side * 2 : PadW; } }
        float OffsetX { get { return Annotated ? Side : 0; } }

        void Transform(out float scale, out float ox, out float oy)
        {
            scale = Math.Min(Width / DesignW, Height / PadH);
            ox = (Width - DesignW * scale) / 2 + OffsetX * scale;
            oy = (Height - PadH * scale) / 2;
        }

        PadButton? HitTest(Point p)
        {
            float s, ox, oy; Transform(out s, out ox, out oy);
            float x = (p.X - ox) / s, y = (p.Y - oy) / s;
            foreach (var kv in hit)
            {
                var r = kv.Value;
                r.Inflate(4, 4);
                if (r.Contains(x, y)) return kv.Key;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h.HasValue ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, h.HasValue && LabelFor != null && !Annotated ? Names.Of(h.Value) + ": " + LabelFor(h.Value) : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var h = HitTest(e.Location);
            if (h.HasValue) { SelectedButton = h; Invalidate(); var ev = ButtonClicked; if (ev != null) ev(h.Value); }
        }

        bool IsPressed(PadButton b) { return (PressedMask & PadEngine.Bit(b)) != 0; }

        Color StateFill(PadButton b, Color normal)
        {
            if (IsPressed(b)) return Theme.Accent;
            if (SelectedButton.HasValue && SelectedButton.Value == b) return Theme.Selection;
            if (hover.HasValue && hover.Value == b) return ControlPaint.Light(normal, 0.3f);
            return normal;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);
            float s, ox, oy; Transform(out s, out ox, out oy);
            var state = g.Save();
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(s, s);

            Color key = Color.FromArgb(34, 37, 46);

            // triggers (behind the body)
            DrawRound(g, hit[PadButton.LT], 10, StateFill(PadButton.LT, key), Names.Short(PadButton.LT));
            DrawRound(g, hit[PadButton.RT], 10, StateFill(PadButton.RT, key), Names.Short(PadButton.RT));

            // body
            using (var body = new GraphicsPath())
            {
                body.AddEllipse(60, 95, 200, 230);
                body.AddEllipse(340, 95, 200, 230);
                body.AddPath(Theme.RoundRect(new RectangleF(120, 60, 360, 200), 80), false);
                body.FillMode = FillMode.Winding;
                using (var lg = new LinearGradientBrush(new RectangleF(0, 60, 600, 270), Theme.PadBodyEdge, Theme.PadBody, 90f))
                    g.FillPath(lg, body);
            }

            // bumpers sit on the top edge
            DrawRound(g, hit[PadButton.LB], 12, StateFill(PadButton.LB, key), Names.Short(PadButton.LB));
            DrawRound(g, hit[PadButton.RB], 12, StateFill(PadButton.RB, key), Names.Short(PadButton.RB));

            // guide button
            using (var b = new SolidBrush(Color.FromArgb(30, 32, 40))) g.FillEllipse(b, 282, 88, 36, 36);
            using (var p = new Pen(Theme.Accent, 2.5f)) g.DrawEllipse(p, 288, 94, 24, 24);

            // sticks
            DrawStick(g, PadButton.L3);
            DrawStick(g, PadButton.R3);

            // d-pad
            using (var b = new SolidBrush(Color.FromArgb(30, 32, 40))) g.FillEllipse(b, 192, 195, 86, 82);
            foreach (var d in new[] { PadButton.DPadUp, PadButton.DPadDown, PadButton.DPadLeft, PadButton.DPadRight })
                DrawRound(g, hit[d], 5, StateFill(d, Color.FromArgb(70, 75, 90)), null);

            // view / menu
            DrawRound(g, hit[PadButton.Back], 8, StateFill(PadButton.Back, key), null);
            DrawRound(g, hit[PadButton.Start], 8, StateFill(PadButton.Start, key), null);
            using (var p = new Pen(Theme.TextMuted, 1.4f))
            {
                g.DrawRectangle(p, 265, 147, 8, 6); g.DrawRectangle(p, 268, 145, 8, 6);
                g.DrawLine(p, 323, 147, 335, 147); g.DrawLine(p, 323, 151, 335, 151); g.DrawLine(p, 323, 155, 335, 155);
            }

            // face buttons
            DrawFace(g, PadButton.Y, Theme.FaceY);
            DrawFace(g, PadButton.X, Theme.FaceX);
            DrawFace(g, PadButton.B, Theme.FaceB);
            DrawFace(g, PadButton.A, Theme.FaceA);

            if (Annotated) DrawAnnotations(g);
            g.Restore(state);
        }

        void DrawRound(Graphics g, RectangleF r, float rad, Color fill, string label)
        {
            using (var path = Theme.RoundRect(r, rad))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(80, 0, 0, 0))) g.DrawPath(p, path);
            }
            if (label != null)
                using (var f = new Font("Segoe UI Semibold", 11f, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Theme.TextMuted))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(label, f, b, r, sf);
        }

        void DrawStick(Graphics g, PadButton b)
        {
            var r = hit[b];
            using (var well = new SolidBrush(Color.FromArgb(28, 30, 38))) g.FillEllipse(well, RectangleF.Inflate(r, 6, 6));
            Color c = StateFill(b, Color.FromArgb(46, 49, 60));
            using (var br = new SolidBrush(c)) g.FillEllipse(br, r);
            using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f)) g.DrawEllipse(p, RectangleF.Inflate(r, -8, -8));
        }

        void DrawFace(Graphics g, PadButton b, Color c)
        {
            var r = hit[b];
            c = Names.FaceColour(b, c);
            Color fill = IsPressed(b) ? Theme.Accent : (SelectedButton.HasValue && SelectedButton.Value == b) ? Theme.Selection : Color.FromArgb(26, 28, 35);
            if (hover.HasValue && hover.Value == b && !IsPressed(b)) fill = ControlPaint.Light(fill, 0.4f);
            using (var br = new SolidBrush(fill)) g.FillEllipse(br, r);
            using (var f = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(IsPressed(b) ? Color.White : c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Names.Face(b), f, tb, r, sf);
        }

        // ------------------------------------------------------------ annotations

        void DrawAnnotations(Graphics g)
        {
            if (LabelFor == null) return;
            // left column (x = -230..-10), right column (610..830), in pad coordinates
            var left = new[] { PadButton.LT, PadButton.LB, PadButton.L3, PadButton.Back, PadButton.DPadUp, PadButton.DPadLeft, PadButton.DPadRight, PadButton.DPadDown };
            var right = new[] { PadButton.RT, PadButton.RB, PadButton.Y, PadButton.B, PadButton.X, PadButton.Start, PadButton.A, PadButton.R3 };
            DrawColumn(g, left, true);
            DrawColumn(g, right, false);
        }

        void DrawColumn(Graphics g, PadButton[] buttons, bool leftSide)
        {
            float top = 4, step = (PadH - 8) / buttons.Length;
            using (var nameFont = new Font("Segoe UI Semibold", 11.5f, GraphicsUnit.Pixel))
            using (var actFont = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel))
            using (var line = new Pen(Color.FromArgb(90, Theme.TextMuted), 1f))
            using (var dot = new SolidBrush(Theme.Accent))
            using (var nameB = new SolidBrush(Theme.Text))
            using (var actB = new SolidBrush(Theme.TextMuted))
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    var b = buttons[i];
                    float y = top + step * i + step / 2;
                    var r = hit[b];
                    var target = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                    float textX = leftSide ? -Side + 8 : PadW + 14;
                    float anchorX = leftSide ? -6 : PadW + 6;
                    // elbow line: across to just beside the pad, then to the button
                    float elbowX = leftSide ? Math.Min(target.X - 30, 70) : Math.Max(target.X + 30, PadW - 70);
                    g.DrawLines(line, new[] { new PointF(anchorX, y), new PointF(elbowX, y), target });
                    g.FillEllipse(dot, target.X - 3, target.Y - 3, 6, 6);
                    string label = LabelFor(b);
                    string stick = b == PadButton.L3 ? LeftStickLabel : b == PadButton.R3 ? RightStickLabel : "";
                    if (stick.Length > 0) label = (label == "—" ? "" : label + "  ·  ") + "stick moves: " + stick;
                    var sf = new StringFormat { Alignment = leftSide ? StringAlignment.Far : StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    var nameRect = new RectangleF(textX, y - 16, Side - 22, 16);
                    var actRect = new RectangleF(textX, y, Side - 22, 16);
                    g.DrawString(Names.Of(b), nameFont, nameB, nameRect, sf);
                    g.DrawString(label, actFont, actB, actRect, sf);
                    sf.Dispose();
                }
            }
        }
    }

    /// <summary>Button names that match the controller in use (Xbox, PlayStation, Nintendo).</summary>
    public static class Names
    {
        /// <summary>Set by the UI from the engine's active controller.</summary>
        public static PadFamily Family = PadFamily.Xbox;

        /// <summary>Text drawn on the face buttons.</summary>
        public static string Face(PadButton b)
        {
            if (Family == PadFamily.PlayStation)
                switch (b) { case PadButton.A: return "\u2715"; case PadButton.B: return "\u25CB"; case PadButton.X: return "\u25A1"; case PadButton.Y: return "\u25B3"; }
            if (Family == PadFamily.Nintendo)   // positions stay Xbox-style; Nintendo prints the letters swapped
                switch (b) { case PadButton.A: return "B"; case PadButton.B: return "A"; case PadButton.X: return "Y"; case PadButton.Y: return "X"; }
            return b.ToString();
        }

        public static Color FaceColour(PadButton b, Color xbox)
        {
            if (Family == PadFamily.PlayStation)
                switch (b)
                {
                    case PadButton.A: return Color.FromArgb(125, 163, 232);
                    case PadButton.B: return Color.FromArgb(232, 107, 107);
                    case PadButton.X: return Color.FromArgb(214, 140, 200);
                    case PadButton.Y: return Color.FromArgb(94, 196, 160);
                }
            if (Family == PadFamily.Nintendo || Family == PadFamily.Generic) return Color.FromArgb(220, 222, 228);
            return xbox;
        }

        /// <summary>Short label: LB / L1 / L ...</summary>
        public static string Short(PadButton b)
        {
            bool ps = Family == PadFamily.PlayStation, nin = Family == PadFamily.Nintendo;
            switch (b)
            {
                case PadButton.LB: return ps ? "L1" : nin ? "L" : "LB";
                case PadButton.RB: return ps ? "R1" : nin ? "R" : "RB";
                case PadButton.LT: return ps ? "L2" : nin ? "ZL" : "LT";
                case PadButton.RT: return ps ? "R2" : nin ? "ZR" : "RT";
                case PadButton.Back: return ps ? "Share" : nin ? "\u2212" : "View";
                case PadButton.Start: return ps ? "Options" : nin ? "+" : "Menu";
                case PadButton.A: case PadButton.B: case PadButton.X: case PadButton.Y: return Face(b);
                default: return b.ToString();
            }
        }

        public static string Of(PadButton b)
        {
            bool ps = Family == PadFamily.PlayStation, nin = Family == PadFamily.Nintendo;
            switch (b)
            {
                case PadButton.A: return ps ? "\u2715 Cross" : nin ? "B (bottom)" : "A";
                case PadButton.B: return ps ? "\u25CB Circle" : nin ? "A (right)" : "B";
                case PadButton.X: return ps ? "\u25A1 Square" : nin ? "Y (left)" : "X";
                case PadButton.Y: return ps ? "\u25B3 Triangle" : nin ? "X (top)" : "Y";
                case PadButton.Back: return ps ? "Share / Create" : nin ? "\u2212 (minus)" : "View";
                case PadButton.Start: return ps ? "Options" : nin ? "+ (plus)" : "Menu";
                case PadButton.L3: return "L3 (left stick press)";
                case PadButton.R3: return "R3 (right stick press)";
                case PadButton.DPadUp: return "D-pad up";
                case PadButton.DPadDown: return "D-pad down";
                case PadButton.DPadLeft: return "D-pad left";
                case PadButton.DPadRight: return "D-pad right";
                case PadButton.LT: return ps ? "L2 (left trigger)" : nin ? "ZL (left trigger)" : "LT (left trigger)";
                case PadButton.RT: return ps ? "R2 (right trigger)" : nin ? "ZR (right trigger)" : "RT (right trigger)";
                case PadButton.LB: return ps ? "L1 (left bumper)" : nin ? "L (left bumper)" : "LB (left bumper)";
                case PadButton.RB: return ps ? "R1 (right bumper)" : nin ? "R (right bumper)" : "RB (right bumper)";
                default: return b.ToString();
            }
        }

        /// <summary>"Back+Start" → "View + Menu" (or "Share + Options" on PlayStation).</summary>
        public static string Combo(string combo)
        {
            if (string.IsNullOrEmpty(combo) || combo.Equals("None", StringComparison.OrdinalIgnoreCase)) return "(none)";
            var parts = combo.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                PadButton b;
                if (Enum.TryParse(parts[i].Trim(), true, out b)) parts[i] = Short(b);
            }
            return string.Join(" + ", parts);
        }

        public static string FamilyName(PadFamily f)
        {
            switch (f)
            {
                case PadFamily.PlayStation: return "PlayStation";
                case PadFamily.Nintendo: return "Nintendo";
                case PadFamily.Generic: return "Other";
                default: return "Xbox";
            }
        }

        public static string Battery(int type, int level)
        {
            if (type < 0) return "";
            if (type == XInput.BATTERY_TYPE_WIRED) return "Wired";
            if (type == XInput.BATTERY_TYPE_DISCONNECTED) return "";
            switch (level)
            {
                case 0: return "Battery empty";
                case 1: return "Battery low";
                case 2: return "Battery medium";
                case 3: return "Battery full";
            }
            return "";
        }
    }

    // ====================================================================== stick visualiser

    /// <summary>Shows a stick's live position, the deadzone, and the shaped output.</summary>
    public class StickView : Control
    {
        public int RawX, RawY;
        public double Deadzone = 0.15, Curve = 2;
        public string Caption = "Left stick";
        public string ModeText = "Mouse";

        public StickView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.UiFont(9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 10))
            using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);

            TextRenderer.DrawText(g, Caption, Font, new Point(12, 10), Theme.Text, TextFormatFlags.NoPadding);
            var ms = TextRenderer.MeasureText(ModeText, Font);
            TextRenderer.DrawText(g, ModeText, Font, new Point(Width - ms.Width - 12, 10), Theme.Accent, TextFormatFlags.NoPadding);

            float size = Math.Min(Width - 24, Height - 44);
            float cx = Width / 2f, cy = 32 + size / 2f + 4;
            float r = size / 2f;
            using (var b = new SolidBrush(Theme.Bg)) g.FillEllipse(b, cx - r, cy - r, size, size);
            using (var p = new Pen(Theme.Border)) g.DrawEllipse(p, cx - r, cy - r, size, size);
            float dz = (float)(r * Deadzone);
            using (var b = new SolidBrush(Color.FromArgb(60, Theme.Danger))) g.FillEllipse(b, cx - dz, cy - dz, dz * 2, dz * 2);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) { g.DrawLine(p, cx - r, cy, cx + r, cy); g.DrawLine(p, cx, cy - r, cx, cy + r); }

            float rx = (float)Math.Max(-1, RawX / 32767.0), ry = (float)Math.Max(-1, RawY / 32767.0);
            double ox, oy;
            PadEngine.ShapeStick((short)RawX, (short)RawY, Deadzone, Curve, out ox, out oy);
            // output vector
            if (ox != 0 || oy != 0)
                using (var p = new Pen(Theme.Accent, 3f) { EndCap = LineCap.Round })
                    g.DrawLine(p, cx, cy, cx + (float)ox * r, cy - (float)oy * r);
            // raw dot
            float px = cx + rx * r, py = cy - ry * r;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, px - 5, py - 5, 10, 10);

            string pct = ((int)(Math.Sqrt(ox * ox + oy * oy) * 100)).ToString() + "% output";
            using (var f = Theme.UiFont(8.5f))
            {
                var ps = TextRenderer.MeasureText(pct, f);
                TextRenderer.DrawText(g, pct, f, new Point((int)(cx - ps.Width / 2), Height - 22), Theme.TextMuted, TextFormatFlags.NoPadding);
            }
        }
    }

    // ====================================================================== cursor test area

    /// <summary>Click the dots with the controller to get a feel for the speed settings.</summary>
    public class TestArea : Control
    {
        readonly Random rng = new Random();
        PointF target;
        float radius = 16;
        int hits;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        double best = -1, last = -1;

        public TestArea()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.UiFont(9f);
            Cursor = Cursors.Cross;
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (target.IsEmpty) NewTarget(); }

        void NewTarget()
        {
            if (Width < 80 || Height < 80) return;
            radius = hits % 3 == 2 ? 9 : 16;       // every third target is small, to test precision mode
            target = new PointF(rng.Next(30, Width - 30), rng.Next(44, Height - 24));
            clock.Restart();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            float dx = e.X - target.X, dy = e.Y - target.Y;
            if (dx * dx + dy * dy <= (radius + 3) * (radius + 3))
            {
                last = clock.Elapsed.TotalSeconds;
                if (best < 0 || last < best) best = last;
                hits++;
                NewTarget();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 10))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, "Try it: click the dots with A", Font, new Point(12, 10), Theme.Text, TextFormatFlags.NoPadding);
            string stats = hits == 0 ? "small dots: hold LT for precision" :
                hits + " hit · last " + last.ToString("0.00") + "s · best " + best.ToString("0.00") + "s";
            var ss = TextRenderer.MeasureText(stats, Font);
            TextRenderer.DrawText(g, stats, Font, new Point(Width - ss.Width - 12, 10), Theme.TextMuted, TextFormatFlags.NoPadding);
            if (target.IsEmpty) NewTarget();
            using (var b = new SolidBrush(Color.FromArgb(50, Theme.Accent))) g.FillEllipse(b, target.X - radius - 6, target.Y - radius - 6, (radius + 6) * 2, (radius + 6) * 2);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, target.X - radius, target.Y - radius, radius * 2, radius * 2);
        }
    }
}

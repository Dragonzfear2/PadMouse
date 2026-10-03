using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>
    /// A small click-through pop-up near the bottom of the screen ("PadMouse on", "Profile: Browser"...).
    /// Never takes focus and fades out on its own.
    /// </summary>
    public class Osd : Form
    {
        readonly Timer timer = new Timer { Interval = 30 };
        string title = "", subtitle = "";
        Color accent = Theme.Accent;
        double shownAt;
        float scale = 1f;
        Font titleFont, subFont;
        const double HoldSecs = 1.4, FadeSecs = 0.35;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Opacity = 0;
            timer.Tick += delegate { Tick(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // NOACTIVATE | TOOLWINDOW | TOPMOST | LAYERED | TRANSPARENT (click-through)
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008 | 0x00080000 | 0x00000020;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void ShowMessage(string title, string subtitle, Color accent)
        {
            this.title = title ?? "";
            this.subtitle = subtitle ?? "";
            this.accent = accent;
            if (!IsHandleCreated) CreateHandle();
            scale = Win32.DpiScale(Handle);
            if (titleFont != null) { titleFont.Dispose(); subFont.Dispose(); }
            titleFont = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            subFont = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            Size tsz = TextRenderer.MeasureText(this.title, titleFont);
            Size ssz = this.subtitle.Length > 0 ? TextRenderer.MeasureText(this.subtitle, subFont) : Size.Empty;
            int w = (int)(Math.Max(tsz.Width, ssz.Width) + 64 * scale);
            int h = (int)((this.subtitle.Length > 0 ? 62 : 44) * scale);
            w = Math.Max(w, (int)(200 * scale));
            Size = new Size(w, h);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, w, h), h / 2f))
                Region = new Region(path);

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - (int)(70 * scale));
            shownAt = clock.Elapsed.TotalSeconds;
            Opacity = 0.96;
            if (!Visible) Show();
            Invalidate();
            timer.Start();
        }

        void Tick()
        {
            double t = clock.Elapsed.TotalSeconds - shownAt;
            if (t < HoldSecs) return;
            double f = 1 - (t - HoldSecs) / FadeSecs;
            if (f <= 0) { timer.Stop(); Hide(); return; }
            Opacity = 0.96 * f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Surface);
            float h = Height;
            // accent dot
            float d = 12 * scale;
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, 22 * scale, h / 2 - d / 2, d, d);
            float x = 44 * scale;
            if (subtitle.Length > 0)
            {
                TextRenderer.DrawText(g, title, titleFont, new Point((int)x, (int)(10 * scale)), Theme.Text, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, subtitle, subFont, new Point((int)x, (int)(34 * scale)), Theme.TextMuted, TextFormatFlags.NoPadding);
            }
            else
            {
                var r = new Rectangle((int)x, 0, Width - (int)x, Height);
                TextRenderer.DrawText(g, title, titleFont, r, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }
    }
}

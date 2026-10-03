using System;
using System.Drawing;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>First-run guide: the controller with every button labelled, plus the three things to know.</summary>
    public class WelcomeForm : Form
    {
        readonly ControllerView pad;
        readonly Timer live = new Timer { Interval = 40 };
        public bool OpenSettingsRequested;

        public WelcomeForm(Config cfg, PadEngine engine)
        {
            var prof = cfg.Profiles[Math.Min(engine.ActiveProfile, cfg.Profiles.Count - 1)];

            Text = "Welcome to PadMouse";
            Icon = TrayIcons.MakeIcon(Theme.Accent);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 680);

            Controls.Add(new Label { Text = "Welcome to PadMouse", Font = Theme.Semibold(20f), Location = new Point(36, 26), AutoSize = true });
            Controls.Add(new Label
            {
                Text = "Your controller now works as a mouse. Here's what every button does in the \"" + prof.Name + "\" profile. Try pressing them!",
                Location = new Point(38, 70), Size = new Size(1000, 22), ForeColor = Theme.TextMuted
            });

            pad = new ControllerView { Annotated = true, Location = new Point(20, 104), Size = new Size(1040, 400) };
            pad.LabelFor = b => Short(prof.Get(b));
            pad.LeftStickLabel = prof.LeftStick.ToString().ToLowerInvariant();
            pad.RightStickLabel = prof.RightStick.ToString().ToLowerInvariant();
            Controls.Add(pad);

            int y = 524, w = 326, gap = 18, x = 36;
            Controls.Add(Tip(x, y, w, "Switch on / off", "Hold " + Names.Combo(cfg.ToggleCombo) + " together. Turn it off before playing a game; PadMouse also pauses by itself when a fullscreen game is in front."));
            Controls.Add(Tip(x + w + gap, y, w, "Type with the controller", "Press Y for the on-screen keyboard. D-pad moves, A types, B deletes, Y closes."));
            Controls.Add(Tip(x + (w + gap) * 2, y, w, "Profiles & settings", "Hold View and press RB / LB to change profile. Click the green tray icon for settings, or run PadMouse again."));

            var go = new FlatButton { Text = "Let's go", Primary = true, Location = new Point(910, 628), Size = new Size(134, 38) };
            var settings = new FlatButton { Text = "Open settings", Location = new Point(764, 628), Size = new Size(136, 38) };
            go.Click += delegate { Close(); };
            settings.Click += delegate { OpenSettingsRequested = true; Close(); };
            Controls.Add(go); Controls.Add(settings);
            AcceptButton = go;

            Theme.Apply(this);
            live.Tick += delegate { pad.PressedMask = engine.ConnectedSlot >= 0 ? engine.LiveMask : 0; pad.Invalidate(); };
        }

        static string Short(string action)
        {
            ButtonAction a; string err;
            if (!ButtonAction.TryParse(action, out a, out err)) return action;
            switch (a.Kind)
            {
                case ActionKind.None: return "—";
                case ActionKind.LeftClick: return "Left click";
                case ActionKind.RightClick: return "Right click";
                case ActionKind.MiddleClick: return "Middle click";
                case ActionKind.Keyboard: return "On-screen keyboard";
                case ActionKind.Precision: return "Slow cursor (hold)";
                case ActionKind.Toggle: return "On / off";
                case ActionKind.Key: return a.Text.Substring(4).Replace("+", " + ");
                default: return ButtonAction.Describe(action);
            }
        }

        static Control Tip(int x, int y, int w, string title, string body)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, 92), BackColor = Theme.Surface };
            p.Controls.Add(new Label { Text = title, UseMnemonic = false, Font = Theme.Semibold(10.5f), Location = new Point(14, 10), AutoSize = true, ForeColor = Theme.Accent, BackColor = Theme.Surface });
            p.Controls.Add(new Label { Text = body, Location = new Point(14, 34), Size = new Size(w - 24, 54), ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Font = Theme.UiFont(9f) });
            return p;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Win32.UseDarkTitleBar(Handle); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); live.Start(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { live.Stop(); base.OnFormClosed(e); }
    }
}

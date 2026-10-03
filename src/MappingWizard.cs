using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>
    /// Teaches PadMouse an unrecognised controller: "press the bottom button", "push the left stick right"...
    /// Produces an SDL game controller mapping that's saved and used from then on.
    /// </summary>
    public class MappingWizard : Form
    {
        class Step
        {
            public string Field;       // SDL mapping field, e.g. "a", "leftx"
            public string Prompt;
            public PadButton? Show;    // button to highlight on the picture
            public bool Stick;         // expects an axis movement
            public bool Trigger;
            public string Binding;     // result, e.g. "b0", "a2~", "h0.1"
        }

        readonly PadEngine engine;
        readonly PadInfo pad;
        readonly List<Step> steps = new List<Step>();
        readonly Timer timer = new Timer { Interval = 30 };
        readonly ControllerView picture;
        readonly Label stepLabel, prompt, status;
        readonly FlatButton skipBtn, backBtn, finishBtn;
        int index;
        RawJoystickState baseline;
        RawJoystickState rest;            // resting position captured before the first step
        PadFamily previousFamily;
        bool waitingForRelease;
        double releaseSince;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public MappingWizard(PadEngine engine, PadInfo pad)
        {
            this.engine = engine;
            this.pad = pad;

            Add("a", "Press the BOTTOM face button", PadButton.A);
            Add("b", "Press the RIGHT face button", PadButton.B);
            Add("x", "Press the LEFT face button", PadButton.X);
            Add("y", "Press the TOP face button", PadButton.Y);
            Add("leftshoulder", "Press the LEFT bumper (top-left shoulder button)", PadButton.LB);
            Add("rightshoulder", "Press the RIGHT bumper (top-right shoulder button)", PadButton.RB);
            Add("lefttrigger", "Pull the LEFT trigger all the way", PadButton.LT).Trigger = true;
            Add("righttrigger", "Pull the RIGHT trigger all the way", PadButton.RT).Trigger = true;
            Add("back", "Press the LEFT middle button (Back / Select / Share)", PadButton.Back);
            Add("start", "Press the RIGHT middle button (Start / Options / Menu)", PadButton.Start);
            Add("leftstick", "Press the LEFT stick in, like a button", PadButton.L3);
            Add("rightstick", "Press the RIGHT stick in, like a button", PadButton.R3);
            Add("dpup", "Press UP on the D-pad", PadButton.DPadUp);
            Add("dpdown", "Press DOWN on the D-pad", PadButton.DPadDown);
            Add("dpleft", "Press LEFT on the D-pad", PadButton.DPadLeft);
            Add("dpright", "Press RIGHT on the D-pad", PadButton.DPadRight);
            Add("leftx", "Push the LEFT stick all the way to the RIGHT", PadButton.L3).Stick = true;
            Add("lefty", "Push the LEFT stick all the way DOWN", PadButton.L3).Stick = true;
            Add("rightx", "Push the RIGHT stick all the way to the RIGHT", PadButton.R3).Stick = true;
            Add("righty", "Push the RIGHT stick all the way DOWN", PadButton.R3).Stick = true;

            Text = "Set up controller";
            Icon = TrayIcons.MakeIcon(Theme.Accent);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 560);

            Controls.Add(new Label { Text = "Set up " + pad.Name, UseMnemonic = false, Font = Theme.Semibold(15f), Location = new Point(28, 22), Size = new Size(590, 30), AutoEllipsis = true });
            Controls.Add(new Label { Text = "Do what each step asks. Skip anything your controller doesn't have.", Location = new Point(30, 56), Size = new Size(590, 20), ForeColor = Theme.TextMuted });

            picture = new ControllerView { Location = new Point(20, 86), Size = new Size(600, 300) };
            Controls.Add(picture);

            stepLabel = new Label { Location = new Point(30, 396), Size = new Size(580, 20), ForeColor = Theme.TextMuted, Font = Theme.UiFont(9f) };
            prompt = new Label { Location = new Point(30, 418), Size = new Size(580, 30), Font = Theme.Semibold(13f), UseMnemonic = false };
            status = new Label { Location = new Point(30, 452), Size = new Size(580, 22), ForeColor = Theme.Accent };
            Controls.Add(stepLabel); Controls.Add(prompt); Controls.Add(status);

            backBtn = new FlatButton { Text = "Back", Location = new Point(28, 500), Size = new Size(100, 36) };
            skipBtn = new FlatButton { Text = "Skip", Location = new Point(138, 500), Size = new Size(100, 36) };
            finishBtn = new FlatButton { Text = "Finish", Primary = true, Location = new Point(492, 500), Size = new Size(120, 36), Visible = false };
            var cancel = new FlatButton { Text = "Cancel", Location = new Point(372, 500), Size = new Size(110, 36) };
            backBtn.Click += delegate { if (index > 0) { index--; steps[index].Binding = null; StartStep(); } };
            skipBtn.Click += delegate { steps[index].Binding = null; Next(); };
            finishBtn.Click += delegate { Finish(); };
            cancel.Click += delegate { Close(); };
            Controls.Add(backBtn); Controls.Add(skipBtn); Controls.Add(finishBtn); Controls.Add(cancel);

            previousFamily = Names.Family;
            Names.Family = PadFamily.Generic;
            timer.Tick += delegate { Tick(); };
        }

        Step Add(string field, string promptText, PadButton show)
        {
            var s = new Step { Field = field, Prompt = promptText, Show = show };
            steps.Add(s);
            return s;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Win32.UseDarkTitleBar(Handle); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            engine.MappingAdded += OnMappingAdded;
            engine.RequestRawCapture(pad.InstanceId);
            index = 0;
            StartStep();
            timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            engine.MappingAdded -= OnMappingAdded;
            engine.RequestRawCapture(-1);
            Names.Family = previousFamily;
            base.OnFormClosed(e);
        }

        void StartStep()
        {
            var s = steps[index];
            baseline = null;
            waitingForRelease = true;   // let go of everything before we start listening
            releaseSince = clock.Elapsed.TotalSeconds;
            stepLabel.Text = "Step " + (index + 1) + " of " + steps.Count;
            prompt.Text = s.Prompt;
            status.Text = "";
            picture.SelectedButton = s.Show;
            picture.Invalidate();
            backBtn.Enabled = index > 0;
            skipBtn.Visible = true;
            finishBtn.Visible = false;
        }

        void Next()
        {
            if (index < steps.Count - 1) { index++; StartStep(); return; }
            prompt.Text = "All done. Press Finish to save.";
            stepLabel.Text = "";
            status.Text = Summary();
            picture.SelectedButton = null;
            picture.Invalidate();
            skipBtn.Visible = false;
            finishBtn.Visible = true;
        }

        string Summary()
        {
            int n = 0;
            foreach (var s in steps) if (s.Binding != null) n++;
            return n + " of " + steps.Count + " inputs set up.";
        }

        void Tick()
        {
            var raw = engine.Pads.Raw;
            if (raw == null) { status.Text = "Waiting for the controller…"; return; }
            if (finishBtn.Visible) return;

            if (waitingForRelease)
            {
                // Wait until nothing is held, then take that as the resting position.
                if (!Idle(raw)) { releaseSince = clock.Elapsed.TotalSeconds; status.Text = "Let go of everything…"; return; }
                if (clock.Elapsed.TotalSeconds - releaseSince < 0.25) return;
                waitingForRelease = false;
                baseline = Copy(raw);
                if (rest == null) rest = baseline;
                status.Text = "";
                return;
            }

            string binding = Detect(raw, steps[index]);
            if (binding == null) return;
            if (IsUsed(binding)) { status.Text = "That one's already used. Try the right button, or Skip."; status.ForeColor = Theme.Warning; return; }
            steps[index].Binding = binding;
            status.ForeColor = Theme.Accent;
            Next();
        }

        bool IsUsed(string binding)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (i == index || steps[i].Binding == null) continue;
                if (Conflicts(steps[i].Binding, binding)) return true;
            }
            return false;
        }

        /// <summary>Same input, unless they're opposite halves of one axis (e.g. a combined trigger axis).</summary>
        static bool Conflicts(string a, string b)
        {
            a = a.TrimEnd('~'); b = b.TrimEnd('~');
            char sa = a[0] == '+' || a[0] == '-' ? a[0] : ' ';
            char sb = b[0] == '+' || b[0] == '-' ? b[0] : ' ';
            if (a.TrimStart('+', '-') != b.TrimStart('+', '-')) return false;
            return sa == ' ' || sb == ' ' || sa == sb;
        }

        bool Idle(RawJoystickState r)
        {
            foreach (bool b in r.Buttons) if (b) return false;
            foreach (byte h in r.Hats) if (h != 0) return false;
            if (rest != null)
                for (int i = 0; i < r.Axes.Length && i < rest.Axes.Length; i++)
                    if (Math.Abs(r.Axes[i] - rest.Axes[i]) > 8000) return false;
            return true;
        }

        static RawJoystickState Copy(RawJoystickState r)
        {
            return new RawJoystickState { Axes = (short[])r.Axes.Clone(), Buttons = (bool[])r.Buttons.Clone(), Hats = (byte[])r.Hats.Clone() };
        }

        string Detect(RawJoystickState r, Step step)
        {
            var b = baseline;
            if (b == null) return null;

            if (!step.Stick)
            {
                for (int i = 0; i < r.Buttons.Length && i < b.Buttons.Length; i++)
                    if (r.Buttons[i] && !b.Buttons[i]) return "b" + i;
                for (int i = 0; i < r.Hats.Length && i < b.Hats.Length; i++)
                {
                    int v = r.Hats[i];
                    if (v != 0 && v != b.Hats[i] && (v == 1 || v == 2 || v == 4 || v == 8)) return "h" + i + "." + v;
                }
            }

            // Axes: pick the one that moved furthest from rest.
            int best = -1, bestDelta = 0;
            for (int i = 0; i < r.Axes.Length && i < b.Axes.Length; i++)
            {
                int d = r.Axes[i] - b.Axes[i];
                if (Math.Abs(d) > Math.Abs(bestDelta)) { bestDelta = d; best = i; }
            }
            if (best < 0 || Math.Abs(bestDelta) < 20000) return null;

            if (step.Stick) return bestDelta > 0 ? "a" + best : "a" + best + "~";   // SDL: right/down are positive
            if (step.Trigger)
            {
                if (b.Axes[best] < -16000) return "a" + best;           // full-range trigger resting at -32768
                return bestDelta > 0 ? "+a" + best : "-a" + best;       // half-axis trigger
            }
            return bestDelta > 0 ? "+a" + best : "-a" + best;           // a button reported as an axis
        }

        void Finish()
        {
            var sb = new StringBuilder();
            string name = pad.Name.Replace(",", " ").Trim();
            if (name.Length == 0) name = "Controller";
            sb.Append(pad.Guid).Append(',').Append(name).Append(',');
            int n = 0;
            foreach (var s in steps)
            {
                if (s.Binding == null) continue;
                sb.Append(s.Field).Append(':').Append(s.Binding).Append(',');
                n++;
            }
            sb.Append("platform:Windows,");
            if (n < 4) { MessageBox.Show(this, "Set up at least a few buttons first.", "PadMouse"); return; }
            if (string.IsNullOrEmpty(pad.Guid)) { MessageBox.Show(this, "PadMouse couldn't identify this controller.", "PadMouse"); return; }
            finishBtn.Enabled = false;
            status.Text = "Saving…";
            engine.RequestAddMapping(sb.ToString());
        }

        void OnMappingAdded(bool ok, string err)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (ok)
                    {
                        MessageBox.Show(this, pad.Name + " is set up. PadMouse will use it like any other controller." + (err != null ? "\n\nNote: " + err : ""),
                            "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Close();
                    }
                    else
                    {
                        finishBtn.Enabled = true;
                        status.ForeColor = Theme.Danger;
                        status.Text = "Couldn't save: " + err;
                    }
                });
            }
            catch (InvalidOperationException) { }
        }
    }
}

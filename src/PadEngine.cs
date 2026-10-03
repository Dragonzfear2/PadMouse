using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PadMouse
{
    /// <summary>Commands the engine sends to the on-screen keyboard.</summary>
    public enum OskCommand { Up, Down, Left, Right, Press, Backspace, Space, Enter, Shift, CaretLeft, CaretRight, Close }

    public enum ChordKind { Toggle, NextProfile, PrevProfile }

    /// <summary>
    /// Polls the controller on a background thread (~200 Hz) and turns it into mouse/keyboard input.
    /// All input state lives on that thread; other threads talk to it via the Request* methods
    /// and read the volatile Live*/Battery* fields.
    /// </summary>
    public class PadEngine
    {
        const int ButtonCount = 16;

        // ---- shared with other threads
        volatile Config pendingConfig;
        volatile int pendingEnabled = -1;      // -1 = no request, 0 = off, 1 = on
        volatile int pendingProfile = -1;      // -1 = no request
        volatile bool enabled;
        volatile bool paused;
        volatile bool running;
        volatile int activeProfile;
        public volatile bool OskVisible;
        public volatile int ConnectedSlot = -1;
        public volatile uint LiveMask;
        public volatile int LiveLX, LiveLY, LiveRX, LiveRY;   // raw stick values
        public volatile int LiveLT, LiveRT;                   // 0..255
        public volatile int BatteryType = -1;                 // XInput.BATTERY_TYPE_*, -1 = unknown
        public volatile int BatteryLevel = -1;                // 0 empty .. 3 full

        public bool Enabled { get { return enabled; } }
        public bool Paused { get { return paused; } }
        public bool Active { get { return enabled && !paused; } }
        public int ActiveProfile { get { return activeProfile; } }

        /// <summary>Events are raised on the engine thread. Subscribers must marshal to the UI themselves.</summary>
        public event Action<bool> EnabledChanged;
        public event Action<bool> ConnectionChanged;
        public event Action<int, bool> ProfileChanged;        // index, changedByController
        public event Action BatteryChanged;
        public event Action ToggleKeyboard;
        public event Action<OskCommand> KeyboardCommand;

        // ---- engine-thread state
        Config cfg;
        ButtonAction[][] profileActions = new ButtonAction[0][];
        ButtonAction[] actions = new ButtonAction[ButtonCount];
        uint[] chordMasks = new uint[3];
        uint chordUnion;
        uint fullDefer;    // buttons shared by several combos (View by default): act only on release
        uint graceDefer;   // buttons in one combo: wait a moment in case the combo is being pressed
        const double Grace = 0.08;
        readonly bool[] pending = new bool[ButtonCount];
        readonly double[] pendingAt = new double[ButtonCount];
        Thread thread;

        uint prevMask;
        bool ltDown, rtDown;
        bool chordConsumed;
        bool modeChanged;
        double nextScan, nextBattery;

        readonly bool[] held = new bool[ButtonCount];
        readonly double[] nextRepeat = new double[ButtonCount];
        readonly int[] mouseRef = new int[5];
        readonly int[] keyRef = new int[256];

        double moveAccX, moveAccY, scrollAccX, scrollAccY;
        double rumbleOffAt = -1;
        bool wasActive;

        bool oskMode;
        int navDir = -1;
        double nextNav;

        public PadEngine(Config config)
        {
            ApplyConfigNow(config);
            int def = config.IndexOfProfile(config.DefaultProfile);
            SelectProfile(def < 0 ? 0 : def);
            enabled = config.StartEnabled;
            wasActive = enabled;
        }

        public void Start()
        {
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "PadMouse engine", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) thread.Join(1000);
        }

        public void RequestEnabled(bool on) { pendingEnabled = on ? 1 : 0; }
        public void RequestConfig(Config c) { pendingConfig = c; }
        public void RequestProfile(int index) { pendingProfile = index; }

        /// <summary>Pause (e.g. while a fullscreen game is in front). Unlike Enabled, it ignores the controller completely.</summary>
        public void SetPaused(bool p) { paused = p; }

        // ------------------------------------------------------------------ loop

        void Run()
        {
            try { Win32.timeBeginPeriod(1); } catch { }
            var sw = Stopwatch.StartNew();
            double last = 0;
            try
            {
                while (running)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    double dt = Math.Min(0.1, now - last);
                    last = now;
                    try { Step(now, dt); }
                    catch (Exception ex) { Log(ex); }
                    Thread.Sleep(5);
                }
            }
            finally
            {
                ReleaseAll();
                if (ConnectedSlot >= 0) XInput.Vibrate(ConnectedSlot, 0, 0);
                try { Win32.timeEndPeriod(1); } catch { }
            }
        }

        void Step(double now, double dt)
        {
            Config nc = pendingConfig;
            if (nc != null)
            {
                pendingConfig = null;
                ReleaseAll();
                string current = cfg.Profiles[Math.Min(activeProfile, cfg.Profiles.Count - 1)].Name;
                ApplyConfigNow(nc);
                int idx = nc.IndexOfProfile(current);
                SelectProfile(idx < 0 ? 0 : idx);
            }

            int pe = pendingEnabled;
            if (pe >= 0) { pendingEnabled = -1; SetEnabled(pe == 1); }

            int pp = pendingProfile;
            if (pp >= 0) { pendingProfile = -1; if (pp != activeProfile && pp < profileActions.Length) { ReleaseAll(); SelectProfile(pp); Raise(ProfileChanged, pp, false); } }

            bool active = enabled && !paused;
            if (!active && wasActive) ReleaseAll();
            wasActive = active;

            if (rumbleOffAt >= 0 && now >= rumbleOffAt) { XInput.Vibrate(ConnectedSlot, 0, 0); rumbleOffAt = -1; }

            // Find a controller (scan once a second while none is connected).
            XInputState st;
            if (ConnectedSlot < 0)
            {
                if (now < nextScan) return;
                nextScan = now + 1.0;
                for (int i = 0; i < 4; i++)
                {
                    if (XInput.TryGetState(i, out st))
                    {
                        ConnectedSlot = i;
                        prevMask = 0; chordConsumed = false;
                        nextBattery = now;
                        Raise(ConnectionChanged, true);
                        break;
                    }
                }
                if (ConnectedSlot < 0) return;
            }
            if (!XInput.TryGetState(ConnectedSlot, out st))
            {
                ConnectedSlot = -1;
                ReleaseAll();
                prevMask = 0; LiveMask = 0;
                LiveLX = LiveLY = LiveRX = LiveRY = LiveLT = LiveRT = 0;
                BatteryType = BatteryLevel = -1;
                Raise(ConnectionChanged, false);
                return;
            }

            if (now >= nextBattery) { nextBattery = now + 30; PollBattery(); }

            var g = st.Gamepad;
            uint mask = ReadButtons(g);
            LiveMask = mask;
            LiveLX = g.sThumbLX; LiveLY = g.sThumbLY; LiveRX = g.sThumbRX; LiveRY = g.sThumbRY;
            LiveLT = g.bLeftTrigger; LiveRT = g.bRightTrigger;

            if (paused)
            {
                // A game has the controller: don't react to anything, including combos.
                prevMask = mask;
                chordConsumed = (mask & chordUnion) != 0;
                return;
            }

            uint pressed = mask & ~prevMask;
            uint released = prevMask & ~mask;

            // Chords fire when their last button goes down. Prefer the chord with the most buttons.
            int fired = -1; int bestBits = 0;
            for (int c = 0; c < chordMasks.Length; c++)
            {
                uint m = chordMasks[c];
                if (m == 0) continue;
                if ((mask & m) == m && (prevMask & m) != m)
                {
                    if (!enabled && c != (int)ChordKind.Toggle) continue; // while off, only the on/off combo works
                    int bits = Bits(m);
                    if (bits > bestBits) { bestBits = bits; fired = c; }
                }
            }
            if (fired >= 0)
            {
                chordConsumed = true;
                ReleaseAll();
                ClearPending();
                switch ((ChordKind)fired)
                {
                    case ChordKind.Toggle: DoToggle(now); break;
                    case ChordKind.NextProfile: CycleProfile(1, now); break;
                    case ChordKind.PrevProfile: CycleProfile(-1, now); break;
                }
            }

            // Shared combo buttons tap on release, unless a combo was used.
            uint deferredTaps = 0;
            if (!chordConsumed) deferredTaps = released & fullDefer;

            if (!enabled)
            {
                // While off, buttons mapped to "Toggle" still work so you can switch back on.
                uint taps = (pressed & ~fullDefer) | deferredTaps;
                for (int i = 0; i < ButtonCount; i++)
                    if ((taps & (1u << i)) != 0 && actions[i].Kind == ActionKind.Toggle) { Down(i, now); break; }
            }
            else if (fired < 0)
            {
                if (OskVisible) StepKeyboard(g, pressed, deferredTaps, now, dt);
                else StepNormal(g, pressed, released, deferredTaps, now, dt);
            }

            prevMask = mask;
            if ((mask & chordUnion) == 0) chordConsumed = false;
        }

        static int Bits(uint v) { int c = 0; while (v != 0) { c += (int)(v & 1); v >>= 1; } return c; }

        void DoToggle(double now)
        {
            SetEnabled(!enabled);
            if (enabled) rumbleOffAt = Rumble(now, 0, 30000, 0.12);
            else rumbleOffAt = Rumble(now, 40000, 0, 0.25);
        }

        void CycleProfile(int delta, double now)
        {
            int n = profileActions.Length;
            if (n <= 1) return;
            int idx = ((activeProfile + delta) % n + n) % n;
            ReleaseAll();
            SelectProfile(idx);
            rumbleOffAt = Rumble(now, 0, 18000, 0.07);
            Raise(ProfileChanged, idx, true);
        }

        void PollBattery()
        {
            int type, level;
            if (!XInput.TryGetBattery(ConnectedSlot, out type, out level)) return;
            if (type != BatteryType || level != BatteryLevel)
            {
                BatteryType = type; BatteryLevel = level;
                Raise(BatteryChanged);
            }
        }

        double Rumble(double now, ushort left, ushort right, double secs)
        {
            if (!cfg.Rumble) return rumbleOffAt;
            XInput.Vibrate(ConnectedSlot, left, right);
            return now + secs;
        }

        uint ReadButtons(XInputGamepad g)
        {
            uint m = 0;
            ushort w = g.wButtons;
            if ((w & XInput.A) != 0) m |= Bit(PadButton.A);
            if ((w & XInput.B) != 0) m |= Bit(PadButton.B);
            if ((w & XInput.X) != 0) m |= Bit(PadButton.X);
            if ((w & XInput.Y) != 0) m |= Bit(PadButton.Y);
            if ((w & XInput.LEFT_SHOULDER) != 0) m |= Bit(PadButton.LB);
            if ((w & XInput.RIGHT_SHOULDER) != 0) m |= Bit(PadButton.RB);
            if ((w & XInput.BACK) != 0) m |= Bit(PadButton.Back);
            if ((w & XInput.START) != 0) m |= Bit(PadButton.Start);
            if ((w & XInput.LEFT_THUMB) != 0) m |= Bit(PadButton.L3);
            if ((w & XInput.RIGHT_THUMB) != 0) m |= Bit(PadButton.R3);
            if ((w & XInput.DPAD_UP) != 0) m |= Bit(PadButton.DPadUp);
            if ((w & XInput.DPAD_DOWN) != 0) m |= Bit(PadButton.DPadDown);
            if ((w & XInput.DPAD_LEFT) != 0) m |= Bit(PadButton.DPadLeft);
            if ((w & XInput.DPAD_RIGHT) != 0) m |= Bit(PadButton.DPadRight);

            // Triggers are analogue; add a little hysteresis so they don't chatter.
            double thr = cfg.TriggerThreshold;
            double lt = g.bLeftTrigger / 255.0, rt = g.bRightTrigger / 255.0;
            if (lt >= thr) ltDown = true; else if (lt < thr * 0.7) ltDown = false;
            if (rt >= thr) rtDown = true; else if (rt < thr * 0.7) rtDown = false;
            if (ltDown) m |= Bit(PadButton.LT);
            if (rtDown) m |= Bit(PadButton.RT);
            return m;
        }

        public static uint Bit(PadButton b) { return 1u << (int)b; }

        // ------------------------------------------------------------------ normal (mouse) mode

        void StepNormal(XInputGamepad g, uint pressed, uint released, uint deferredTaps, double now, double dt)
        {
            if (oskMode) { oskMode = false; navDir = -1; }

            modeChanged = false;
            for (int i = 0; i < ButtonCount; i++)
            {
                uint bit = 1u << i;
                if ((fullDefer & bit) != 0)
                {
                    if ((deferredTaps & bit) != 0) { Down(i, now); Up(i); }
                }
                else if ((graceDefer & bit) != 0)
                {
                    if ((pressed & bit) != 0) { pending[i] = true; pendingAt[i] = now; }
                    if ((released & bit) != 0)
                    {
                        if (pending[i]) { pending[i] = false; if (!chordConsumed) { Down(i, now); Up(i); } }
                        else Up(i);
                    }
                    else if (pending[i] && now - pendingAt[i] >= Grace)
                    {
                        pending[i] = false;
                        if (!chordConsumed) Down(i, now);
                    }
                }
                else
                {
                    if ((pressed & bit) != 0) Down(i, now);
                    if ((released & bit) != 0) Up(i);
                }
                if (modeChanged) return;
            }

            bool precise = false;
            for (int i = 0; i < ButtonCount; i++)
            {
                if (!held[i]) continue;
                if (actions[i].Kind == ActionKind.Precision) precise = true;
                if (now >= nextRepeat[i]) Repeat(i, now);
            }
            double factor = precise ? cfg.PrecisionFactor : 1.0;

            var prof = cfg.Profiles[activeProfile];
            double lx, ly, rx, ry;
            Stick(g.sThumbLX, g.sThumbLY, out lx, out ly);
            Stick(g.sThumbRX, g.sThumbRY, out rx, out ry);
            double mx = 0, my = 0, sx = 0, sy = 0;
            Route(prof.LeftStick, lx, ly, ref mx, ref my, ref sx, ref sy);
            Route(prof.RightStick, rx, ry, ref mx, ref my, ref sx, ref sy);

            MoveCursor(mx, my, factor, dt);
            Scroll(sx, sy, factor, dt);
        }

        static void Route(StickMode mode, double x, double y, ref double mx, ref double my, ref double sx, ref double sy)
        {
            if (mode == StickMode.Mouse) { mx += x; my += y; }
            else if (mode == StickMode.Scroll) { sx += x; sy += y; }
        }

        void MoveCursor(double x, double y, double factor, double dt)
        {
            if (x == 0 && y == 0) { moveAccX = moveAccY = 0; return; }
            double speed = cfg.CursorSpeed * factor;
            moveAccX += x * speed * dt;
            moveAccY -= y * speed * dt;   // stick up = screen up
            int dx = (int)moveAccX, dy = (int)moveAccY;
            moveAccX -= dx; moveAccY -= dy;
            InputSender.MoveCursorBy(dx, dy);
        }

        void Scroll(double x, double y, double factor, double dt)
        {
            if (x == 0 && y == 0) { scrollAccX = scrollAccY = 0; return; }
            double sign = cfg.InvertScroll ? -1 : 1;
            scrollAccY += y * cfg.ScrollSpeed * factor * dt * sign;
            scrollAccX += x * cfg.ScrollSpeed * factor * dt * sign;
            int step = cfg.SmoothScroll ? 15 : 120;   // 15 = 1/8 notch, keeps message rate sane
            int vy = ((int)(scrollAccY / step)) * step;
            int vx = ((int)(scrollAccX / step)) * step;
            if (vy != 0) { InputSender.Wheel(vy, false); scrollAccY -= vy; }
            if (vx != 0) { InputSender.Wheel(vx, true); scrollAccX -= vx; }
        }

        /// <summary>Applies the radial deadzone and response curve. Output is in -1..1.</summary>
        public static void ShapeStick(short rawX, short rawY, double deadzone, double curve, out double x, out double y)
        {
            double fx = Math.Max(-1.0, rawX / 32767.0), fy = Math.Max(-1.0, rawY / 32767.0);
            double mag = Math.Sqrt(fx * fx + fy * fy);
            if (mag <= deadzone || mag == 0) { x = y = 0; return; }
            double m = Math.Min(1.0, (mag - deadzone) / (1.0 - deadzone));
            double scaled = Math.Pow(m, curve);
            x = fx / mag * scaled;
            y = fy / mag * scaled;
        }

        void Stick(short rawX, short rawY, out double x, out double y)
        {
            ShapeStick(rawX, rawY, cfg.Deadzone, cfg.Curve, out x, out y);
        }

        // ------------------------------------------------------------------ actions

        void Down(int i, double now)
        {
            if (held[i]) return;
            var a = actions[i];
            held[i] = true;
            nextRepeat[i] = now + cfg.KeyRepeatDelay / 1000.0;
            switch (a.Kind)
            {
                case ActionKind.LeftClick: MouseDown(MouseBtn.Left); break;
                case ActionKind.RightClick: MouseDown(MouseBtn.Right); break;
                case ActionKind.MiddleClick: MouseDown(MouseBtn.Middle); break;
                case ActionKind.MouseBack: MouseDown(MouseBtn.Back); break;
                case ActionKind.MouseForward: MouseDown(MouseBtn.Forward); break;
                case ActionKind.DoubleClick:
                    InputSender.MouseButton(MouseBtn.Left, true); InputSender.MouseButton(MouseBtn.Left, false);
                    InputSender.MouseButton(MouseBtn.Left, true); InputSender.MouseButton(MouseBtn.Left, false);
                    break;
                case ActionKind.ScrollUp: InputSender.Wheel(120, false); break;
                case ActionKind.ScrollDown: InputSender.Wheel(-120, false); break;
                case ActionKind.ScrollLeft: InputSender.Wheel(-120, true); break;
                case ActionKind.ScrollRight: InputSender.Wheel(120, true); break;
                case ActionKind.Key:
                    foreach (ushort vk in a.Keys) if (keyRef[vk]++ == 0) InputSender.Key(vk, true);
                    break;
                case ActionKind.Keyboard:
                    held[i] = false;
                    modeChanged = true;
                    ReleaseAll();
                    Raise(ToggleKeyboard);
                    break;
                case ActionKind.Toggle:
                    held[i] = false;
                    modeChanged = true;
                    ReleaseAll();
                    DoToggle(now);
                    break;
                case ActionKind.NextProfile:
                case ActionKind.PrevProfile:
                    held[i] = false;
                    modeChanged = true;
                    CycleProfile(a.Kind == ActionKind.NextProfile ? 1 : -1, now);
                    break;
            }
        }

        void Repeat(int i, double now)
        {
            var a = actions[i];
            nextRepeat[i] = now + cfg.KeyRepeatRate / 1000.0;
            switch (a.Kind)
            {
                case ActionKind.ScrollUp: InputSender.Wheel(120, false); break;
                case ActionKind.ScrollDown: InputSender.Wheel(-120, false); break;
                case ActionKind.ScrollLeft: InputSender.Wheel(-120, true); break;
                case ActionKind.ScrollRight: InputSender.Wheel(120, true); break;
                case ActionKind.Key:
                    int rk = a.RepeatKey;
                    if (rk >= 0) InputSender.Key((ushort)rk, true);
                    break;
            }
        }

        void Up(int i)
        {
            if (!held[i]) return;
            held[i] = false;
            var a = actions[i];
            switch (a.Kind)
            {
                case ActionKind.LeftClick: MouseUp(MouseBtn.Left); break;
                case ActionKind.RightClick: MouseUp(MouseBtn.Right); break;
                case ActionKind.MiddleClick: MouseUp(MouseBtn.Middle); break;
                case ActionKind.MouseBack: MouseUp(MouseBtn.Back); break;
                case ActionKind.MouseForward: MouseUp(MouseBtn.Forward); break;
                case ActionKind.Key:
                    for (int k = a.Keys.Length - 1; k >= 0; k--)
                    {
                        ushort vk = a.Keys[k];
                        if (keyRef[vk] > 0 && --keyRef[vk] == 0) InputSender.Key(vk, false);
                    }
                    break;
            }
        }

        void MouseDown(MouseBtn b) { if (mouseRef[(int)b]++ == 0) InputSender.MouseButton(b, true); }
        void MouseUp(MouseBtn b) { if (mouseRef[(int)b] > 0 && --mouseRef[(int)b] == 0) InputSender.MouseButton(b, false); }

        /// <summary>Lets go of every held mouse button and key, so nothing gets stuck.</summary>
        void ReleaseAll()
        {
            for (int i = 0; i < ButtonCount; i++) if (held[i]) Up(i);
            for (int b = 0; b < mouseRef.Length; b++)
                if (mouseRef[b] > 0) { mouseRef[b] = 0; InputSender.MouseButton((MouseBtn)b, false); }
            for (int vk = 0; vk < keyRef.Length; vk++)
                if (keyRef[vk] > 0) { keyRef[vk] = 0; InputSender.Key((ushort)vk, false); }
            moveAccX = moveAccY = scrollAccX = scrollAccY = 0;
            ClearPending();
        }

        void ClearPending() { for (int i = 0; i < ButtonCount; i++) pending[i] = false; }

        void SetEnabled(bool on)
        {
            if (on == enabled) return;
            if (!on) ReleaseAll();
            enabled = on;
            Raise(EnabledChanged, on);
        }

        void ApplyConfigNow(Config c)
        {
            cfg = c;
            profileActions = new ButtonAction[c.Profiles.Count][];
            for (int p = 0; p < c.Profiles.Count; p++)
            {
                var arr = new ButtonAction[ButtonCount];
                for (int i = 0; i < ButtonCount; i++)
                {
                    ButtonAction a; string err;
                    if (!ButtonAction.TryParse(c.Profiles[p].Get((PadButton)i), out a, out err)) a = ButtonAction.None;
                    arr[i] = a;
                }
                profileActions[p] = arr;
            }
            uint m; string e;
            chordMasks[(int)ChordKind.Toggle] = Config.TryParseCombo(c.ToggleCombo, out m, out e) ? m : 0;
            chordMasks[(int)ChordKind.NextProfile] = Config.TryParseCombo(c.NextProfileCombo, out m, out e) ? m : 0;
            chordMasks[(int)ChordKind.PrevProfile] = Config.TryParseCombo(c.PrevProfileCombo, out m, out e) ? m : 0;
            chordUnion = chordMasks[0] | chordMasks[1] | chordMasks[2];
            fullDefer = (chordMasks[0] & chordMasks[1]) | (chordMasks[0] & chordMasks[2]) | (chordMasks[1] & chordMasks[2]);
            graceDefer = chordUnion & ~fullDefer;
            chordConsumed = (prevMask & chordUnion) != 0; // don't fire taps for buttons already held
        }

        void SelectProfile(int idx)
        {
            if (idx < 0 || idx >= profileActions.Length) idx = 0;
            activeProfile = idx;
            actions = profileActions[idx];
        }

        // ------------------------------------------------------------------ on-screen keyboard mode

        void StepKeyboard(XInputGamepad g, uint pressed, uint deferredTaps, double now, double dt)
        {
            if (!oskMode) { ReleaseAll(); oskMode = true; navDir = -1; }

            // Edges: normal buttons on press; chord buttons on release (if no chord was used).
            uint taps = (pressed & ~fullDefer) | deferredTaps;
            if (Has(taps, PadButton.A)) Cmd(OskCommand.Press);
            if (Has(taps, PadButton.B)) Cmd(OskCommand.Backspace);
            if (Has(taps, PadButton.X)) Cmd(OskCommand.Space);
            if (Has(taps, PadButton.Y) || Has(taps, PadButton.Back)) Cmd(OskCommand.Close);
            if (Has(taps, PadButton.LB)) Cmd(OskCommand.Shift);
            if (Has(taps, PadButton.RB) || Has(taps, PadButton.Start)) Cmd(OskCommand.Enter);
            if (Has(taps, PadButton.LT)) Cmd(OskCommand.CaretLeft);
            if (Has(taps, PadButton.RT)) Cmd(OskCommand.CaretRight);

            // Navigation: D-pad or left stick, with auto-repeat while held.
            uint mask = LiveMask;
            int dir = -1;
            if (Has(mask, PadButton.DPadUp)) dir = (int)OskCommand.Up;
            else if (Has(mask, PadButton.DPadDown)) dir = (int)OskCommand.Down;
            else if (Has(mask, PadButton.DPadLeft)) dir = (int)OskCommand.Left;
            else if (Has(mask, PadButton.DPadRight)) dir = (int)OskCommand.Right;
            else
            {
                double lx = g.sThumbLX / 32767.0, ly = g.sThumbLY / 32767.0;
                if (Math.Max(Math.Abs(lx), Math.Abs(ly)) > 0.55)
                {
                    if (Math.Abs(lx) > Math.Abs(ly)) dir = (int)(lx > 0 ? OskCommand.Right : OskCommand.Left);
                    else dir = (int)(ly > 0 ? OskCommand.Up : OskCommand.Down);
                }
            }
            if (dir != navDir)
            {
                navDir = dir;
                if (dir >= 0) { Cmd((OskCommand)dir); nextNav = now + 0.35; }
            }
            else if (dir >= 0 && now >= nextNav)
            {
                Cmd((OskCommand)dir);
                nextNav = now + 0.11;
            }

            // Right stick still moves the mouse, so you can click elsewhere without closing the keyboard.
            double rx, ry;
            Stick(g.sThumbRX, g.sThumbRY, out rx, out ry);
            MoveCursor(rx, ry, 1.0, dt);
        }

        static bool Has(uint mask, PadButton b) { return (mask & Bit(b)) != 0; }

        void Cmd(OskCommand c)
        {
            var h = KeyboardCommand;
            if (h != null) h(c);
        }

        // ------------------------------------------------------------------ helpers

        void Raise(Action a) { if (a != null) a(); }
        void Raise(Action<bool> a, bool v) { if (a != null) a(v); }
        void Raise(Action<int, bool> a, int i, bool v) { if (a != null) a(i, v); }

        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Config.Folder);
                string path = Path.Combine(Config.Folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now + " " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}

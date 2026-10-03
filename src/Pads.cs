using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PadMouse
{
    public enum PadFamily { Xbox, PlayStation, Nintendo, Generic }

    /// <summary>One connected controller. Everything is normalised to the XInput layout.</summary>
    public abstract class Pad
    {
        public string Name = "Controller";
        public PadFamily Family = PadFamily.Xbox;
        public string Key = "";

        /// <summary>Reads the current state. Returns false once the controller has gone.</summary>
        public abstract bool Poll(out XInputGamepad g);
        public virtual void Rumble(ushort left, ushort right) { }
        public virtual bool TryBattery(out int type, out int level) { type = -1; level = -1; return false; }
        public virtual void Close() { }
    }

    public class XInputPad : Pad
    {
        public readonly int Slot;
        public XInputPad(int slot)
        {
            Slot = slot;
            Key = "xinput:" + slot;
            Name = "Xbox controller";
            Family = PadFamily.Xbox;
        }

        public override bool Poll(out XInputGamepad g)
        {
            XInputState st;
            bool ok = XInput.TryGetState(Slot, out st);
            g = st.Gamepad;
            return ok;
        }

        public override void Rumble(ushort left, ushort right) { XInput.Vibrate(Slot, left, right); }
        public override bool TryBattery(out int type, out int level) { return XInput.TryGetBattery(Slot, out type, out level); }
    }

    /// <summary>A controller read through SDL's game controller API (PlayStation, Switch, mapped generic pads).</summary>
    public class SdlPad : Pad
    {
        readonly IntPtr gc;
        public readonly int InstanceId;

        public SdlPad(IntPtr gc, int instanceId)
        {
            this.gc = gc;
            InstanceId = instanceId;
            Key = "sdl:" + instanceId;
            string n = Sdl.FromUtf8(Sdl.SDL_GameControllerName(gc));
            Family = FamilyOf(Sdl.SDL_GameControllerGetType(gc), n);
            Name = FriendlyName(Family, n);
        }

        public static PadFamily FamilyOf(int type, string name)
        {
            switch (type)
            {
                case Sdl.TYPE_PS3: case Sdl.TYPE_PS4: case Sdl.TYPE_PS5: return PadFamily.PlayStation;
                case Sdl.TYPE_SWITCH_PRO: case Sdl.TYPE_JOYCON_LEFT: case Sdl.TYPE_JOYCON_RIGHT: case Sdl.TYPE_JOYCON_PAIR: return PadFamily.Nintendo;
                case Sdl.TYPE_XBOX360: case Sdl.TYPE_XBOXONE: return PadFamily.Xbox;
            }
            string l = (name ?? "").ToLowerInvariant();
            if (l.Contains("playstation") || l.Contains("dualshock") || l.Contains("dualsense") || l.Contains("ps4") || l.Contains("ps5")) return PadFamily.PlayStation;
            if (l.Contains("nintendo") || l.Contains("switch") || l.Contains("joy-con")) return PadFamily.Nintendo;
            return PadFamily.Generic;
        }

        static string FriendlyName(PadFamily f, string sdlName)
        {
            if (string.IsNullOrEmpty(sdlName)) sdlName = "Controller";
            return sdlName;
        }

        static short InvertY(short v) { return v == short.MinValue ? short.MaxValue : (short)(-v); }

        public override bool Poll(out XInputGamepad g)
        {
            g = new XInputGamepad();
            if (Sdl.SDL_GameControllerGetAttached(gc) == 0) return false;
            ushort w = 0;
            if (B(Sdl.BTN_A)) w |= XInput.A;
            if (B(Sdl.BTN_B)) w |= XInput.B;
            if (B(Sdl.BTN_X)) w |= XInput.X;
            if (B(Sdl.BTN_Y)) w |= XInput.Y;
            if (B(Sdl.BTN_BACK)) w |= XInput.BACK;
            if (B(Sdl.BTN_START)) w |= XInput.START;
            if (B(Sdl.BTN_LEFTSTICK)) w |= XInput.LEFT_THUMB;
            if (B(Sdl.BTN_RIGHTSTICK)) w |= XInput.RIGHT_THUMB;
            if (B(Sdl.BTN_LEFTSHOULDER)) w |= XInput.LEFT_SHOULDER;
            if (B(Sdl.BTN_RIGHTSHOULDER)) w |= XInput.RIGHT_SHOULDER;
            if (B(Sdl.BTN_DPAD_UP)) w |= XInput.DPAD_UP;
            if (B(Sdl.BTN_DPAD_DOWN)) w |= XInput.DPAD_DOWN;
            if (B(Sdl.BTN_DPAD_LEFT)) w |= XInput.DPAD_LEFT;
            if (B(Sdl.BTN_DPAD_RIGHT)) w |= XInput.DPAD_RIGHT;
            g.wButtons = w;
            g.sThumbLX = Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_LEFTX);
            g.sThumbLY = InvertY(Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_LEFTY));   // SDL: down is positive
            g.sThumbRX = Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_RIGHTX);
            g.sThumbRY = InvertY(Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_RIGHTY));
            g.bLeftTrigger = Trigger(Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_TRIGGERLEFT));
            g.bRightTrigger = Trigger(Sdl.SDL_GameControllerGetAxis(gc, Sdl.AXIS_TRIGGERRIGHT));
            return true;
        }

        bool B(int b) { return Sdl.SDL_GameControllerGetButton(gc, b) != 0; }
        static byte Trigger(short v) { return (byte)Math.Max(0, Math.Min(255, v / 128)); }

        public override void Rumble(ushort left, ushort right)
        {
            try { Sdl.SDL_GameControllerRumble(gc, left, right, left == 0 && right == 0 ? 0u : 400u); } catch { }
        }

        public override bool TryBattery(out int type, out int level)
        {
            type = -1; level = -1;
            int p = Sdl.SDL_JoystickCurrentPowerLevel(Sdl.SDL_GameControllerGetJoystick(gc));
            switch (p)
            {
                case Sdl.POWER_WIRED: type = XInput.BATTERY_TYPE_WIRED; level = 3; return true;
                case Sdl.POWER_EMPTY: type = XInput.BATTERY_TYPE_NIMH; level = 0; return true;
                case Sdl.POWER_LOW: type = XInput.BATTERY_TYPE_NIMH; level = 1; return true;
                case Sdl.POWER_MEDIUM: type = XInput.BATTERY_TYPE_NIMH; level = 2; return true;
                case Sdl.POWER_FULL: case Sdl.POWER_MAX: type = XInput.BATTERY_TYPE_NIMH; level = 3; return true;
            }
            return false;
        }

        public override void Close() { try { Sdl.SDL_GameControllerClose(gc); } catch { } }
    }

    /// <summary>What the UI is shown about a connected controller.</summary>
    public class PadInfo
    {
        public string Name;
        public PadFamily Family;
        public bool Active;
        public bool NeedsSetup;     // generic joystick SDL doesn't know: run the set-up wizard
        public int InstanceId = -1; // SDL instance for unrecognised joysticks
        public string Guid = "";
    }

    /// <summary>Live readings from an unrecognised joystick while the set-up wizard runs.</summary>
    public class RawJoystickState
    {
        public short[] Axes = new short[0];
        public bool[] Buttons = new bool[0];
        public byte[] Hats = new byte[0];
    }

    /// <summary>
    /// Finds every controller (XInput first, then SDL), polls them, and follows whichever one
    /// you used last. Lives on the engine thread.
    /// </summary>
    public class PadManager
    {
        readonly List<Pad> pads = new List<Pad>();
        readonly Dictionary<int, string> unmapped = new Dictionary<int, string>(); // instance -> name
        readonly Dictionary<int, string> unmappedGuid = new Dictionary<int, string>();
        Pad active;
        bool sdlReady;
        double nextScan;
        readonly Dictionary<string, ushort> lastButtons = new Dictionary<string, ushort>();

        // raw capture for the set-up wizard
        IntPtr rawJoy = IntPtr.Zero;
        public volatile RawJoystickState Raw;

        public volatile PadInfo[] Connected = new PadInfo[0];
        public Pad Active { get { return active; } }
        public bool SdlAvailable { get { return sdlReady; } }
        public string SdlProblem { get; private set; }

        public static string MappingsFile { get { return Path.Combine(Config.Folder, "controller-mappings.txt"); } }

        public void Init()
        {
            if (!Sdl.TryLoad()) { SdlProblem = Sdl.LoadError; return; }
            try
            {
                Sdl.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");   // we never have focus
                Sdl.SetHint("SDL_XINPUT_ENABLED", "0");                      // Xbox pads are read via XInput directly
                Sdl.SetHint("SDL_JOYSTICK_RAWINPUT", "0");
                Sdl.SetHint("SDL_JOYSTICK_THREAD", "1");
                Sdl.SetHint("SDL_GAMECONTROLLER_USE_BUTTON_LABELS", "0");   // A = bottom button on every controller
                if (Sdl.SDL_Init(Sdl.INIT_GAMECONTROLLER | Sdl.INIT_JOYSTICK | Sdl.INIT_EVENTS) != 0)
                {
                    SdlProblem = Sdl.Error;
                    return;
                }
                if (File.Exists(MappingsFile))
                    foreach (string line in File.ReadAllLines(MappingsFile))
                        if (line.Trim().Length > 0 && !line.StartsWith("#")) Sdl.AddMapping(line.Trim());
                sdlReady = true;
            }
            catch (Exception ex) { SdlProblem = ex.Message; }
        }

        public void Shutdown()
        {
            StopRaw();
            foreach (var p in pads) p.Close();
            pads.Clear();
            if (sdlReady) { try { Sdl.SDL_Quit(); } catch { } sdlReady = false; }
        }

        /// <summary>Call every frame. Returns the active controller's state (false if none).</summary>
        public bool Update(double now, out XInputGamepad state, out bool activeChanged)
        {
            state = new XInputGamepad();
            activeChanged = false;
            if (sdlReady)
            {
                Sdl.SDL_PumpEvents();
                Sdl.SDL_FlushEvents(0, 0xFFFF);   // we read state directly, so drop queued events
            }
            if (now >= nextScan) { nextScan = now + 1.0; Scan(); }

            Pad switchTo = null;
            XInputGamepad activeState = new XInputGamepad();
            for (int i = pads.Count - 1; i >= 0; i--)
            {
                var p = pads[i];
                XInputGamepad g;
                if (!p.Poll(out g))
                {
                    p.Close();
                    pads.RemoveAt(i);
                    lastButtons.Remove(p.Key);
                    if (p == active) { active = null; activeChanged = true; }
                    PublishList();
                    continue;
                }
                ushort prev;
                lastButtons.TryGetValue(p.Key, out prev);
                bool newPress = (g.wButtons & ~prev) != 0;
                lastButtons[p.Key] = g.wButtons;
                if (p == active) activeState = g;
                else if (newPress || BigStick(g)) switchTo = p;   // you picked up a different controller
            }
            if (active == null && pads.Count > 0) { active = pads[0]; activeChanged = true; Poll(active, out activeState); }
            if (switchTo != null && switchTo != active) { active = switchTo; activeChanged = true; Poll(active, out activeState); }
            if (activeChanged) PublishList();
            if (rawJoy != IntPtr.Zero) CaptureRaw();
            state = activeState;
            return active != null;
        }

        static void Poll(Pad p, out XInputGamepad g) { if (!p.Poll(out g)) g = new XInputGamepad(); }

        static bool BigStick(XInputGamepad g)
        {
            const int T = 24000;
            return Math.Abs((int)g.sThumbLX) > T || Math.Abs((int)g.sThumbLY) > T || Math.Abs((int)g.sThumbRX) > T || Math.Abs((int)g.sThumbRY) > T;
        }

        void Scan()
        {
            bool changed = false;
            // XInput slots
            for (int slot = 0; slot < 4; slot++)
            {
                string key = "xinput:" + slot;
                if (pads.Exists(p => p.Key == key)) continue;
                XInputState st;
                if (XInput.TryGetState(slot, out st)) { pads.Add(new XInputPad(slot)); changed = true; }
            }
            // SDL devices
            if (sdlReady)
            {
                var seen = new HashSet<int>();
                int n = Sdl.SDL_NumJoysticks();
                for (int i = 0; i < n; i++)
                {
                    int inst = Sdl.SDL_JoystickGetDeviceInstanceID(i);
                    seen.Add(inst);
                    string key = "sdl:" + inst;
                    if (pads.Exists(p => p.Key == key)) continue;
                    string name = Sdl.FromUtf8(Sdl.SDL_JoystickNameForIndex(i));
                    if (Sdl.SDL_IsGameController(i) != 0)
                    {
                        int type = Sdl.SDL_GameControllerTypeForIndex(i);
                        if (type == Sdl.TYPE_XBOX360 || type == Sdl.TYPE_XBOXONE || type == Sdl.TYPE_VIRTUAL) continue; // XInput covers these
                        IntPtr gc = Sdl.SDL_GameControllerOpen(i);
                        if (gc == IntPtr.Zero) continue;
                        pads.Add(new SdlPad(gc, inst));
                        if (unmapped.Remove(inst)) unmappedGuid.Remove(inst);
                        changed = true;
                    }
                    else if (!unmapped.ContainsKey(inst) && !LooksLikeXbox(name))
                    {
                        unmapped[inst] = string.IsNullOrEmpty(name) ? "Unknown controller" : name;
                        unmappedGuid[inst] = Sdl.GuidString(Sdl.SDL_JoystickGetDeviceGUID(i));
                        changed = true;
                    }
                }
                foreach (int inst in new List<int>(unmapped.Keys))
                    if (!seen.Contains(inst)) { unmapped.Remove(inst); unmappedGuid.Remove(inst); changed = true; }
            }
            if (changed) PublishList();
        }

        static bool LooksLikeXbox(string name)
        {
            string l = (name ?? "").ToLowerInvariant();
            return l.Contains("xbox") || l.Contains("xinput") || l.Contains("x-box");
        }

        void PublishList()
        {
            var list = new List<PadInfo>();
            foreach (var p in pads) list.Add(new PadInfo { Name = p.Name, Family = p.Family, Active = p == active });
            foreach (var kv in unmapped)
            {
                string g;
                unmappedGuid.TryGetValue(kv.Key, out g);
                list.Add(new PadInfo { Name = kv.Value, Family = PadFamily.Generic, NeedsSetup = true, InstanceId = kv.Key, Guid = g ?? "" });
            }
            Connected = list.ToArray();
        }

        // ------------------------------------------------------------------ set-up wizard support

        public void StartRaw(int instanceId)
        {
            StopRaw();
            if (!sdlReady) return;
            int n = Sdl.SDL_NumJoysticks();
            for (int i = 0; i < n; i++)
            {
                if (Sdl.SDL_JoystickGetDeviceInstanceID(i) != instanceId) continue;
                rawJoy = Sdl.SDL_JoystickOpen(i);
                break;
            }
        }

        public void StopRaw()
        {
            if (rawJoy != IntPtr.Zero) { try { Sdl.SDL_JoystickClose(rawJoy); } catch { } }
            rawJoy = IntPtr.Zero;
            Raw = null;
        }

        void CaptureRaw()
        {
            if (Sdl.SDL_JoystickGetAttached(rawJoy) == 0) { StopRaw(); return; }
            var s = new RawJoystickState();
            int na = Math.Min(16, Sdl.SDL_JoystickNumAxes(rawJoy));
            int nb = Math.Min(64, Sdl.SDL_JoystickNumButtons(rawJoy));
            int nh = Math.Min(4, Sdl.SDL_JoystickNumHats(rawJoy));
            s.Axes = new short[Math.Max(0, na)];
            s.Buttons = new bool[Math.Max(0, nb)];
            s.Hats = new byte[Math.Max(0, nh)];
            for (int i = 0; i < s.Axes.Length; i++) s.Axes[i] = Sdl.SDL_JoystickGetAxis(rawJoy, i);
            for (int i = 0; i < s.Buttons.Length; i++) s.Buttons[i] = Sdl.SDL_JoystickGetButton(rawJoy, i) != 0;
            for (int i = 0; i < s.Hats.Length; i++) s.Hats[i] = Sdl.SDL_JoystickGetHat(rawJoy, i);
            Raw = s;
        }

        /// <summary>Adds an SDL mapping string, saves it for next time and picks the controller up.</summary>
        public bool AddMapping(string mapping, out string error)
        {
            error = null;
            if (!sdlReady) { error = "controller support isn't available"; return false; }
            StopRaw();
            if (!Sdl.AddMapping(mapping)) { error = Sdl.Error; return false; }
            try
            {
                Directory.CreateDirectory(Config.Folder);
                string guid = mapping.Split(',')[0];
                var lines = new List<string>();
                if (File.Exists(MappingsFile))
                    foreach (string l in File.ReadAllLines(MappingsFile))
                        if (!l.StartsWith(guid + ",")) lines.Add(l);
                lines.Add(mapping);
                File.WriteAllLines(MappingsFile, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex) { error = "saved for now, but couldn't write the file: " + ex.Message; }
            nextScan = 0;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PadMouse
{
    public enum PadButton { A, B, X, Y, LB, RB, LT, RT, Back, Start, L3, R3, DPadUp, DPadDown, DPadLeft, DPadRight }

    public enum StickMode { Mouse, Scroll, None }

    public enum ActionKind
    {
        None, LeftClick, RightClick, MiddleClick, DoubleClick, MouseBack, MouseForward,
        ScrollUp, ScrollDown, ScrollLeft, ScrollRight, Keyboard, Precision, Toggle, NextProfile, PrevProfile, Key
    }

    /// <summary>What a controller button does. Text form: "LeftClick", "Keyboard", "Key:Ctrl+C", ...</summary>
    public class ButtonAction
    {
        public ActionKind Kind;
        public ushort[] Keys = new ushort[0];
        public string Text = "None";

        public static readonly ButtonAction None = new ButtonAction();

        public static bool TryParse(string text, out ButtonAction action, out string error)
        {
            action = None; error = null;
            string s = (text ?? "").Trim();
            if (s.Length == 0 || s.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;

            if (s.StartsWith("Key:", StringComparison.OrdinalIgnoreCase))
            {
                string combo = s.Substring(4).Trim();
                ushort[] keys;
                if (!KeyNames.TryParseCombo(combo, out keys, out error)) return false;
                action = new ButtonAction { Kind = ActionKind.Key, Keys = keys, Text = "Key:" + combo };
                return true;
            }

            ActionKind kind;
            if (Enum.TryParse(s, true, out kind) && kind != ActionKind.Key && !IsNumeric(s))
            {
                action = new ButtonAction { Kind = kind, Text = kind.ToString() };
                return true;
            }
            error = "Unknown action '" + s + "'. Use e.g. LeftClick, RightClick, Keyboard, Precision, Toggle or Key:Ctrl+C";
            return false;
        }

        static bool IsNumeric(string s)
        {
            int dummy;
            return int.TryParse(s, out dummy);
        }

        /// <summary>Last non-modifier key in a combo — the one that auto-repeats when held.</summary>
        public int RepeatKey
        {
            get
            {
                if (Kind != ActionKind.Key || Keys.Length == 0) return -1;
                ushort last = Keys[Keys.Length - 1];
                return KeyNames.IsModifier(last) ? -1 : last;
            }
        }

        /// <summary>Short human description for the settings UI.</summary>
        public static string Describe(string text)
        {
            ButtonAction a; string err;
            if (!TryParse(text, out a, out err)) return "Not valid";
            switch (a.Kind)
            {
                case ActionKind.None: return "Does nothing";
                case ActionKind.LeftClick: return "Left mouse button (hold to drag)";
                case ActionKind.RightClick: return "Right mouse button";
                case ActionKind.MiddleClick: return "Middle mouse button";
                case ActionKind.DoubleClick: return "Double-click";
                case ActionKind.MouseBack: return "Mouse back button";
                case ActionKind.MouseForward: return "Mouse forward button";
                case ActionKind.ScrollUp: return "Scroll up one notch (repeats)";
                case ActionKind.ScrollDown: return "Scroll down one notch (repeats)";
                case ActionKind.ScrollLeft: return "Scroll left (repeats)";
                case ActionKind.ScrollRight: return "Scroll right (repeats)";
                case ActionKind.Keyboard: return "Open / close the on-screen keyboard";
                case ActionKind.Precision: return "Slow, precise cursor while held";
                case ActionKind.Toggle: return "Switch PadMouse on / off";
                case ActionKind.NextProfile: return "Switch to the next profile";
                case ActionKind.PrevProfile: return "Switch to the previous profile";
                default: return "Presses " + a.Text.Substring(4).Replace("+", " + ");
            }
        }

        public static readonly string[] Suggestions =
        {
            "None", "LeftClick", "RightClick", "MiddleClick", "DoubleClick", "MouseBack", "MouseForward",
            "ScrollUp", "ScrollDown", "ScrollLeft", "ScrollRight", "Keyboard", "Precision", "Toggle",
            "NextProfile", "PrevProfile",
            "Key:Enter", "Key:Escape", "Key:Backspace", "Key:Space", "Key:Tab", "Key:Delete",
            "Key:Up", "Key:Down", "Key:Left", "Key:Right", "Key:PageUp", "Key:PageDown", "Key:Home", "Key:End",
            "Key:Win", "Key:Win+Tab", "Key:Alt+Tab", "Key:Alt+F4", "Key:Alt+Left", "Key:Alt+Right",
            "Key:Ctrl+C", "Key:Ctrl+V", "Key:Ctrl+X", "Key:Ctrl+Z", "Key:Ctrl+W", "Key:Ctrl+T", "Key:Ctrl+Tab",
            "Key:Ctrl+Shift+Tab", "Key:F", "Key:F5", "Key:F11", "Key:Win+D", "Key:Win+Shift+S",
            "Key:VolumeUp", "Key:VolumeDown", "Key:VolumeMute",
            "Key:MediaPlayPause", "Key:MediaNextTrack", "Key:MediaPreviousTrack",
        };
    }

    public static class KeyNames
    {
        static readonly Dictionary<string, ushort> map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            {"Ctrl",0xA2},{"Control",0xA2},{"Shift",0xA0},{"Alt",0xA4},{"Win",0x5B},{"Windows",0x5B},
            {"RCtrl",0xA3},{"RShift",0xA1},{"RAlt",0xA5},{"AltGr",0xA5},
            {"Esc",0x1B},{"Escape",0x1B},{"Enter",0x0D},{"Return",0x0D},{"Backspace",0x08},{"Bksp",0x08},
            {"Tab",0x09},{"Space",0x20},{"Delete",0x2E},{"Del",0x2E},{"Insert",0x2D},{"Ins",0x2D},
            {"Home",0x24},{"End",0x23},{"PageUp",0x21},{"PgUp",0x21},{"PageDown",0x22},{"PgDn",0x22},
            {"Up",0x26},{"Down",0x28},{"Left",0x25},{"Right",0x27},
            {"CapsLock",0x14},{"PrintScreen",0x2C},{"PrtSc",0x2C},{"Apps",0x5D},{"Menu",0x5D},{"Pause",0x13},
            {"Plus",0xBB},{"Equals",0xBB},{"Minus",0xBD},{"Comma",0xBC},{"Period",0xBE},{"Slash",0xBF},
            {"VolumeUp",0xAF},{"VolumeDown",0xAE},{"VolumeMute",0xAD},{"Mute",0xAD},
            {"MediaPlayPause",0xB3},{"PlayPause",0xB3},{"MediaNextTrack",0xB0},{"NextTrack",0xB0},
            {"MediaPreviousTrack",0xB1},{"PrevTrack",0xB1},{"MediaStop",0xB2},
            {"BrowserBack",0xA6},{"BrowserForward",0xA7},{"BrowserRefresh",0xA8},{"BrowserHome",0xAC},
        };

        public static bool IsModifier(ushort vk)
        {
            return (vk >= 0xA0 && vk <= 0xA5) || vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C;
        }

        public static bool TryParseKey(string name, out ushort vk)
        {
            vk = 0;
            string n = name.Trim();
            if (n.Length == 0) return false;
            if (map.TryGetValue(n, out vk)) return true;
            if (n.Length == 1)
            {
                char c = char.ToUpperInvariant(n[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = c; return true; }
            }
            if ((n[0] == 'F' || n[0] == 'f') && n.Length <= 3)
            {
                int f;
                if (int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 24) { vk = (ushort)(0x70 + f - 1); return true; }
            }
            return false;
        }

        public static bool TryParseCombo(string combo, out ushort[] keys, out string error)
        {
            keys = null; error = null;
            var list = new List<ushort>();
            foreach (string part in combo.Split('+'))
            {
                ushort vk;
                if (!TryParseKey(part, out vk))
                {
                    error = "Unknown key '" + part.Trim() + "' in '" + combo + "'";
                    return false;
                }
                list.Add(vk);
            }
            if (list.Count == 0) { error = "Empty key combo"; return false; }
            keys = list.ToArray();
            return true;
        }
    }
}

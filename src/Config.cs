using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PadMouse
{
    /// <summary>A named button layout. Can switch on automatically when one of its apps is in front.</summary>
    public class Profile
    {
        public string Name = "Desktop";
        public List<string> Apps = new List<string>();   // process names, lower case, no ".exe"
        public StickMode LeftStick = StickMode.Mouse;
        public StickMode RightStick = StickMode.Scroll;
        public Dictionary<PadButton, string> Buttons = new Dictionary<PadButton, string>();

        public Profile Clone()
        {
            var p = (Profile)MemberwiseClone();
            p.Apps = new List<string>(Apps);
            p.Buttons = new Dictionary<PadButton, string>(Buttons);
            return p;
        }

        public string Get(PadButton b)
        {
            string v;
            return Buttons.TryGetValue(b, out v) ? v : "None";
        }

        public bool MatchesApp(string process)
        {
            if (string.IsNullOrEmpty(process)) return false;
            return Apps.Contains(Config.NormaliseApp(process));
        }

        // ---------------------------------------------------------------- built-in profiles

        public static Profile Desktop()
        {
            return new Profile
            {
                Name = "Desktop",
                Buttons = new Dictionary<PadButton, string>
                {
                    {PadButton.A, "LeftClick"}, {PadButton.B, "RightClick"}, {PadButton.X, "MiddleClick"}, {PadButton.Y, "Keyboard"},
                    {PadButton.LB, "Key:Alt+Left"}, {PadButton.RB, "Key:Alt+Right"},
                    {PadButton.LT, "Precision"}, {PadButton.RT, "LeftClick"},
                    {PadButton.Back, "Key:Escape"}, {PadButton.Start, "Key:Win"},
                    {PadButton.L3, "Key:Win+Tab"}, {PadButton.R3, "None"},
                    {PadButton.DPadUp, "Key:Up"}, {PadButton.DPadDown, "Key:Down"},
                    {PadButton.DPadLeft, "Key:Left"}, {PadButton.DPadRight, "Key:Right"},
                }
            };
        }

        public static Profile Browser()
        {
            var p = Desktop();
            p.Name = "Browser";
            p.Apps = new List<string> { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
            p.Buttons[PadButton.LB] = "Key:Ctrl+Shift+Tab";
            p.Buttons[PadButton.RB] = "Key:Ctrl+Tab";
            p.Buttons[PadButton.DPadUp] = "Key:PageUp";
            p.Buttons[PadButton.DPadDown] = "Key:PageDown";
            p.Buttons[PadButton.DPadLeft] = "Key:Alt+Left";
            p.Buttons[PadButton.DPadRight] = "Key:Alt+Right";
            p.Buttons[PadButton.L3] = "Key:F5";
            p.Buttons[PadButton.R3] = "Key:Ctrl+T";
            return p;
        }

        public static Profile Media()
        {
            var p = Desktop();
            p.Name = "Media";
            p.Apps = new List<string> { "vlc", "mpc-hc64", "mpc-be64", "potplayermini64" };
            p.Buttons[PadButton.A] = "Key:MediaPlayPause";
            p.Buttons[PadButton.X] = "Key:F";
            p.Buttons[PadButton.LB] = "Key:Left";
            p.Buttons[PadButton.RB] = "Key:Right";
            p.Buttons[PadButton.DPadUp] = "Key:VolumeUp";
            p.Buttons[PadButton.DPadDown] = "Key:VolumeDown";
            p.Buttons[PadButton.DPadLeft] = "Key:MediaPreviousTrack";
            p.Buttons[PadButton.DPadRight] = "Key:MediaNextTrack";
            p.Buttons[PadButton.R3] = "Key:VolumeMute";
            return p;
        }
    }

    public class Config
    {
        public const int CurrentVersion = 2;

        // Cursor & sticks
        public double CursorSpeed = 1800;      // pixels/second at full deflection
        public double PrecisionFactor = 0.35;  // speed multiplier while Precision is held
        public double Curve = 2.0;             // response curve exponent (1 = linear)
        public double Deadzone = 0.15;         // 0..0.6
        public double ScrollSpeed = 900;       // wheel units/second at full deflection (120 = one notch)
        public bool SmoothScroll = true;       // false = only whole notches (for older apps)
        public bool InvertScroll = false;
        public double TriggerThreshold = 0.35;
        public int KeyRepeatDelay = 400;       // ms before a held key starts repeating
        public int KeyRepeatRate = 40;         // ms between repeats

        // Combos
        public string ToggleCombo = "Back+Start";
        public string NextProfileCombo = "Back+RB";
        public string PrevProfileCombo = "Back+LB";

        // Behaviour
        public bool StartEnabled = true;
        public bool Rumble = true;
        public bool ShowOsd = true;
        public bool BatteryWarnings = true;
        public bool AutoPause = true;
        public List<string> NeverPauseApps = new List<string> { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "vlc", "explorer", "applicationframehost" };
        public List<string> AlwaysPauseApps = new List<string>();
        public bool AutoSwitchProfiles = true;
        public string DefaultProfile = "Desktop";

        // App housekeeping
        public bool CheckForUpdates = true;
        public string LastUpdateCheck = "";
        public string SkippedVersion = "";
        public bool FirstRunDone = false;

        public List<Profile> Profiles = new List<Profile> { Profile.Desktop(), Profile.Browser(), Profile.Media() };

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PadMouse"); }
        }

        public static string FilePath { get { return Path.Combine(Folder, "config.ini"); } }

        public static string NormaliseApp(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith(".exe")) s = s.Substring(0, s.Length - 4);
            return s;
        }

        public static List<string> ParseAppList(string s)
        {
            var list = new List<string>();
            foreach (string part in (s ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = NormaliseApp(part);
                if (n.Length > 0 && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        public static string JoinApps(List<string> apps) { return string.Join(", ", apps.ToArray()); }

        public Profile FindProfile(string name)
        {
            foreach (var p in Profiles) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public int IndexOfProfile(string name)
        {
            for (int i = 0; i < Profiles.Count; i++) if (Profiles[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static bool TryParseCombo(string text, out uint mask, out string error)
        {
            mask = 0; error = null;
            string s = (text ?? "").Trim();
            if (s.Length == 0 || s.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string part in s.Split('+'))
            {
                PadButton b;
                if (!Enum.TryParse(part.Trim(), true, out b) || IsNumeric(part))
                {
                    error = "Unknown button '" + part.Trim() + "' in '" + s + "'. Use names like Back+Start or L3+R3.";
                    return false;
                }
                mask |= 1u << (int)b;
            }
            if (CountBits(mask) < 2) { error = "'" + s + "' needs at least two buttons."; return false; }
            return true;
        }

        static int CountBits(uint v) { int c = 0; while (v != 0) { c += (int)(v & 1); v >>= 1; } return c; }
        static bool IsNumeric(string s) { int d; return int.TryParse(s.Trim(), out d); }

        /// <summary>Checks every value; returns null if OK, otherwise a readable list of problems.</summary>
        public string Validate()
        {
            var sb = new StringBuilder();
            if (Profiles.Count == 0) sb.AppendLine("There must be at least one profile.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Profiles)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) sb.AppendLine("A profile has no name.");
                else if (!names.Add(p.Name)) sb.AppendLine("Two profiles are called '" + p.Name + "'.");
                if (p.Name.IndexOfAny(new[] { '[', ']', '=' }) >= 0) sb.AppendLine("Profile names can't contain [ ] or =.");
                foreach (var kv in p.Buttons)
                {
                    ButtonAction a; string err;
                    if (!ButtonAction.TryParse(kv.Value, out a, out err)) sb.AppendLine(p.Name + " / " + kv.Key + ": " + err);
                }
            }
            uint m1, m2, m3; string e;
            if (!TryParseCombo(ToggleCombo, out m1, out e)) sb.AppendLine("On/off combo: " + e);
            if (!TryParseCombo(NextProfileCombo, out m2, out e)) sb.AppendLine("Next profile combo: " + e);
            if (!TryParseCombo(PrevProfileCombo, out m3, out e)) sb.AppendLine("Previous profile combo: " + e);
            if ((m1 != 0 && (m1 == m2 || m1 == m3)) || (m2 != 0 && m2 == m3)) sb.AppendLine("Two combos use exactly the same buttons.");
            if (FindProfile(DefaultProfile) == null && Profiles.Count > 0) DefaultProfile = Profiles[0].Name;
            return sb.Length == 0 ? null : sb.ToString().Trim();
        }

        public Config Clone()
        {
            var c = (Config)MemberwiseClone();
            c.NeverPauseApps = new List<string>(NeverPauseApps);
            c.AlwaysPauseApps = new List<string>(AlwaysPauseApps);
            c.Profiles = new List<Profile>();
            foreach (var p in Profiles) c.Profiles.Add(p.Clone());
            return c;
        }

        // ---------------------------------------------------------------- persistence

        public static Config Load(out string error)
        {
            error = null;
            var cfg = new Config();
            try
            {
                if (!File.Exists(FilePath)) { cfg.Save(); return cfg; }

                var problems = new StringBuilder();
                var parsed = new List<Profile>();
                Profile current = null;
                Profile legacy = null;             // v1 files: [Buttons] + LeftStick/RightStick in [Settings]
                bool inSettings = false;
                int lineNo = 0;

                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    lineNo++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        current = null; inSettings = false;
                        if (section.Equals("Settings", StringComparison.OrdinalIgnoreCase)) inSettings = true;
                        else if (section.Equals("Buttons", StringComparison.OrdinalIgnoreCase))
                        {
                            if (legacy == null) legacy = new Profile { Name = "Desktop" };
                            current = legacy;
                        }
                        else if (section.StartsWith("Profile:", StringComparison.OrdinalIgnoreCase))
                        {
                            current = new Profile { Name = section.Substring(8).Trim() };
                            parsed.Add(current);
                        }
                        else problems.AppendLine("Line " + lineNo + ": unknown section [" + section + "]");
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    int hash = val.IndexOf(" #", StringComparison.Ordinal);
                    if (hash >= 0) val = val.Substring(0, hash).Trim();

                    string err = null;
                    if (current != null) err = SetProfileValue(current, key, val);
                    else if (inSettings)
                    {
                        // v1 kept stick modes in [Settings]
                        if (key.Equals("LeftStick", StringComparison.OrdinalIgnoreCase) || key.Equals("RightStick", StringComparison.OrdinalIgnoreCase))
                        {
                            if (legacy == null) legacy = new Profile { Name = "Desktop" };
                            err = SetProfileValue(legacy, key, val);
                        }
                        else err = cfg.SetSetting(key, val);
                    }
                    if (err != null) problems.AppendLine("Line " + lineNo + ": " + err);
                }

                if (parsed.Count > 0)
                {
                    cfg.Profiles = parsed;
                }
                else if (legacy != null)
                {
                    // Upgrade a v1 file: keep the user's layout as "Desktop", add the new built-in profiles.
                    var desktop = Profile.Desktop();
                    foreach (var kv in legacy.Buttons) desktop.Buttons[kv.Key] = kv.Value;
                    desktop.LeftStick = legacy.LeftStick;
                    desktop.RightStick = legacy.RightStick;
                    cfg.Profiles = new List<Profile> { desktop, Profile.Browser(), Profile.Media() };
                    cfg.FirstRunDone = true; // they've already been using it
                    try { cfg.Save(); } catch { }
                }

                // Make sure every profile has every button listed.
                foreach (var p in cfg.Profiles)
                    foreach (PadButton b in Enum.GetValues(typeof(PadButton)))
                        if (!p.Buttons.ContainsKey(b)) p.Buttons[b] = "None";

                string v = cfg.Validate();
                if (v != null) problems.AppendLine(v);
                if (problems.Length > 0) error = problems.ToString().Trim();
            }
            catch (Exception ex)
            {
                error = "Could not read " + FilePath + ": " + ex.Message;
            }
            if (cfg.Profiles.Count == 0) cfg.Profiles.Add(Profile.Desktop());
            return cfg;
        }

        static string SetProfileValue(Profile p, string key, string val)
        {
            StickMode sm;
            if (key.Equals("Apps", StringComparison.OrdinalIgnoreCase)) { p.Apps = ParseAppList(val); return null; }
            if (key.Equals("LeftStick", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(val, true, out sm) && !IsNumeric(val)) { p.LeftStick = sm; return null; }
                return "bad value '" + val + "' for LeftStick";
            }
            if (key.Equals("RightStick", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(val, true, out sm) && !IsNumeric(val)) { p.RightStick = sm; return null; }
                return "bad value '" + val + "' for RightStick";
            }
            PadButton b;
            if (!Enum.TryParse(key, true, out b) || IsNumeric(key)) return "unknown button '" + key + "'";
            p.Buttons[b] = val;
            return null;
        }

        string SetSetting(string key, string val)
        {
            var inv = CultureInfo.InvariantCulture;
            double d; int i; bool bl;
            switch (key.ToLowerInvariant())
            {
                case "version": return null;
                case "cursorspeed": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { CursorSpeed = Clamp(d, 50, 10000); return null; } break;
                case "precisionfactor": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { PrecisionFactor = Clamp(d, 0.05, 1); return null; } break;
                case "curve": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { Curve = Clamp(d, 1, 5); return null; } break;
                case "deadzone": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { Deadzone = Clamp(d, 0, 0.6); return null; } break;
                case "scrollspeed": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { ScrollSpeed = Clamp(d, 10, 20000); return null; } break;
                case "triggerthreshold": if (double.TryParse(val, NumberStyles.Float, inv, out d)) { TriggerThreshold = Clamp(d, 0.05, 0.95); return null; } break;
                case "smoothscroll": if (TryBool(val, out bl)) { SmoothScroll = bl; return null; } break;
                case "invertscroll": if (TryBool(val, out bl)) { InvertScroll = bl; return null; } break;
                case "startenabled": if (TryBool(val, out bl)) { StartEnabled = bl; return null; } break;
                case "rumble": if (TryBool(val, out bl)) { Rumble = bl; return null; } break;
                case "showosd": if (TryBool(val, out bl)) { ShowOsd = bl; return null; } break;
                case "batterywarnings": if (TryBool(val, out bl)) { BatteryWarnings = bl; return null; } break;
                case "autopause": if (TryBool(val, out bl)) { AutoPause = bl; return null; } break;
                case "autoswitchprofiles": if (TryBool(val, out bl)) { AutoSwitchProfiles = bl; return null; } break;
                case "checkforupdates": if (TryBool(val, out bl)) { CheckForUpdates = bl; return null; } break;
                case "firstrundone": if (TryBool(val, out bl)) { FirstRunDone = bl; return null; } break;
                case "keyrepeatdelay": if (int.TryParse(val, out i)) { KeyRepeatDelay = Math.Max(50, i); return null; } break;
                case "keyrepeatrate": if (int.TryParse(val, out i)) { KeyRepeatRate = Math.Max(10, i); return null; } break;
                case "togglecombo": ToggleCombo = val; return null;
                case "nextprofilecombo": NextProfileCombo = val; return null;
                case "prevprofilecombo": PrevProfileCombo = val; return null;
                case "neverpauseapps": NeverPauseApps = ParseAppList(val); return null;
                case "alwayspauseapps": AlwaysPauseApps = ParseAppList(val); return null;
                case "defaultprofile": DefaultProfile = val; return null;
                case "lastupdatecheck": LastUpdateCheck = val; return null;
                case "skippedversion": SkippedVersion = val; return null;
                default: return "unknown setting '" + key + "'";
            }
            return "bad value '" + val + "' for " + key;
        }

        static bool TryBool(string s, out bool b)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "true": case "yes": case "on": case "1": b = true; return true;
                case "false": case "no": case "off": case "0": b = false; return true;
            }
            b = false; return false;
        }

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        public void Save()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# PadMouse configuration. Edit here or via tray icon > Settings.");
            sb.AppendLine("# After editing by hand, use tray icon > Reload config.");
            sb.AppendLine();
            sb.AppendLine("[Settings]");
            sb.AppendLine("Version = " + CurrentVersion);
            sb.AppendLine("CursorSpeed = " + CursorSpeed.ToString(inv) + "          # pixels per second at full stick");
            sb.AppendLine("PrecisionFactor = " + PrecisionFactor.ToString(inv) + "     # speed multiplier while Precision is held");
            sb.AppendLine("Curve = " + Curve.ToString(inv) + "                 # 1 = linear, higher = finer control near centre");
            sb.AppendLine("Deadzone = " + Deadzone.ToString(inv) + "            # 0 - 0.6");
            sb.AppendLine("ScrollSpeed = " + ScrollSpeed.ToString(inv) + "          # wheel units per second (120 = one notch)");
            sb.AppendLine("SmoothScroll = " + B(SmoothScroll) + "        # false = whole notches only (older apps)");
            sb.AppendLine("InvertScroll = " + B(InvertScroll));
            sb.AppendLine("TriggerThreshold = " + TriggerThreshold.ToString(inv) + "    # how far LT/RT must be pulled to count as pressed");
            sb.AppendLine("KeyRepeatDelay = " + KeyRepeatDelay + "       # ms before a held key repeats");
            sb.AppendLine("KeyRepeatRate = " + KeyRepeatRate + "         # ms between repeats");
            sb.AppendLine("ToggleCombo = " + ToggleCombo + "     # buttons held together to switch PadMouse on/off");
            sb.AppendLine("NextProfileCombo = " + NextProfileCombo);
            sb.AppendLine("PrevProfileCombo = " + PrevProfileCombo);
            sb.AppendLine("StartEnabled = " + B(StartEnabled));
            sb.AppendLine("Rumble = " + B(Rumble));
            sb.AppendLine("ShowOsd = " + B(ShowOsd) + "             # small pop-up when toggling or switching profile");
            sb.AppendLine("BatteryWarnings = " + B(BatteryWarnings));
            sb.AppendLine("AutoPause = " + B(AutoPause) + "           # pause while a fullscreen game is in front");
            sb.AppendLine("NeverPauseApps = " + JoinApps(NeverPauseApps));
            sb.AppendLine("AlwaysPauseApps = " + JoinApps(AlwaysPauseApps));
            sb.AppendLine("AutoSwitchProfiles = " + B(AutoSwitchProfiles) + "  # use a profile's Apps list to switch automatically");
            sb.AppendLine("DefaultProfile = " + DefaultProfile);
            sb.AppendLine("CheckForUpdates = " + B(CheckForUpdates));
            sb.AppendLine("LastUpdateCheck = " + LastUpdateCheck);
            sb.AppendLine("SkippedVersion = " + SkippedVersion);
            sb.AppendLine("FirstRunDone = " + B(FirstRunDone));
            sb.AppendLine();
            sb.AppendLine("# Actions: None, LeftClick, RightClick, MiddleClick, DoubleClick, MouseBack, MouseForward,");
            sb.AppendLine("#          ScrollUp, ScrollDown, ScrollLeft, ScrollRight, Keyboard (on-screen keyboard),");
            sb.AppendLine("#          Precision (slow cursor while held), Toggle (on/off), NextProfile, PrevProfile,");
            sb.AppendLine("#          Key:<combo> e.g. Key:Ctrl+Shift+Esc");
            sb.AppendLine("# Apps = process names (as shown in Task Manager > Details), comma separated.");
            foreach (var p in Profiles)
            {
                sb.AppendLine();
                sb.AppendLine("[Profile: " + p.Name + "]");
                sb.AppendLine("Apps = " + JoinApps(p.Apps));
                sb.AppendLine("LeftStick = " + p.LeftStick + "     # Mouse | Scroll | None");
                sb.AppendLine("RightStick = " + p.RightStick);
                foreach (PadButton b in Enum.GetValues(typeof(PadButton)))
                    sb.AppendLine(b + " = " + p.Get(b));
            }
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        static string B(bool b) { return b ? "true" : "false"; }
    }
}

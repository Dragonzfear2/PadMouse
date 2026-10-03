using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PadMouse
{
    /// <summary>
    /// Minimal SDL2 bindings for reading non-Xbox controllers (PlayStation, Switch, generic).
    /// SDL2.dll is embedded in PadMouse.exe and extracted to %LocalAppData%\PadMouse on first use.
    /// All SDL calls must happen on the engine thread.
    /// </summary>
    public static class Sdl
    {
        const string Dll = "SDL2.dll";

        public const uint INIT_JOYSTICK = 0x200, INIT_HAPTIC = 0x1000, INIT_GAMECONTROLLER = 0x2000, INIT_EVENTS = 0x4000;

        // SDL_GameControllerAxis
        public const int AXIS_LEFTX = 0, AXIS_LEFTY = 1, AXIS_RIGHTX = 2, AXIS_RIGHTY = 3, AXIS_TRIGGERLEFT = 4, AXIS_TRIGGERRIGHT = 5;

        // SDL_GameControllerButton
        public const int BTN_A = 0, BTN_B = 1, BTN_X = 2, BTN_Y = 3, BTN_BACK = 4, BTN_GUIDE = 5, BTN_START = 6,
            BTN_LEFTSTICK = 7, BTN_RIGHTSTICK = 8, BTN_LEFTSHOULDER = 9, BTN_RIGHTSHOULDER = 10,
            BTN_DPAD_UP = 11, BTN_DPAD_DOWN = 12, BTN_DPAD_LEFT = 13, BTN_DPAD_RIGHT = 14;

        // SDL_GameControllerType
        public const int TYPE_UNKNOWN = 0, TYPE_XBOX360 = 1, TYPE_XBOXONE = 2, TYPE_PS3 = 3, TYPE_PS4 = 4,
            TYPE_SWITCH_PRO = 5, TYPE_VIRTUAL = 6, TYPE_PS5 = 7, TYPE_LUNA = 8, TYPE_STADIA = 9, TYPE_SHIELD = 10,
            TYPE_JOYCON_LEFT = 11, TYPE_JOYCON_RIGHT = 12, TYPE_JOYCON_PAIR = 13;

        // SDL_JoystickPowerLevel
        public const int POWER_UNKNOWN = -1, POWER_EMPTY = 0, POWER_LOW = 1, POWER_MEDIUM = 2, POWER_FULL = 3, POWER_MAX = 4, POWER_WIRED = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct JoystickGUID
        {
            public ulong a, b;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_Init(uint flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetError();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_SetHint(byte[] name, byte[] value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PumpEvents();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FlushEvents(uint min, uint max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickGetDeviceInstanceID(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern JoystickGUID SDL_JoystickGetDeviceGUID(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_JoystickGetGUIDString(JoystickGUID guid, byte[] buf, int size);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerTypeForIndex(int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerClose(IntPtr gc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerGetAttached(IntPtr gc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr gc, int button);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GameControllerGetAxis(IntPtr gc, int axis);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerName(IntPtr gc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerGetType(IntPtr gc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerRumble(IntPtr gc, ushort low, ushort high, uint ms);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerGetJoystick(IntPtr gc);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_GameControllerAddMapping(byte[] mapping);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickOpen(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_JoystickClose(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickGetAttached(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickInstanceID(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumAxes(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumButtons(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumHats(IntPtr joy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_JoystickGetAxis(IntPtr joy, int axis);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetButton(IntPtr joy, int button);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetHat(IntPtr joy, int hat);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickCurrentPowerLevel(IntPtr joy);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibrary(string path);

        public static bool Loaded { get; private set; }
        public static string LoadError { get; private set; }

        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }

        public static string FromUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public static void SetHint(string name, string value) { SDL_SetHint(Utf8(name), Utf8(value)); }
        public static bool AddMapping(string mapping) { return SDL_GameControllerAddMapping(Utf8(mapping)) >= 0; }
        public static string Error { get { return FromUtf8(SDL_GetError()); } }

        public static string GuidString(JoystickGUID g)
        {
            var buf = new byte[64];
            SDL_JoystickGetGUIDString(g, buf, buf.Length);
            int n = Array.IndexOf(buf, (byte)0);
            return Encoding.ASCII.GetString(buf, 0, n < 0 ? buf.Length : n);
        }

        /// <summary>Extracts the embedded SDL2.dll (64-bit only) and loads it. Safe to call more than once.</summary>
        public static bool TryLoad()
        {
            if (Loaded) return true;
            if (LoadError != null) return false;
            try
            {
                if (IntPtr.Size != 8) { LoadError = "SDL needs 64-bit Windows"; return false; }
                var asm = Assembly.GetExecutingAssembly();
                using (var res = asm.GetManifestResourceStream("SDL2.dll"))
                {
                    if (res == null) { LoadError = "SDL2.dll isn't bundled in this build"; return false; }
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadMouse", "sdl-" + res.Length);
                    string path = Path.Combine(dir, "SDL2.dll");
                    if (!File.Exists(path) || new FileInfo(path).Length != res.Length)
                    {
                        Directory.CreateDirectory(dir);
                        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        using (var f = File.Create(tmp)) res.CopyTo(f);
                        try { if (File.Exists(path)) File.Delete(path); File.Move(tmp, path); }
                        catch { try { File.Delete(tmp); } catch { } }
                    }
                    if (LoadLibrary(path) == IntPtr.Zero) { LoadError = "couldn't load SDL2.dll (error " + Marshal.GetLastWin32Error() + ")"; return false; }
                }
                Loaded = true;
                return true;
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                return false;
            }
        }
    }
}

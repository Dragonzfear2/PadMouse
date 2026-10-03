using System;
using System.Runtime.InteropServices;

namespace PadMouse
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    /// <summary>XInput wrapper. Uses xinput1_4 (Windows 8+) and falls back to xinput9_1_0.</summary>
    public static class XInput
    {
        public const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008,
            START = 0x0010, BACK = 0x0020, LEFT_THUMB = 0x0040, RIGHT_THUMB = 0x0080,
            LEFT_SHOULDER = 0x0100, RIGHT_SHOULDER = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState14(uint index, out XInputState state);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        static extern uint SetState14(uint index, ref XInputVibration vib);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint GetState910(uint index, out XInputState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        static extern uint SetState910(uint index, ref XInputVibration vib);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        static extern uint GetBattery14(uint index, byte devType, out XInputBatteryInformation info);

        public const int BATTERY_TYPE_DISCONNECTED = 0, BATTERY_TYPE_WIRED = 1, BATTERY_TYPE_ALKALINE = 2,
            BATTERY_TYPE_NIMH = 3, BATTERY_TYPE_UNKNOWN = 0xFF;
        public const int BATTERY_LEVEL_EMPTY = 0, BATTERY_LEVEL_LOW = 1, BATTERY_LEVEL_MEDIUM = 2, BATTERY_LEVEL_FULL = 3;

        /// <summary>Battery info (xinput1_4 only). Returns false if unavailable.</summary>
        public static bool TryGetBattery(int index, out int type, out int level)
        {
            type = -1; level = -1;
            if (unavailable || useLegacy || index < 0) return false;
            try
            {
                XInputBatteryInformation info;
                if (GetBattery14((uint)index, 0 /*BATTERY_DEVTYPE_GAMEPAD*/, out info) != 0) return false;
                type = info.BatteryType; level = info.BatteryLevel;
                return true;
            }
            catch { return false; }
        }

        static bool useLegacy;
        static bool unavailable;

        public static bool Available { get { return !unavailable; } }

        public static bool TryGetState(int index, out XInputState state)
        {
            state = new XInputState();
            if (unavailable) return false;
            try
            {
                uint r = useLegacy ? GetState910((uint)index, out state) : GetState14((uint)index, out state);
                return r == 0;
            }
            catch (Exception ex)
            {
                if (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                {
                    if (!useLegacy) { useLegacy = true; return TryGetState(index, out state); }
                    unavailable = true;
                }
                return false;
            }
        }

        public static void Vibrate(int index, ushort left, ushort right)
        {
            if (unavailable || index < 0) return;
            var v = new XInputVibration { wLeftMotorSpeed = left, wRightMotorSpeed = right };
            try
            {
                if (useLegacy) SetState910((uint)index, ref v); else SetState14((uint)index, ref v);
            }
            catch { }
        }
    }

    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
        }
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

        /// <summary>Dark title bar on Windows 10 20H1+ / 11.</summary>
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
                int corner = 2; // DWMWCP_ROUND (Windows 11)
                DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
            }
            catch { }
        }

        public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        public static void MakeDpiAware()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static float DpiScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch { }
            return 1f;
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace PadMouse
{
    public enum MouseBtn { Left = 0, Right = 1, Middle = 2, Back = 3, Forward = 4 }

    /// <summary>Synthesises mouse and keyboard input with SendInput.</summary>
    public static class InputSender
    {
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
            MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
            MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100,
            MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_VIRTUALDESK = 0x4000,
            MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
        const uint XBUTTON1 = 1, XBUTTON2 = 2;

        static readonly int InputSize = Marshal.SizeOf(typeof(Win32.INPUT));

        static void Send(Win32.INPUT[] inputs)
        {
            Win32.SendInput((uint)inputs.Length, inputs, InputSize);
        }

        static Win32.INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
        {
            var i = new Win32.INPUT { type = INPUT_MOUSE };
            i.U.mi = new Win32.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags };
            return i;
        }

        /// <summary>
        /// Moves the cursor by a pixel offset using an absolute move, so Windows'
        /// pointer acceleration ("Enhance pointer precision") doesn't distort stick input.
        /// </summary>
        public static void MoveCursorBy(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            Win32.POINT p;
            if (!Win32.GetCursorPos(out p)) return;
            int left = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int top = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int width = Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN));
            int height = Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
            int nx = Math.Max(left, Math.Min(left + width - 1, p.X + dx));
            int ny = Math.Max(top, Math.Min(top + height - 1, p.Y + dy));
            // Normalise to 0..65535 across the virtual desktop; round up so the
            // target maps back onto exactly pixel nx/ny.
            int ax = (int)(((long)(nx - left) * 65536 + width - 1) / width);
            int ay = (int)(((long)(ny - top) * 65536 + height - 1) / height);
            Send(new[] { Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, ax, ay) });
        }

        public static void MouseButton(MouseBtn b, bool down)
        {
            uint flags; uint data = 0;
            switch (b)
            {
                case MouseBtn.Left: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
                case MouseBtn.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                case MouseBtn.Middle: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
                case MouseBtn.Back: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
                default: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
            }
            Send(new[] { Mouse(flags, 0, 0, data) });
        }

        /// <summary>Wheel delta in Windows units (120 = one notch). Positive = up / right.</summary>
        public static void Wheel(int delta, bool horizontal)
        {
            if (delta == 0) return;
            Send(new[] { Mouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta)) });
        }

        static bool IsExtended(ushort vk)
        {
            return (vk >= 0x21 && vk <= 0x28)      // PgUp, PgDn, End, Home, arrows
                || vk == 0x2C || vk == 0x2D || vk == 0x2E // PrintScreen, Insert, Delete
                || vk == 0x5B || vk == 0x5C || vk == 0x5D // LWin, RWin, Apps
                || vk == 0xA3 || vk == 0xA5               // RCtrl, RAlt
                || vk == 0x6F || vk == 0x90               // Numpad divide, NumLock
                || (vk >= 0xA6 && vk <= 0xB7);            // browser / volume / media / launch keys
        }

        static Win32.INPUT KeyInput(ushort vk, bool down)
        {
            uint flags = down ? 0u : KEYEVENTF_KEYUP;
            if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            var i = new Win32.INPUT { type = INPUT_KEYBOARD };
            i.U.ki = new Win32.KEYBDINPUT { wVk = vk, wScan = (ushort)Win32.MapVirtualKey(vk, 0), dwFlags = flags };
            return i;
        }

        public static void Key(ushort vk, bool down)
        {
            Send(new[] { KeyInput(vk, down) });
        }

        public static void TapKey(ushort vk)
        {
            Send(new[] { KeyInput(vk, true), KeyInput(vk, false) });
        }

        /// <summary>Types a character directly, independent of the keyboard layout.</summary>
        public static void TypeChar(char c)
        {
            var down = new Win32.INPUT { type = INPUT_KEYBOARD };
            down.U.ki = new Win32.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE };
            var up = new Win32.INPUT { type = INPUT_KEYBOARD };
            up.U.ki = new Win32.KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP };
            Send(new[] { down, up });
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GlobalTranslator
{
    internal static class NativeMethods
    {
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_HOTKEY = 0x0312;
        internal const int INPUT_KEYBOARD = 1;
        internal const ushort VK_CONTROL = 0x11;
        internal const ushort VK_C = 0x43;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const int HOTKEY_TRANSLATE = 1;
        internal const int HOTKEY_SCREENSHOT = 2;
        internal const int HOTKEY_SETTINGS = 3;

        internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSLLHOOKSTRUCT
        {
            public POINT Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        // INPUT's union is the size of its largest member (MOUSEINPUT).
        // On x64 that is 32 bytes, even when only KEYBDINPUT is used.
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        internal static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            try { SetProcessDPIAware(); }
            catch { }
        }

        internal static uint SendCopy()
        {
            var inputs = new[]
            {
                Key(VK_CONTROL, 0),
                Key(VK_C, 0),
                Key(VK_C, KEYEVENTF_KEYUP),
                Key(VK_CONTROL, KEYEVENTF_KEYUP)
            };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT Key(ushort key, uint flags)
        {
            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = flags }
                }
            };
        }
    }
}

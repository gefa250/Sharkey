using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace GlobalTranslator
{
    internal static class NativeMethods
    {
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_RBUTTONDOWN = 0x0204;
        internal const int WM_MBUTTONDOWN = 0x0207;
        internal const int WM_XBUTTONDOWN = 0x020B;
        internal const int WM_HOTKEY = 0x0312;
        internal const int INPUT_KEYBOARD = 1;
        internal const ushort VK_CONTROL = 0x11;
        internal const ushort VK_C = 0x43;
        internal const ushort VK_ESCAPE = 0x1B;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const int HOTKEY_TRANSLATE = 1;
        internal const int HOTKEY_SCREENSHOT = 2;
        internal const int HOTKEY_SETTINGS = 3;
        internal const int HOTKEY_DISMISS = 4;
        internal const int HOTKEY_WRITING = 5;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOZORDER = 0x0004;

        internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

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

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor", SetLastError = true)]
        private static extern int GetDpiForMonitorNative(
            IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
        private static extern uint GetDpiForWindowNative(IntPtr window);

        internal static uint GetWindowDpi(IntPtr window)
        {
            if (window == IntPtr.Zero) return 96;
            try
            {
                uint dpi = GetDpiForWindowNative(window);
                return dpi == 0 ? 96 : dpi;
            }
            catch (EntryPointNotFoundException) { return 96; }
            catch (DllNotFoundException) { return 96; }
        }

        internal static uint GetMonitorDpi(int x, int y)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(
                    new POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (monitor == IntPtr.Zero) return 0;
                uint dpiX;
                uint dpiY;
                // MDT_EFFECTIVE_DPI = 0.  GetDpiForMonitor is available on
                // Windows 8+ and gives the scaling of the target display even
                // before the popup HWND has moved there.
                if (GetDpiForMonitorNative(monitor, 0, out dpiX, out dpiY) == 0 &&
                    dpiX > 0)
                    return dpiX;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            catch { }
            return 0;
        }

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

namespace GlobalTranslator
{
    // A short-lived, non-blocking mouse hook used only while the translation
    // popup is visible. It never consumes the click.
    internal sealed class PopupDismissMonitor : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly NativeMethods.LowLevelMouseProc _callback;
        private IntPtr _hook;

        public event EventHandler<PopupMouseEventArgs> MouseDown;

        public PopupDismissMonitor(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _callback = MouseHook;
        }

        public bool IsRunning { get { return _hook != IntPtr.Zero; } }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            try
            {
                using (Process process = Process.GetCurrentProcess())
                using (ProcessModule module = process.MainModule)
                    _hook = NativeMethods.SetWindowsHookEx(
                        NativeMethods.WH_MOUSE_LL,
                        _callback,
                        NativeMethods.GetModuleHandle(module.ModuleName),
                        0);
                if (_hook == IntPtr.Zero)
                    DiagnosticLog.Write(
                        "Popup outside-click hook unavailable; error=" +
                        Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write(
                    "Popup outside-click hook failed; type=" +
                    ex.GetType().Name);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && IsMouseDown(wParam))
            {
                NativeMethods.MSLLHOOKSTRUCT data =
                    (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                NativeMethods.POINT point = data.Point;
                try
                {
                    _dispatcher.BeginInvoke(new Action(delegate
                    {
                        var handler = MouseDown;
                        if (handler != null)
                            handler(this, new PopupMouseEventArgs(point.X, point.Y));
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The dispatcher can be shutting down while the native
                    // hook is being removed; the click is no longer relevant.
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        private static bool IsMouseDown(IntPtr message)
        {
            int value = message.ToInt32();
            return value == NativeMethods.WM_LBUTTONDOWN ||
                   value == NativeMethods.WM_RBUTTONDOWN ||
                   value == NativeMethods.WM_MBUTTONDOWN ||
                   value == NativeMethods.WM_XBUTTONDOWN;
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Dispose() { Stop(); }
    }

    internal sealed class PopupMouseEventArgs : EventArgs
    {
        public readonly int X;
        public readonly int Y;

        public PopupMouseEventArgs(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}

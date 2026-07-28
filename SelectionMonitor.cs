using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GlobalTranslator
{
    internal sealed class SelectedTextEventArgs : EventArgs
    {
        public string Text;
        public int X;
        public int Y;
    }

    internal sealed class SelectionMonitor : IDisposable
    {
        private readonly NativeMethods.LowLevelMouseProc _callback;
        private readonly Dispatcher _dispatcher;
        private IntPtr _hook;
        private bool _capturing;
        private int _mouseDownX;
        private int _mouseDownY;
        private DateTime _lastCapture = DateTime.MinValue;

        public event EventHandler<SelectedTextEventArgs> TextSelected;
        public bool Enabled { get; set; }

        public SelectionMonitor(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _callback = MouseHook;
            Enabled = true;
        }

        public void Start()
        {
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
                _hook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL, _callback,
                    NativeMethods.GetModuleHandle(module.ModuleName), 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("无法安装全局鼠标钩子，错误码：" + Marshal.GetLastWin32Error());
        }

        private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                int message = wParam.ToInt32();
                if (message == 0x0201)
                {
                    _mouseDownX = data.Point.X;
                    _mouseDownY = data.Point.Y;
                }
                else if (message == NativeMethods.WM_LBUTTONUP && Enabled && !_capturing)
                {
                    int dx = Math.Abs(data.Point.X - _mouseDownX);
                    int dy = Math.Abs(data.Point.Y - _mouseDownY);
                    if ((dx >= 4 || dy >= 4) && (DateTime.UtcNow - _lastCapture).TotalMilliseconds > 450)
                    {
                        int x = data.Point.X;
                        int y = data.Point.Y;
                        _dispatcher.BeginInvoke(new Action(async delegate { await CaptureAsync(x, y); }));
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        public async Task CaptureAtCursorAsync()
        {
            NativeMethods.POINT point;
            NativeMethods.GetCursorPos(out point);
            await CaptureAsync(point.X, point.Y);
        }

        private async Task CaptureAsync(int x, int y)
        {
            if (_capturing) return;
            DiagnosticLog.Write("Clipboard capture started");
            _capturing = true;
            _lastCapture = DateTime.UtcNow;
            IDataObject previous = null;
            try
            {
                try
                {
                    previous = Clipboard.GetDataObject();
                }
                catch { }

                try { Clipboard.Clear(); }
                catch { }
                uint sentInputs = NativeMethods.SendCopy();
                DiagnosticLog.Write(
                    "Copy keys sent=" + sentInputs + "/4; inputSize=" +
                    Marshal.SizeOf(typeof(NativeMethods.INPUT)));
                string selected = null;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    await Task.Delay(45);
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string candidate = Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(candidate))
                            {
                                selected = candidate.Trim();
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(selected) && selected.Length <= 10000)
                {
                    var handler = TextSelected;
                    if (handler != null) handler(this, new SelectedTextEventArgs { Text = selected, X = x, Y = y });
                }
                else
                {
                    DiagnosticLog.Write("Clipboard capture found no supported text");
                }
            }
            catch
            {
                // Clipboard access can transiently fail when another process owns it.
            }

            if (previous != null)
            {
                await Task.Delay(60);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { Clipboard.SetDataObject(previous, true); break; }
                    catch { System.Threading.Thread.Sleep(30); }
                }
            }
            _capturing = false;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}

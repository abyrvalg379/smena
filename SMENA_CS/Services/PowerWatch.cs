using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SMENA.Services
{
    /// <summary>
    /// Hidden top-level window receiving system power and session notifications.
    /// Block boundaries must land at the sleep/lock moment — not at the next poller
    /// tick. Needs a real top-level window: broadcasts (WM_POWERBROADCAST) do not
    /// reach message-only windows. Every failure is swallowed — the poller's gap
    /// detection (Tick) stays as the fallback for freezes and missed events.
    /// </summary>
    public sealed class PowerWatch : IDisposable
    {
        /// <summary>Message → event kind. Public nested enum: tests pin the constants.</summary>
        public enum Kind { None, Suspended, Resumed, Locked, Unlocked }

        public static Kind Classify(int msg, IntPtr wParam)
        {
            if (msg == WM_POWERBROADCAST)
            {
                switch (wParam.ToInt64())
                {
                    case PBT_APMSUSPEND: return Kind.Suspended;
                    case PBT_APMRESUMEAUTOMATIC: return Kind.Resumed;
                }
            }
            else if (msg == WM_WTSSESSION_CHANGE)
            {
                switch (wParam.ToInt64())
                {
                    case WTS_SESSION_LOCK: return Kind.Locked;
                    case WTS_SESSION_UNLOCK: return Kind.Unlocked;
                }
            }
            return Kind.None;
        }

        public event Action? Suspended;
        public event Action? Resumed;
        public event Action? Locked;
        public event Action? Unlocked;

        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_APMSUSPEND = 0x0004;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int WM_WTSSESSION_CHANGE = 0x02B1;
        private const int WTS_SESSION_LOCK = 0x7;
        private const int WTS_SESSION_UNLOCK = 0x8;
        private const int NOTIFY_FOR_THIS_SESSION = 0;

        private readonly HwndSource? _source;

        public PowerWatch()
        {
            try
            {
                var p = new HwndSourceParameters("SMENA_PowerWatch")
                {
                    PositionX = 0,
                    PositionY = 0,
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0,   // WS_OVERLAPPED, never shown
                };
                _source = new HwndSource(p);
                _source.AddHook(WndProc);
                WTSRegisterSessionNotification(_source.Handle, NOTIFY_FOR_THIS_SESSION);
            }
            catch
            {
                _source = null;   // notifications optional — Tick gap detection covers the rest
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (Classify(msg, wParam))
            {
                case Kind.Suspended: Suspended?.Invoke(); break;
                case Kind.Resumed: Resumed?.Invoke(); break;
                case Kind.Locked: Locked?.Invoke(); break;
                case Kind.Unlocked: Unlocked?.Invoke(); break;
                default: return IntPtr.Zero;
            }
            handled = msg == WM_POWERBROADCAST;   // POWERBROADCAST wants TRUE when processed
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            try
            {
                if (_source != null)
                {
                    WTSUnRegisterSessionNotification(_source.Handle);
                    _source.Dispose();
                }
            }
            catch { /* best effort */ }
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

        [DllImport("wtsapi32.dll")]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
    }
}

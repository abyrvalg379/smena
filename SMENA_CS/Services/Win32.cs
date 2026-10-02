using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SMENA.Services
{
    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        public static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            int len = GetWindowText(hWnd, sb, 512);
            if (len <= 0) return "";
            var title = sb.ToString();
            return title.Length > 200 ? title.Substring(0, 200) : title;
        }

        public static DateTime GetLastInputTime()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return DateTime.Now;
            // dwTime is env tick count (ms since boot); fold into current wall clock by delta.
            uint nowTick = (uint)Environment.TickCount;
            long deltaMs = nowTick >= info.dwTime ? nowTick - info.dwTime : (uint.MaxValue - info.dwTime + nowTick);
            return DateTime.Now.AddMilliseconds(-deltaMs);
        }
    }
}

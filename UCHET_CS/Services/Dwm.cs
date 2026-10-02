using System;
using System.Runtime.InteropServices;

namespace UCHET.Services
{
    /// <summary>Win11 rounded corners for borderless windows (no-op on older systems).</summary>
    public static class Dwm
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                var pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { /* Win10 / dwmapi absent: corners stay square, nothing breaks */ }
        }
    }
}

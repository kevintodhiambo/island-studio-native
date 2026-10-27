using System;
using System.Runtime.InteropServices;

namespace IslandNative
{
    // Hide / restore the Windows taskbar (primary + any secondary-monitor taskbars).
    // Hiding is just ShowWindow(SW_HIDE) on the shell tray windows — reversible and, unlike
    // changing the work area, it can't leave the desktop in a wedged state. Always paired
    // with Show() on exit and in the crash handlers so the taskbar is never left hidden.
    public static class TaskbarManager
    {
        const int SW_HIDE = 0, SW_SHOW = 5;

        public static void Apply(bool hide) { if (hide) Hide(); else Show(); }

        public static void Hide() => ForEachTray(h => ShowWindow(h, SW_HIDE));
        public static void Show() => ForEachTray(h => ShowWindow(h, SW_SHOW));

        static void ForEachTray(Action<IntPtr> act)
        {
            try
            {
                IntPtr primary = FindWindow("Shell_TrayWnd", null);
                if (primary != IntPtr.Zero) act(primary);

                // Secondary-monitor taskbars (there can be several).
                IntPtr sec = IntPtr.Zero;
                while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                    act(sec);
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindow(string? cls, string? name);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    }
}

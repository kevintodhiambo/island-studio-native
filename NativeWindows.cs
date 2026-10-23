using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandNative
{
    public struct WinInfo { public IntPtr Hwnd; public uint Pid; public string Title; }

    // Native window enumeration + activation + icon extraction — the clean replacement
    // for the Electron app's PowerShell P/Invoke helpers.
    public static class NativeWindows
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool alt);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        const uint GW_OWNER = 4;

        public static List<WinInfo> Enumerate()
        {
            var list = new List<WinInfo>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                int len = GetWindowTextLength(h); if (len == 0) return true;
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_TOOLWINDOW) != 0) return true;
                if (GetWindow(h, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return true;
                var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);
                GetWindowThreadProcessId(h, out uint pid);
                list.Add(new WinInfo { Hwnd = h, Pid = pid, Title = sb.ToString() });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string? ProcessPath(uint pid)
        {
            try { using var p = Process.GetProcessById((int)pid); return p.MainModule?.FileName; }
            catch { return null; }
        }

        // Dock click: focus if in background/minimized, minimize if already in front.
        public static void Toggle(IntPtr h)
        {
            if (!IsIconic(h) && GetForegroundWindow() == h) { ShowWindow(h, 6); return; }
            if (IsIconic(h)) ShowWindow(h, 9);
            SwitchToThisWindow(h, true);
            SetForegroundWindow(h);
        }

        public static ImageSource? IconFor(string path)
        {
            try
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
        }

        public static void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }
}

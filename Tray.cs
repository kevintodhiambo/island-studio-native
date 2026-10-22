using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IslandNative
{
    // System-tray presence: a quiet icon by the clock with Start-with-Windows and Exit.
    public class Tray : IDisposable
    {
        readonly NotifyIcon _ni;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "IslandStudio";

        public Tray()
        {
            _ni = new NotifyIcon { Icon = LoadIcon(), Text = "Island Studio", Visible = true };

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Island Studio") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsAutoStart(), CheckOnClick = true };
            startup.CheckedChanged += (s, e) => SetAutoStart(startup.Checked);
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            menu.Items.Add(exit);

            _ni.ContextMenuStrip = menu;
        }

        static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(ExePath()) ?? SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }

        static string ExePath() => Process.GetCurrentProcess().MainModule!.FileName!;

        static bool IsAutoStart()
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(AppName) != null;
        }

        static void SetAutoStart(bool on)
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue(AppName, "\"" + ExePath() + "\"");
            else k.DeleteValue(AppName, false);
        }

        public void Dispose() { _ni.Visible = false; _ni.Dispose(); }
    }
}

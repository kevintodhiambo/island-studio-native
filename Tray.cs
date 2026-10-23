using System;
using System.Drawing;
using System.Windows.Forms;

namespace IslandNative
{
    // System-tray presence: a quiet icon by the clock with Settings, Start-with-Windows and Exit.
    public class Tray : IDisposable
    {
        readonly NotifyIcon _ni;
        SettingsWindow? _settings;
        ToolStripMenuItem _startup = null!;

        public Tray()
        {
            _ni = new NotifyIcon { Icon = LoadIcon(), Text = "Island Studio", Visible = true };
            _ni.DoubleClick += (s, e) => OpenSettings();

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Island Studio") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var settings = new ToolStripMenuItem("Settings…");
            settings.Click += (s, e) => OpenSettings();
            menu.Items.Add(settings);

            _startup = new ToolStripMenuItem("Start with Windows") { Checked = AutoStart.Enabled, CheckOnClick = true };
            _startup.CheckedChanged += (s, e) => AutoStart.Set(_startup.Checked);
            menu.Items.Add(_startup);

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            menu.Items.Add(exit);

            menu.Opening += (s, e) => _startup.Checked = AutoStart.Enabled;
            _ni.ContextMenuStrip = menu;
        }

        void OpenSettings()
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (s, e) => _settings = null;
                _settings.Show();
            }
            else { _settings.Activate(); }
        }

        static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }

        public void Dispose() { _ni.Visible = false; _ni.Dispose(); }
    }
}

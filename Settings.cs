using System;
using System.IO;
using System.Text.Json;

namespace IslandNative
{
    public class SettingsData
    {
        public bool IdleEyes { get; set; } = true;
        public bool DockEnabled { get; set; } = true;
        public double DockIconSize { get; set; } = 44;
        public bool DockMagnify { get; set; } = true;
    }

    // App settings, persisted to %APPDATA%\IslandStudio\settings.json. Windows subscribe
    // to Changed and re-read the live values when it fires.
    public static class Settings
    {
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandStudio");
        static string FilePath => Path.Combine(Dir, "settings.json");

        public static SettingsData Data { get; private set; } = Load();
        public static event Action? Changed;

        static SettingsData Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath)) ?? new SettingsData(); }
            catch { }
            return new SettingsData();
        }

        public static void Save()
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
            Changed?.Invoke();
        }
    }

    // Start-with-Windows via the HKCU Run key (shared by the tray and settings).
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "IslandStudio";
        static string ExePath() => System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;

        public static bool Enabled
        {
            get { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(AppName) != null; }
        }

        public static void Set(bool on)
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue(AppName, "\"" + ExePath() + "\"");
            else k.DeleteValue(AppName, false);
        }
    }
}

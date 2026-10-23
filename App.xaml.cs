using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log(a.ExceptionObject as Exception);

            if (Environment.GetEnvironmentVariable("ISLAND_DIAG") != null) { RunDiag(); Shutdown(); return; }

            base.OnStartup(e);
        }

        // Verify the volume/brightness native interop (the control-center backends).
        static void RunDiag()
        {
            var sb = new StringBuilder();
            try
            {
                var vol = new VolumeService();
                sb.AppendLine($"volume available: {vol.Available}");
                float v0 = vol.Get(); sb.AppendLine($"volume now: {v0:0.00}");
                vol.Set(0.42f); sb.AppendLine($"after Set(0.42): {vol.Get():0.00}");
                vol.Set(v0); sb.AppendLine($"restored to: {vol.Get():0.00}");
                sb.AppendLine($"mute: {vol.GetMute()}");

                var br = new BrightnessService();
                sb.AppendLine($"brightness supported: {br.Supported}");
                int b0 = br.Get(); sb.AppendLine($"brightness now: {b0}");
                if (b0 >= 0) { br.Set(b0); sb.AppendLine("brightness Set() ran without error"); }
            }
            catch (Exception ex) { sb.AppendLine("DIAG ERROR: " + ex); }
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "islandstudio-diag.txt"), sb.ToString()); } catch { }
        }

        static void Log(Exception? ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "islandstudio-crash.log"), DateTime.Now + "\n" + ex + "\n\n"); }
            catch { }
        }
    }
}

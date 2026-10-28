using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class App : Application
    {
        // Held for the process lifetime so only one Island Studio ever runs.
        static Mutex? _single;
        const string MutexName = @"Local\IslandStudio.SingleInstance.v1";

        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => { Log(a.ExceptionObject as Exception); try { TaskbarManager.Show(); } catch { } };
            // Never leave the taskbar hidden if the process is ending for any reason.
            AppDomain.CurrentDomain.ProcessExit += (s, a) => { try { TaskbarManager.Show(); } catch { } };

            if (Environment.GetEnvironmentVariable("ISLAND_DIAG") != null) { RunDiag(); Shutdown(); return; }

            // Headless install (verification hook): run the installer the same way the GUI
            // does — on a background thread — log the result, exit.
            if (Environment.GetEnvironmentVariable("ISLAND_INSTALL") != null)
            {
                // Run on a background thread like the GUI installer does, to exercise the same path.
                var (ok, msg) = System.Threading.Tasks.Task.Run(() => Installer.Install(false)).GetAwaiter().GetResult();
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "islandstudio-install.txt"),
                    $"ok={ok}\n{msg}\nfrom: {AppContext.BaseDirectory}\ninstalled at: {Installer.InstallPath}\n"); } catch { }
                Shutdown(); return;
            }

            // Offscreen capture harness: build the notch, render PNGs, exit. No install/mutex.
            if (Environment.GetEnvironmentVariable("ISLAND_SHOT_DIR") != null) { new MainWindow().Show(); return; }

            // Not running from the install location → this exe is an installer/updater.
            if (!Installer.IsInstalled) { new InstallWindow().Show(); return; }

            // Installed copy: enforce a single instance.
            if (!AcquireSingleInstance()) { Shutdown(); return; }

            new MainWindow().Show();
        }

        // true if we got the lock; false if another instance already holds it.
        public static bool AcquireSingleInstance()
        {
            try
            {
                _single = new Mutex(true, MutexName, out bool createdNew);
                return createdNew;
            }
            catch { return true; } // if the mutex can't be created, don't block startup
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

                var w = System.Threading.Tasks.Task.Run(() => WeatherService.Fetch()).GetAwaiter().GetResult();
                sb.AppendLine($"weather: {(w == null ? "null" : $"{w.Temp}° {w.Label} {w.City}")}");
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

using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Keep running even if the (hidden) main window is ever closed; exit is via the tray.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log(a.ExceptionObject as Exception);
            base.OnStartup(e);
        }

        static void Log(Exception? ex)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "islandstudio-crash.log");
                File.AppendAllText(path, DateTime.Now + "\n" + ex + "\n\n");
            }
            catch { }
        }
    }
}

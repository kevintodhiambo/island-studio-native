using System;
using System.Diagnostics;
using System.IO;

namespace IslandNative
{
    // Install / update / clean-up so only one clean copy of Island Studio ever exists.
    //
    // "Install" copies this exe to a fixed per-user location, closes every running copy
    // (however it was named or wherever it was launched from), points Start-with-Windows
    // at the installed copy, and launches it. Re-running a downloaded exe is therefore
    // also the update path: it replaces the installed copy and relaunches.
    public static class Installer
    {
        public const string ProductName = "Island Studio";
        // Must match the published apphost filename (AssemblyName in the csproj).
        public const string ExeName = "IslandStudio.exe";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs", "Island Studio");
        public static string InstallPath => Path.Combine(InstallDir, ExeName);

        public static string CurrentExe
        {
            get { try { return Process.GetCurrentProcess().MainModule!.FileName!; } catch { return ""; } }
        }

        // We're the installed copy when we're running from the canonical path.
        public static bool IsInstalled
        {
            get
            {
                try { return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstallPath), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }

        // Kill every OTHER Island Studio process, identified by the exe's ProductName so it
        // works no matter what the file was renamed to. Returns how many were closed.
        public static int CloseOtherInstances()
        {
            int closed = 0;
            int me = -1;
            try { me = Process.GetCurrentProcess().Id; } catch { }
            foreach (var p in Process.GetProcesses())
            {
                if (p.Id == me) continue;
                try
                {
                    string? file = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(file)) continue;
                    var info = FileVersionInfo.GetVersionInfo(file);
                    if (!string.Equals(info.ProductName, ProductName, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    p.WaitForExit(4000);
                    closed++;
                }
                catch { /* access denied / exited / not ours */ }
                finally { try { p.Dispose(); } catch { } }
            }
            return closed;
        }

        // Full install: stop other copies, copy here → install dir, set autostart, launch it.
        // Returns (ok, message). Throws nothing.
        public static (bool ok, string message) Install(bool startWithWindows)
        {
            try
            {
                int closed = CloseOtherInstances();

                string src = CurrentExe;
                if (string.IsNullOrEmpty(src) || !File.Exists(src))
                    return (false, "Could not locate the running program to copy.");

                string srcDir = Path.GetDirectoryName(src)!;
                Directory.CreateDirectory(InstallDir);

                // Copy the whole application folder (a folder-based build has the runtime
                // DLLs next to the exe; a single-file build is just the one exe). Retry
                // briefly in case the OS is still releasing a just-closed copy.
                for (int attempt = 0; ; attempt++)
                {
                    try { CopyDir(srcDir, InstallDir); break; }
                    catch (IOException) when (attempt < 10) { System.Threading.Thread.Sleep(200); }
                }

                SetAutoStart(startWithWindows);

                Process.Start(new ProcessStartInfo(InstallPath) { UseShellExecute = true, WorkingDirectory = InstallDir });
                return (true, closed > 0 ? $"Installed. Closed {closed} old copy(ies)." : "Installed.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // Recursively copy a directory's contents, overwriting existing files.
        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from))
                CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // Point the HKCU Run key at the installed exe (or remove it).
        public static void SetAutoStart(bool on)
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(run, true);
            if (k == null) return;
            if (on) k.SetValue("IslandStudio", "\"" + InstallPath + "\"");
            else k.DeleteValue("IslandStudio", false);
        }
    }
}

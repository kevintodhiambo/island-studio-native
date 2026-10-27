using System;
using System.Diagnostics;
using System.IO;

namespace IslandNative
{
    // Install / update / clean-up so only one clean copy of Island Studio ever exists.
    //
    // "Install" copies this exe's folder to a chosen location, closes every running copy
    // (however it was named or wherever it was launched from), points Start-with-Windows
    // at the installed copy, and launches it. Re-running a downloaded exe is therefore
    // also the update path: it replaces the installed copy and relaunches.
    public static class Installer
    {
        public const string ProductName = "Island Studio";
        // Must match the published apphost filename (AssemblyName in the csproj).
        public const string ExeName = "IslandStudio.exe";
        const string RegKey = @"Software\IslandStudio";

        public static string DefaultDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs", "Island Studio");

        // The folder a prior install chose (remembered), else the default.
        static string? StoredDir
        {
            get { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKey); return k?.GetValue("InstallDir") as string; } catch { return null; } }
        }

        public static string InstallDir => StoredDir ?? DefaultDir;
        public static string InstallPath => Path.Combine(InstallDir, ExeName);

        public static string CurrentExe
        {
            get { try { return Process.GetCurrentProcess().MainModule!.FileName!; } catch { return ""; } }
        }

        // We're the installed copy when we're running from the remembered install folder.
        public static bool IsInstalled
        {
            get
            {
                try
                {
                    string? dir = Path.GetDirectoryName(CurrentExe);
                    return dir != null && string.Equals(Path.GetFullPath(dir), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase);
                }
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

        // Full install: stop other copies, copy the app folder → targetDir (default if null),
        // remember the location, set autostart, launch it. progress reports 0..1 as files copy.
        public static (bool ok, string message) Install(bool startWithWindows, string? targetDir = null, Action<double>? progress = null)
        {
            try
            {
                string dest = string.IsNullOrWhiteSpace(targetDir) ? DefaultDir : targetDir!;

                int closed = CloseOtherInstances();

                string src = CurrentExe;
                if (string.IsNullOrEmpty(src) || !File.Exists(src))
                    return (false, "Could not locate the running program to copy.");
                string srcDir = Path.GetDirectoryName(src)!;

                if (string.Equals(Path.GetFullPath(srcDir), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return (false, "That's already the install folder — pick another, or just run it.");

                Directory.CreateDirectory(dest);

                int total = CountFiles(srcDir), done = 0;
                CopyDir(srcDir, dest, () => { done++; progress?.Invoke(total > 0 ? (double)done / total : 1); });

                try { using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKey); k?.SetValue("InstallDir", dest); } catch { }

                string destExe = Path.Combine(dest, ExeName);
                SetAutoStart(startWithWindows, destExe);

                Process.Start(new ProcessStartInfo(destExe) { UseShellExecute = true, WorkingDirectory = dest });
                return (true, closed > 0 ? $"Installed. Closed {closed} old copy(ies)." : "Installed.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        static int CountFiles(string dir)
        {
            int n = 0;
            try { n += Directory.GetFiles(dir).Length; foreach (var d in Directory.GetDirectories(dir)) n += CountFiles(d); } catch { }
            return n;
        }

        // Recursively copy a directory's contents, overwriting existing files.
        static void CopyDir(string from, string to, Action? onFile = null)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) { CopyFileResilient(f, Path.Combine(to, Path.GetFileName(f))); onFile?.Invoke(); }
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)), onFile);
        }

        // Copy one file, tolerating the locks antivirus real-time scanning puts on each DLL
        // as it lands. When File.Copy throws because the scanner grabbed the handle, the
        // bytes have usually already been written — so if the destination is fully there
        // (same length), accept it instead of fighting to overwrite a file being scanned.
        static void CopyFileResilient(string src, string dest)
        {
            long srcLen = -1;
            try { srcLen = new FileInfo(src).Length; } catch { }
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(src, dest, true); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 30)
                {
                    try { if (srcLen >= 0 && File.Exists(dest) && new FileInfo(dest).Length == srcLen) return; } catch { }
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        // Point the HKCU Run key at the installed exe (or remove it).
        public static void SetAutoStart(bool on, string? exePath = null)
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(run, true);
            if (k == null) return;
            if (on) k.SetValue("IslandStudio", "\"" + (exePath ?? InstallPath) + "\"");
            else k.DeleteValue("IslandStudio", false);
        }
    }
}

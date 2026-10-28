using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace IslandNative
{
    public partial class InstallWindow : Window
    {
        bool _handed;   // true once MainWindow has taken over the app lifetime
        string _targetDir = Installer.DefaultDir;

        public InstallWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => DarkTitleBar();
            // Closing the installer without installing/running should quit, not leave a zombie
            // (ShutdownMode is OnExplicitShutdown, so no window ≠ exit).
            Closed += (s, e) => { if (!_handed) Application.Current.Shutdown(); };

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            SubText.Text = $"Version {v?.Major}.{v?.Minor}.{v?.Build}";
            ShowDest();

            BtnInstall.Click += async (s, e) => await DoInstall();
            BtnChange.Click += (s, e) => ChangeLocation();
            BtnRunOnce.Click += (s, e) => RunOnce();
        }

        void ShowDest() => DestText.Text = "Installs to:  " + _targetDir;

        void ChangeLocation()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose where to install Island Studio",
                UseDescriptionForTitle = true,
                SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                // Install into an "Island Studio" folder inside the chosen location.
                _targetDir = System.IO.Path.Combine(dlg.SelectedPath, "Island Studio");
                ShowDest();
            }
        }

        async Task DoInstall()
        {
            BtnInstall.IsEnabled = false;
            BtnRunOnce.IsEnabled = false;
            BtnChange.IsEnabled = false;
            CbStartup.IsEnabled = false;
            Prog.Value = 0;
            Prog.Visibility = Visibility.Visible;
            Status.Text = "Installing…";

            bool startup = CbStartup.IsChecked == true;
            string dir = _targetDir;
            var progress = new Progress<double>(p => Prog.Value = p);   // marshals to the UI thread
            var (ok, msg) = await Task.Run(() => Installer.Install(startup, dir, p => ((IProgress<double>)progress).Report(p)));

            if (ok)
            {
                Prog.Value = 1;
                Status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x32, 0xD7, 0x4B));
                Status.Text = "✓ Installed — Island Studio is now running at the top of your screen.";
                _handed = true;   // the freshly installed copy is launching and will take over
                await Task.Delay(1800);
                Application.Current.Shutdown();
            }
            else
            {
                Prog.Visibility = Visibility.Collapsed;
                Status.Text = "Couldn't install: " + msg;
                BtnInstall.IsEnabled = true;
                BtnRunOnce.IsEnabled = true;
                BtnChange.IsEnabled = true;
                CbStartup.IsEnabled = true;
            }
        }

        void RunOnce()
        {
            // Close other copies so this trial run is the only one, then run in place.
            try { Installer.CloseOtherInstances(); } catch { }
            App.AcquireSingleInstance();
            _handed = true;
            new MainWindow().Show();
            Close();
        }

        // Offscreen render to PNG (layout check while the screen is locked).
        public void RenderForCapture(string path)
        {
            var root = (FrameworkElement)Content;
            root.Measure(new System.Windows.Size(Width, Height));
            root.Arrange(new Rect(0, 0, Width, Height));
            root.UpdateLayout();
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1E)), null, new Rect(0, 0, Width, Height));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root) { Stretch = System.Windows.Media.Stretch.None }, null, new Rect(0, 0, Width, Height));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }

        void DarkTitleBar()
        {
            try { var h = new WindowInteropHelper(this).Handle; int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4); }
            catch { }
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    }
}

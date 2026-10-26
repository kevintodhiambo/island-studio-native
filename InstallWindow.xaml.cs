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

        public InstallWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => DarkTitleBar();
            // Closing the installer without installing/running should quit, not leave a zombie
            // (ShutdownMode is OnExplicitShutdown, so no window ≠ exit).
            Closed += (s, e) => { if (!_handed) Application.Current.Shutdown(); };

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            SubText.Text = $"Version {v?.Major}.{v?.Minor}.{v?.Build}";
            DestText.Text = "Installs to:  " + Installer.InstallPath;

            BtnInstall.Click += async (s, e) => await DoInstall();
            BtnRunOnce.Click += (s, e) => RunOnce();
        }

        async Task DoInstall()
        {
            BtnInstall.IsEnabled = false;
            BtnRunOnce.IsEnabled = false;
            CbStartup.IsEnabled = false;
            Status.Text = "Installing…";

            bool startup = CbStartup.IsChecked == true;
            var (ok, msg) = await Task.Run(() => Installer.Install(startup));

            if (ok)
            {
                Status.Text = "Done. Opening Island Studio…";
                // The freshly installed copy is launching and will take over; close the installer.
                _handed = true;
                await Task.Delay(600);
                Application.Current.Shutdown();
            }
            else
            {
                Status.Text = "Couldn't install: " + msg;
                BtnInstall.IsEnabled = true;
                BtnRunOnce.IsEnabled = true;
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

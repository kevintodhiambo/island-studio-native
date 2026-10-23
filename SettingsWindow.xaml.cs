using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class SettingsWindow : Window
    {
        bool _loading = true;
        readonly DispatcherTimer _sizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => DarkTitleBar();

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            AboutVersion.Text = $"Version {v?.Major}.{v?.Minor}.{v?.Build}";

            LoadValues();

            TgStartup.Checked += (s, e) => { if (!_loading) AutoStart.Set(true); };
            TgStartup.Unchecked += (s, e) => { if (!_loading) AutoStart.Set(false); };
            TgEyes.Click += (s, e) => Apply(() => Settings.Data.IdleEyes = TgEyes.IsChecked == true);
            TgMinimal.Click += (s, e) => Apply(() => Settings.Data.MinimalNotch = TgMinimal.IsChecked == true);
            TgDock.Click += (s, e) => Apply(() => Settings.Data.DockEnabled = TgDock.IsChecked == true);
            TgMagnify.Click += (s, e) => Apply(() => Settings.Data.DockMagnify = TgMagnify.IsChecked == true);

            SlDockSize.ValueChanged += (s, e) =>
            {
                if (_loading) return;
                Settings.Data.DockIconSize = Math.Round(SlDockSize.Value);
                _sizeDebounce.Stop(); _sizeDebounce.Start();
            };
            _sizeDebounce.Tick += (s, e) => { _sizeDebounce.Stop(); Settings.Save(); };
        }

        void LoadValues()
        {
            _loading = true;
            TgStartup.IsChecked = AutoStart.Enabled;
            TgEyes.IsChecked = Settings.Data.IdleEyes;
            TgMinimal.IsChecked = Settings.Data.MinimalNotch;
            TgDock.IsChecked = Settings.Data.DockEnabled;
            TgMagnify.IsChecked = Settings.Data.DockMagnify;
            SlDockSize.Value = Settings.Data.DockIconSize;
            _loading = false;
        }

        void Apply(Action set) { if (_loading) return; set(); Settings.Save(); }

        // Render the window content to PNG (offscreen verification while the screen is locked).
        public void RenderForCapture(string path)
        {
            var root = (FrameworkElement)Content;
            root.Measure(new Size(Width, Height));
            root.Arrange(new Rect(0, 0, Width, Height));
            root.UpdateLayout();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)), null, new Rect(0, 0, Width, Height));
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None }, null, new Rect(0, 0, Width, Height));
            }
            var rtb = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
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

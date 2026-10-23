using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class MainWindow : Window
    {
        const double CompactW = 230, CompactH = 38;
        const double ExpandedW = 460, ExpandedH = 142;
        const double Shoulder = 16, Bottom = 20;

        double _t, _tTarget, _lastTarget = -1;
        DateTime _lastActive = DateTime.Now;
        const double IdleSeconds = 15;

        readonly Random _rng = new();
        int _blinkCooldown, _glanceCooldown;

        MediaService _media = null!;
        MediaInfo _mi = new();
        Tray? _tray;
        DockWindow? _dock;

        Rectangle[] _eqBars = null!;
        readonly double[] _eqP = { .3, .6, .4, .7 }, _eqV = new double[4], _eqT = { .3, .6, .4, .7 };
        int _eqBeat;

        IntPtr _hwnd;

        public MainWindow()
        {
            InitializeComponent();
            _eqBars = new[] { Eq0, Eq1, Eq2, Eq3 };

            Loaded += (s, e) =>
            {
                PositionTop();
                ApplyShape();
                UpdateClock();
                UpdateBattery();

                _media = new MediaService(Dispatcher);
                _media.Changed += OnMedia;
                _media.Start();

                // Offscreen render capture for verification while the screen is locked.
                var shotDir = Environment.GetEnvironmentVariable("ISLAND_SHOT_DIR");
                if (!string.IsNullOrEmpty(shotDir))
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RunCapture(shotDir)));
                    return;
                }
                _tray = new Tray();
                _dock = new DockWindow();
                _dock.Show();
            };
            Closed += (s, e) => { _tray?.Dispose(); _dock?.Close(); };
            SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; SetBaseStyles(); SetClickThrough(true); };

            BtnPrev.Click += (s, e) => _media?.Prev();
            BtnPlay.Click += (s, e) => _media?.TogglePlay();
            BtnNext.Click += (s, e) => _media?.Next();

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) => { UpdateClock(); UpdateBattery(); };
            clock.Start();

            var loop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            loop.Tick += (s, e) => Loop();
            loop.Start();
        }

        void PositionTop()
        {
            Left = Environment.GetEnvironmentVariable("ISLAND_LEFT") is string lv && double.TryParse(lv, out var lx)
                ? lx : (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
        }

        void OnMedia(MediaInfo mi)
        {
            _mi = mi;
            MTitle.Text = mi.Title;
            MArtist.Text = mi.Artist;
            MArt.Source = mi.Art;
            CArt.Source = mi.Art;
            BtnPlay.Content = mi.Playing ? "" : ""; // pause / play
            if (mi.Active) _lastActive = DateTime.Now;
        }

        // ---------- main loop ----------
        void Loop()
        {
            // Step aside for full-screen apps (games, videos, slideshows).
            bool fs = ForegroundIsFullscreen();
            Notch.Visibility = fs ? Visibility.Hidden : Visibility.Visible;
            if (fs) { if (_lastTarget != 0) { SetClickThrough(true); _lastTarget = 0; } _t = 0; return; }

            GetCursorPos(out var p);
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double cx = p.X / scale, cy = p.Y / scale;

            double nw = Notch.Width, nh = Notch.Height;
            double nl = Left + (Width - nw) / 2, nt = Top;
            const double margin = 24;
            bool inside = cx >= nl - margin && cx <= nl + nw + margin && cy >= nt && cy <= nt + nh + margin;

            if (inside) _lastActive = DateTime.Now;
            _tTarget = inside ? 1 : 0;
            if (_tTarget != _lastTarget) { SetClickThrough(_tTarget == 0); _lastTarget = _tTarget; }

            double prev = _t;
            _t += (_tTarget - _t) * 0.28;
            if (Math.Abs(_t - _tTarget) < 0.0015) _t = _tTarget;

            bool idle = _t < 0.02 && !(_mi.Active && _mi.Playing) && (DateTime.Now - _lastActive).TotalSeconds > IdleSeconds;
            if (Math.Abs(_t - prev) > 0.00005) ApplyShape();
            ApplyLayers(idle);
            if (idle) { Blink(); Glance(); }
            else if (EyesT.X != 0) EyesT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));

            if (_mi.Active && _mi.Playing) StepEq();
            UpdateProgress();
        }

        void ApplyShape()
        {
            double w = CompactW + (ExpandedW - CompactW) * _t;
            double h = CompactH + (ExpandedH - CompactH) * _t;
            Notch.Width = w;
            Notch.Height = h;
            NotchBg.Data = BuildNotch(w, h, Shoulder, Bottom);
        }

        void ApplyLayers(bool idle)
        {
            double compOp = Clamp01(1 - _t * 1.7);
            double expOp = Clamp01((_t - 0.3) * 1.7);
            bool media = _mi.Active;
            bool compHalf = _t < 0.6;

            bool showEyes = idle;
            bool showCompMedia = !showEyes && compHalf && media && _mi.Playing;
            bool showCompClock = !showEyes && !showCompMedia && compHalf;

            Eyes.Visibility = showEyes ? Visibility.Visible : Visibility.Collapsed;
            CompactMedia.Visibility = showCompMedia ? Visibility.Visible : Visibility.Collapsed;
            CompactClock.Visibility = showCompClock ? Visibility.Visible : Visibility.Collapsed;
            CompactMedia.Opacity = compOp; CompactClock.Opacity = compOp;

            ExpMedia.Visibility = (_t > 0.05 && media) ? Visibility.Visible : Visibility.Collapsed;
            ExpInfo.Visibility = (_t > 0.05 && !media) ? Visibility.Visible : Visibility.Collapsed;
            ExpMedia.Opacity = expOp; ExpInfo.Opacity = expOp;
        }

        // One filled outline: flush top, concave shoulders flaring to the edge, rounded bottom.
        static Geometry BuildNotch(double w, double h, double rs, double rb)
        {
            rb = Math.Min(rb, h - rs);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(w, 0), true, false);
                c.ArcTo(new Point(w - rs, rs), new Size(rs, rs), 0, false, SweepDirection.Counterclockwise, true, false);
                c.LineTo(new Point(w - rs, h - rb), true, false);
                c.ArcTo(new Point(w - rs - rb, h), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(rs + rb, h), true, false);
                c.ArcTo(new Point(rs, h - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(rs, rs), true, false);
                c.ArcTo(new Point(0, 0), new Size(rs, rs), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        // ---------- equalizer (spring toward re-randomizing beat targets) ----------
        void StepEq()
        {
            if (++_eqBeat >= 4) // ~every 130ms
            {
                _eqBeat = 0;
                for (int i = 0; i < 4; i++) _eqT[i] = 0.25 + _rng.NextDouble() * 0.75;
            }
            for (int i = 0; i < 4; i++)
            {
                _eqV[i] = (_eqV[i] + (_eqT[i] - _eqP[i]) * 0.28) * 0.72;
                _eqP[i] = Math.Max(0.15, Math.Min(1, _eqP[i] + _eqV[i]));
                _eqBars[i].Height = 3 + _eqP[i] * 12;
            }
        }

        void UpdateProgress()
        {
            if (!_mi.Active || _mi.Duration <= 0) { MFill.Width = 0; return; }
            double pos = _mi.Position + (_mi.Playing ? (DateTime.Now - _mi.Stamp).TotalSeconds : 0);
            double ratio = Clamp01(pos / _mi.Duration);
            double avail = ((FrameworkElement)MFill.Parent).ActualWidth;
            if (avail > 0) MFill.Width = ratio * avail;
        }

        // ---------- eyes ----------
        void Blink()
        {
            if (_blinkCooldown-- > 0) return;
            if (_rng.NextDouble() < 0.07)
            {
                var a = new DoubleAnimation(0.1, TimeSpan.FromMilliseconds(80)) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
                EyeLS.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                EyeRS.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                _blinkCooldown = 40;
            }
        }

        void Glance()
        {
            if (_glanceCooldown-- > 0) return;
            if (_rng.NextDouble() < 0.05)
            {
                double dx = (_rng.Next(3) - 1) * 4;
                EyesT.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(dx, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                _glanceCooldown = 60;
            }
        }

        // ---------- content ----------
        void UpdateClock()
        {
            var now = DateTime.Now;
            CTime.Text = now.ToString("h:mm");
            ETime.Text = now.ToString("h:mm");
            EDate.Text = now.ToString("dddd, MMMM d");
        }

        void UpdateBattery()
        {
            if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128) { CBatt.Visibility = Visibility.Collapsed; return; }
            CBatt.Visibility = Visibility.Visible;
            int pct = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 100;
            bool charging = s.ACLineStatus == 1;
            bool low = !charging && pct <= 20;

            CBattPct.Text = pct + "%";
            EBatt.Text = (charging ? "⚡ " : "") + pct + "%";
            CBattFill.Width = Math.Max(2, 16 * pct / 100.0);
            CBattFill.Background = new SolidColorBrush(
                charging ? Color.FromRgb(0x32, 0xD7, 0x4D) :
                low ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xAF, 0xAF, 0xB4));
            CBattPct.Foreground = new SolidColorBrush(low ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xCF, 0xCF, 0xD4));
            CBoltIcon.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
            CPlugIcon.Visibility = low ? Visibility.Visible : Visibility.Collapsed;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ---------- offscreen capture (verify without the screen) ----------
        void RunCapture(string dir)
        {
            Directory.CreateDirectory(dir);
            // compact clock
            _mi = new MediaInfo(); ApplyLayers(false);
            _t = 0; ApplyShape(); Shot(System.IO.Path.Combine(dir, "compact.png"));
            // expanded info
            _t = 1; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "info.png"));
            // expanded media (fake now-playing so the player renders)
            OnMedia(new MediaInfo { Active = true, Playing = true, Title = "Paper Sky", Artist = "Lumen Coast · Summer Flows", Position = 71, Duration = 193 });
            ApplyShape(); ApplyLayers(false); UpdateProgress(); Shot(System.IO.Path.Combine(dir, "media.png"));
            // compact now-playing
            _t = 0; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "compact-media.png"));
            // idle eyes
            _mi = new MediaInfo(); _t = 0; ApplyShape(); ApplyLayers(true); Shot(System.IO.Path.Combine(dir, "idle.png"));
            // dock bar
            try { new DockWindow().RenderBarForCapture(System.IO.Path.Combine(dir, "dock.png")); } catch { }
            Application.Current.Shutdown();
        }

        void Shot(string path)
        {
            UpdateLayout();
            Notch.UpdateLayout();
            double w = Notch.Width, h = Notch.Height;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2e)), null, new Rect(0, 0, w + 48, h + 40));
                dc.DrawRectangle(new VisualBrush(Notch) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(24, 0, w, h));
            }
            var rtb = new RenderTargetBitmap((int)(w + 48), (int)(h + 40), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        // ---------- interop ----------
        void SetBaseStyles()
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        void SetClickThrough(bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            ex = on ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();

        bool ForegroundIsFullscreen()
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
            if (!GetWindowRect(fg, out var r)) return false;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pw = (int)(SystemParameters.PrimaryScreenWidth * scale);
            int ph = (int)(SystemParameters.PrimaryScreenHeight * scale);
            return r.Left <= 0 && r.Top <= 0 && r.Right >= pw - 1 && r.Bottom >= ph - 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved1; public int BatteryLifeTime; public int BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class MainWindow : Window
    {
        const double CompactW = 230, CompactH = 38;
        const double ExpandedW = 440, ExpandedH = 130;
        const double Shoulder = 16, Bottom = 20;

        double _t;          // 0 = compact, 1 = expanded (smoothed)
        double _tTarget;
        DateTime _lastActive = DateTime.Now;
        const double IdleSeconds = 15;

        readonly Random _rng = new();
        int _blinkCooldown, _glanceCooldown;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                PositionTop();
                ApplyShape();
                UpdateClock();
                UpdateBattery();
            };
            SourceInitialized += (s, e) => MakeClickThrough();

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

        // ---------- main loop: hover, animation, eyes ----------
        void Loop()
        {
            GetCursorPos(out var p);
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double cx = p.X / scale, cy = p.Y / scale;

            double nw = Notch.Width, nh = Notch.Height;
            double nl = Left + (Width - nw) / 2, nt = Top;
            const double margin = 24;
            bool inside = cx >= nl - margin && cx <= nl + nw + margin && cy >= nt && cy <= nt + nh + margin;

            if (inside) _lastActive = DateTime.Now;
            _tTarget = inside ? 1 : 0;

            // Smooth toward target (exponential ease-out), rebuild shape only when it moves.
            double prev = _t;
            _t += (_tTarget - _t) * 0.28;
            if (Math.Abs(_t - _tTarget) < 0.0015) _t = _tTarget;
            if (Math.Abs(_t - prev) > 0.00005) ApplyShape();

            bool idle = _t < 0.02 && (DateTime.Now - _lastActive).TotalSeconds > IdleSeconds;
            Eyes.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            Compact.Visibility = (!idle && _t < 0.5) ? Visibility.Visible : Visibility.Collapsed;
            if (idle) { Blink(); Glance(); }
            else if (EyesT.X != 0) EyesT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        }

        void ApplyShape()
        {
            double w = CompactW + (ExpandedW - CompactW) * _t;
            double h = CompactH + (ExpandedH - CompactH) * _t;
            Notch.Width = w;
            Notch.Height = h;
            NotchBg.Data = BuildNotch(w, h, Shoulder, Bottom);
            Compact.Opacity = Clamp01(1 - _t * 1.7);
            Expanded.Opacity = Clamp01((_t - 0.3) * 1.7);
        }

        // One filled outline: flush top, concave shoulders flaring to the edge, rounded bottom.
        static Geometry BuildNotch(double w, double h, double rs, double rb)
        {
            rb = Math.Min(rb, (h - rs) / 1.0);
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

        // ---------- interop ----------
        void MakeClickThrough()
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte Reserved1; public int BatteryLifeTime; public int BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    }
}

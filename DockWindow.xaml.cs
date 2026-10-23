using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IslandNative
{
    public partial class DockWindow : Window
    {
        const double Base = 44, Max = 66;
        double Influence => Base * 2.6;

        class Item
        {
            public string Name = "", Path = "";
            public Image Img = null!;
            public Ellipse Dot = null!;
            public IntPtr Hwnd;
        }

        readonly List<Item> _items = new();
        IntPtr _hwnd;
        DateTime _lastRun = DateTime.MinValue;

        public DockWindow()
        {
            InitializeComponent();
            BuildItems();

            SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; SetBaseStyles(); SetClickThrough(true); };
            Loaded += (s, e) =>
            {
                Position();
                RefreshRunning();
                var loop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                loop.Tick += (a, b) => Loop();
                loop.Start();
            };
        }

        void BuildItems()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys = Environment.SystemDirectory;
            var seed = new (string name, string path)[]
            {
                ("File Explorer", System.IO.Path.Combine(win, "explorer.exe")),
                ("Notepad",       System.IO.Path.Combine(sys, "notepad.exe")),
                ("Paint",         System.IO.Path.Combine(sys, "mspaint.exe")),
                ("Calculator",    System.IO.Path.Combine(sys, "calc.exe")),
                ("Task Manager",  System.IO.Path.Combine(sys, "Taskmgr.exe")),
            };

            foreach (var (name, path) in seed)
            {
                if (!File.Exists(path)) continue;
                var img = new Image { Source = NativeWindows.IconFor(path), Width = Base, Height = Base };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black };
                var dot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White, Opacity = 0, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

                var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = name };
                stack.Children.Add(img);
                stack.Children.Add(dot);

                var it = new Item { Name = name, Path = path, Img = img, Dot = dot };
                stack.MouseLeftButtonUp += (s, e) => OnClick(it);
                Items.Children.Add(stack);
                _items.Add(it);
            }
        }

        void OnClick(Item it)
        {
            if (it.Hwnd != IntPtr.Zero) NativeWindows.Toggle(it.Hwnd);
            else NativeWindows.Launch(it.Path);
        }

        void Position()
        {
            var wa = SystemParameters.WorkArea; // excludes the taskbar
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height;
        }

        // ---------- loop: magnify + click-through ----------
        void Loop()
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            GetCursorPos(out var p);
            double cx = p.X / scale, cy = p.Y / scale;

            foreach (var it in _items)
            {
                double icx;
                try { icx = it.Img.PointToScreen(new Point(it.Img.ActualWidth / 2, 0)).X / scale; }
                catch { icx = -9999; }
                double d = Math.Abs(cx - icx);
                double s = 1;
                if (d < Influence) { double f = Math.Cos(d / Influence * Math.PI / 2); s = 1 + (Max / Base - 1) * f * f; }
                double size = Base * s;
                it.Img.Width = size; it.Img.Height = size;
            }

            // Clickable only while the cursor is over the bar; otherwise click-through.
            bool over = false;
            try
            {
                var tl = Bar.PointToScreen(new Point(0, 0));
                double bx = tl.X / scale, by = tl.Y / scale;
                over = cx >= bx && cx <= bx + Bar.ActualWidth && cy >= by && cy <= by + Bar.ActualHeight;
            }
            catch { }
            SetClickThrough(!over);

            if ((DateTime.Now - _lastRun).TotalSeconds > 1.5) { _lastRun = DateTime.Now; RefreshRunning(); }
        }

        void RefreshRunning()
        {
            var map = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in NativeWindows.Enumerate())
            {
                var path = NativeWindows.ProcessPath(w.Pid);
                if (path == null) continue;
                if (!map.ContainsKey(path)) map[path] = w.Hwnd;
            }
            foreach (var it in _items)
            {
                it.Hwnd = map.TryGetValue(it.Path, out var h) ? h : IntPtr.Zero;
                it.Dot.Opacity = it.Hwnd != IntPtr.Zero ? 1 : 0;
            }
        }

        // ---------- capture (verify offscreen) ----------
        public void RenderBarForCapture(string path)
        {
            foreach (var it in _items) it.Dot.Opacity = it.Name == "File Explorer" || it.Name == "Notepad" ? 1 : 0;
            Bar.Measure(new Size(2000, 2000));
            Bar.Arrange(new Rect(new Point(0, 0), Bar.DesiredSize));
            Bar.UpdateLayout();
            double w = Bar.ActualWidth, h = Bar.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2b, 0x2e, 0x36)), null, new Rect(0, 0, w + 40, h + 40));
                dc.DrawRectangle(new VisualBrush(Bar) { Stretch = Stretch.None }, null, new Rect(20, 20, w, h));
            }
            var rtb = new RenderTargetBitmap((int)(w + 40), (int)(h + 40), 96, 96, PixelFormats.Pbgra32);
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
            int want = on ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            if (want != ex) SetWindowLong(_hwnd, GWL_EXSTYLE, want);
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    }
}

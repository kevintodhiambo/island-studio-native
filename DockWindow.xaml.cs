using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    public class DockAppDef { public string Name { get; set; } = ""; public string Path { get; set; } = ""; }

    public partial class DockWindow : Window
    {
        double Base => Settings.Data.DockIconSize;
        double Max => Base * 1.5;
        double Influence => Base * 2.6;

        class Item
        {
            public DockAppDef Def = null!;
            public StackPanel Stack = null!;
            public Image Img = null!;
            public Ellipse Dot = null!;
            public IntPtr Hwnd;
        }

        readonly List<Item> _items = new();
        List<DockAppDef> _apps = new();
        IntPtr _hwnd;
        DateTime _lastRun = DateTime.MinValue;

        // drag-to-reorder
        Item? _drag;
        double _dragStartX;
        bool _dragMoved;

        public DockWindow()
        {
            InitializeComponent();
            _apps = DockConfig.Load();
            Rebuild();

            SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; SetBaseStyles(); SetClickThrough(true); };
            Loaded += (s, e) =>
            {
                Position();
                RefreshRunning();
                var loop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                loop.Tick += (a, b) => Loop();
                loop.Start();
            };
            Settings.Changed += OnSettingsChanged;
            Closed += (s, e) => Settings.Changed -= OnSettingsChanged;
        }

        void OnSettingsChanged() => Dispatcher.BeginInvoke(() =>
        {
            _apps = DockConfig.Load();   // pick up an import done from Settings
            Rebuild(); Position(); RefreshRunning();
        });

        void Rebuild()
        {
            Items.Children.Clear();
            _items.Clear();
            foreach (var def in _apps) AddItem(def);
            AddPlusTile();
            AddGearTile();
        }

        void AddItem(DockAppDef def)
        {
            if (!File.Exists(def.Path)) return;
            var img = new Image { Source = NativeWindows.IconFor(def.Path), Width = Base, Height = Base };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            img.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black };
            var dot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White, Opacity = 0, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

            var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = def.Name, Background = Brushes.Transparent };
            stack.Children.Add(img);
            stack.Children.Add(dot);

            var it = new Item { Def = def, Stack = stack, Img = img, Dot = dot };

            // right-click → remove
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Remove from dock" };
            remove.Click += (s, e) => { _apps.Remove(def); DockConfig.Save(_apps); Rebuild(); };
            menu.Items.Add(remove);
            stack.ContextMenu = menu;

            // drag to reorder, or click to launch/toggle
            stack.MouseLeftButtonDown += (s, e) => { _drag = it; _dragStartX = e.GetPosition(this).X; _dragMoved = false; stack.CaptureMouse(); e.Handled = true; };
            stack.MouseMove += (s, e) =>
            {
                if (_drag != it || e.LeftButton != MouseButtonState.Pressed) return;
                double dx = e.GetPosition(this).X - _dragStartX;
                if (Math.Abs(dx) > 6) _dragMoved = true;
                if (_dragMoved) { stack.RenderTransform = new TranslateTransform(dx, 0); System.Windows.Controls.Panel.SetZIndex(stack, 10); }
            };
            stack.MouseLeftButtonUp += (s, e) =>
            {
                if (_drag != it) return;
                stack.ReleaseMouseCapture();
                if (_dragMoved) ReorderTo(it, e.GetPosition(this).X);
                else OnClick(it);
                stack.RenderTransform = null;
                _drag = null;
            };

            Items.Children.Add(stack);
            _items.Add(it);
        }

        void AddPlusTile()
        {
            var plus = new TextBlock { Text = "+", FontSize = 26, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCE)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var border = new Border { Width = Base, Height = Base, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Child = plus };
            var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = "Add an app" };
            stack.Children.Add(border);
            stack.Children.Add(new Ellipse { Width = 4, Height = 4, Opacity = 0, Margin = new Thickness(0, 4, 0, 0) });
            stack.MouseLeftButtonUp += (s, e) => AddApp();
            Items.Children.Add(stack);
        }

        void AddGearTile()
        {
            var gear = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons"), FontSize = 20, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCE)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var border = new Border { Width = Base, Height = Base, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Child = gear };
            var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = "Island Studio settings" };
            stack.Children.Add(border);
            stack.Children.Add(new Ellipse { Width = 4, Height = 4, Opacity = 0, Margin = new Thickness(0, 4, 0, 0) });
            stack.MouseLeftButtonUp += (s, e) => OpenSettings();
            Items.Children.Add(stack);
        }

        static void OpenSettings()
        {
            foreach (Window w in Application.Current.Windows) if (w is SettingsWindow sw) { sw.Activate(); return; }
            new SettingsWindow().Show();
        }

        void AddApp()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Add an app to the dock", Filter = "Programs (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dlg.ShowDialog() == true)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                _apps.Add(new DockAppDef { Name = name, Path = dlg.FileName });
                DockConfig.Save(_apps);
                Rebuild();
                RefreshRunning();
            }
        }

        void ReorderTo(Item it, double cursorX)
        {
            int target = _items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == it) continue;
                double center;
                try { center = _items[i].Stack.TransformToAncestor(this).Transform(new Point(_items[i].Stack.ActualWidth / 2, 0)).X; }
                catch { continue; }
                if (cursorX < center) { target = i; break; }
            }
            int from = _apps.IndexOf(it.Def);
            if (from < 0) return;
            if (target > from) target--;
            target = Math.Max(0, Math.Min(_apps.Count - 1, target));
            _apps.RemoveAt(from);
            _apps.Insert(target, it.Def);
            DockConfig.Save(_apps);
            Rebuild();
            RefreshRunning();
        }

        void OnClick(Item it)
        {
            if (it.Hwnd != IntPtr.Zero) NativeWindows.Toggle(it.Hwnd);
            else NativeWindows.Launch(it.Def.Path);
        }

        void Position()
        {
            var wa = SystemParameters.WorkArea;
            // When the taskbar is hidden, the work area still reserves its strip, so sit on
            // the true screen bottom to use the reclaimed space.
            double bottom = Settings.Data.HideTaskbar ? SystemParameters.PrimaryScreenHeight : wa.Bottom;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = bottom - Height;
        }

        // ---------- loop: magnify + click-through ----------
        void Loop()
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            GetCursorPos(out var p);
            double cx = p.X / scale, cy = p.Y / scale;

            // Dock bar rect (DIPs). Needed both to gate magnification vertically and to
            // decide click-through.
            double bx = -1, by = -1, bw = 0, bh = 0;
            try { var tl = Bar.PointToScreen(new Point(0, 0)); bx = tl.X / scale; by = tl.Y / scale; bw = Bar.ActualWidth; bh = Bar.ActualHeight; }
            catch { }

            // Only magnify when the cursor is actually near the dock — not anywhere along
            // the same column higher up the screen (e.g. over the notch). Magnification
            // begins as the cursor comes within Influence above the bar.
            bool nearDock = by >= 0 && cy >= by - Influence && cy <= by + bh + 8;

            if (_drag == null || !_dragMoved)
            {
                foreach (var it in _items)
                {
                    double s = 1;
                    if (nearDock && Settings.Data.DockMagnify)
                    {
                        double icx;
                        try { icx = it.Img.PointToScreen(new Point(it.Img.ActualWidth / 2, 0)).X / scale; }
                        catch { icx = -9999; }
                        double d = Math.Abs(cx - icx);
                        if (d < Influence) { double f = Math.Cos(d / Influence * Math.PI / 2); s = 1 + (Max / Base - 1) * f * f; }
                    }
                    it.Img.Width = Base * s; it.Img.Height = Base * s;
                }
            }

            bool over = by >= 0 && cx >= bx && cx <= bx + bw && cy >= by && cy <= by + bh;
            SetClickThrough(!over && _drag == null);

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
                it.Hwnd = map.TryGetValue(it.Def.Path, out var h) ? h : IntPtr.Zero;
                it.Dot.Opacity = it.Hwnd != IntPtr.Zero ? 1 : 0;
            }
        }

        // ---------- capture ----------
        public void RenderBarForCapture(string path)
        {
            for (int i = 0; i < _items.Count; i++) _items[i].Dot.Opacity = i < 2 ? 1 : 0;
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

    // Dock app list persisted to %APPDATA%\IslandStudio\dock.json
    public static class DockConfig
    {
        static string Dir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandStudio");
        static string FilePath => System.IO.Path.Combine(Dir, "dock.json");

        public static List<DockAppDef> Load()
        {
            try { if (File.Exists(FilePath)) { var l = JsonSerializer.Deserialize<List<DockAppDef>>(File.ReadAllText(FilePath)); if (l is { Count: > 0 }) return l; } }
            catch { }
            var def = Seed();
            Save(def);
            return def;
        }

        public static void Save(List<DockAppDef> apps)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        static List<DockAppDef> Seed()
        {
            // Prefer the user's own pinned taskbar apps; fall back to a sensible default set.
            var pins = TaskbarPins.Resolve();
            if (pins.Count > 0) return pins;

            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys = Environment.SystemDirectory;
            var cand = new (string name, string path)[]
            {
                ("File Explorer", System.IO.Path.Combine(win, "explorer.exe")),
                ("Notepad",       System.IO.Path.Combine(sys, "notepad.exe")),
                ("Paint",         System.IO.Path.Combine(sys, "mspaint.exe")),
                ("Calculator",    System.IO.Path.Combine(sys, "calc.exe")),
                ("Task Manager",  System.IO.Path.Combine(sys, "Taskmgr.exe")),
            };
            var list = new List<DockAppDef>();
            foreach (var (name, path) in cand) if (File.Exists(path)) list.Add(new DockAppDef { Name = name, Path = path });
            return list;
        }
    }

    // Reads the apps the user pinned to the Windows taskbar (their .lnk shortcuts) and
    // resolves each to its target exe, so the dock can mirror the taskbar.
    public static class TaskbarPins
    {
        public static List<DockAppDef> Resolve()
        {
            var list = new List<DockAppDef>();
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (!Directory.Exists(dir)) return list;

                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return list;
                dynamic shell = Activator.CreateInstance(t)!;
                foreach (var lnk in Directory.GetFiles(dir, "*.lnk"))
                {
                    try
                    {
                        dynamic sc = shell.CreateShortcut(lnk);
                        string target = (string)sc.TargetPath;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(sc);
                        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) continue;
                        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        bool dup = false;
                        foreach (var a in list) if (string.Equals(a.Path, target, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                        if (dup) continue;
                        list.Add(new DockAppDef { Name = System.IO.Path.GetFileNameWithoutExtension(lnk), Path = target });
                    }
                    catch { }
                }
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); } catch { }
            }
            catch { }
            return list;
        }

        // Merge the user's pinned taskbar apps into an existing dock list (no duplicates).
        // Returns how many new apps were added.
        public static int ImportInto(List<DockAppDef> apps)
        {
            int added = 0;
            foreach (var p in Resolve())
            {
                bool dup = false;
                foreach (var a in apps) if (string.Equals(a.Path, p.Path, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) { apps.Add(p); added++; }
            }
            return added;
        }
    }
}

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
        const double MinW = 110, MinH = 6; // thin resting line in minimal-notch mode
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
        WaterWallpaper? _wall;

        VolumeService _vol = null!;
        BrightnessService _bri = null!;
        string _page = "info";
        bool _syncing;
        string _lastDotsKey = "";

        List<LyricLine>? _lyrics;
        string _lyricsKey = "";
        bool _lyricsOn;
        int _lyIdx = -2;

        WeatherInfo? _weather;

        int _tmTotal = 25 * 60, _tmLeft = 25 * 60;
        bool _tmRunning;
        WaterData _water = new();

        List<TodoItem> _todos = new();
        List<string> _shelf = new();
        Point _shelfDragStart;
        bool _addTaskOpen;

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

                _vol = new VolumeService();
                _bri = new BrightnessService();
                InitControlCenter();
                UpdateTimerUi();
                UpdateWaterUi();

                FetchWeather();
                var wx = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
                wx.Tick += (s2, e2) => FetchWeather();
                wx.Start();

                // Offscreen render capture for verification while the screen is locked.
                var shotDir = Environment.GetEnvironmentVariable("ISLAND_SHOT_DIR");
                if (!string.IsNullOrEmpty(shotDir))
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RunCapture(shotDir)));
                    return;
                }
                _tray = new Tray();
                if (Settings.Data.DockEnabled) { _dock = new DockWindow(); _dock.Show(); }
                if (Settings.Data.WaterWallpaper) StartWallpaper();
                Settings.Changed += OnSettingsChanged;
            };
            Closed += (s, e) => { _tray?.Dispose(); _dock?.Close(); _wall?.Close(); Settings.Changed -= OnSettingsChanged; };
            SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; SetBaseStyles(); SetClickThrough(true); };

            BtnPrev.Click += (s, e) => _media?.Prev();
            BtnPlay.Click += (s, e) => _media?.TogglePlay();
            BtnNext.Click += (s, e) => _media?.Next();
            BtnLyrics.Click += (s, e) => { _lyricsOn = true; _lyIdx = -2; ApplyLayers(false); UpdateLyrics(); };
            BtnLyricsBack.Click += (s, e) => { _lyricsOn = false; ApplyLayers(false); };

            // Focus timer
            TmP5.Click += (s, e) => SetTimer(5);
            TmP15.Click += (s, e) => SetTimer(15);
            TmP25.Click += (s, e) => SetTimer(25);
            TmStart.Click += (s, e) => { if (_tmLeft <= 0) _tmLeft = _tmTotal; _tmRunning = !_tmRunning; UpdateTimerUi(); };
            TmReset.Click += (s, e) => { _tmLeft = _tmTotal; _tmRunning = false; UpdateTimerUi(); };

            // Water reminder
            _water = WaterStore.Today();
            WtDrink.Click += (s, e) => { _water.Amount += 250; WaterStore.Save(_water); UpdateWaterUi(); };
            WtUndo.Click += (s, e) => { _water.Amount = Math.Max(0, _water.Amount - 250); WaterStore.Save(_water); UpdateWaterUi(); };

            // To-do
            _todos = TodoStore.Load();
            TdAdd.Click += (s, e) => OpenAddTask();
            BuildTodoList();

            // File shelf
            _shelf = ShelfStore.Load();
            ExpShelf.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            ExpShelf.Drop += OnShelfDrop;
            BuildShelfList();

            // Scroll the expanded notch to switch pages (media / info / control center).
            PreviewMouseWheel += (s, e) =>
            {
                if (_t < 0.5) return;
                var pg = Pages();
                int i = pg.IndexOf(_page); if (i < 0) i = 0;
                i = (i + (e.Delta < 0 ? 1 : -1) + pg.Count) % pg.Count;
                _page = pg[i];
                ApplyLayers(false);
            };

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) => { UpdateClock(); UpdateBattery(); PollControlCenter(); TickTimer(); };
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

            // New track → reset and fetch synced lyrics from LRCLIB.
            string key = mi.Title + "|" + mi.Artist;
            if (key != _lyricsKey)
            {
                _lyricsKey = key;
                _lyrics = null; _lyricsOn = false; _lyIdx = -2;
                BtnLyrics.Visibility = Visibility.Collapsed;
                if (mi.Active)
                {
                    string k = key;
                    LyricsService.Fetch(mi.Title, mi.Artist, mi.Duration).ContinueWith(t =>
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_lyricsKey != k) return; // track changed while fetching
                            _lyrics = t.Result;
                            BtnLyrics.Visibility = (_lyrics is { Count: > 0 }) ? Visibility.Visible : Visibility.Collapsed;
                        }));
                }
            }
        }

        void UpdateLyrics()
        {
            if (!_lyricsOn || _lyrics is not { Count: > 0 }) return;
            double pos = _mi.Position + (_mi.Playing ? (DateTime.Now - _mi.Stamp).TotalSeconds : 0);
            int idx = -1;
            for (int i = 0; i < _lyrics.Count; i++) { if (_lyrics[i].Time <= pos + 0.12) idx = i; else break; }
            if (idx == _lyIdx) return;
            _lyIdx = idx;
            LyCur.Text = idx >= 0 ? _lyrics[idx].Text : "♪";
            LyPrev.Text = idx > 0 ? _lyrics[idx - 1].Text : "";
            LyNext.Text = (idx + 1 < _lyrics.Count) ? _lyrics[idx + 1].Text : "";
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
            if (_tTarget != _lastTarget)
            {
                SetClickThrough(_tTarget == 0);
                if (_tTarget == 1) _page = _mi.Active ? "media" : "info";
                _lastTarget = _tTarget;
            }

            double prev = _t;
            _t += (_tTarget - _t) * 0.28;
            if (Math.Abs(_t - _tTarget) < 0.0015) _t = _tTarget;

            bool idle = Settings.Data.IdleEyes && _t < 0.02 && !(_mi.Active && _mi.Playing) && (DateTime.Now - _lastActive).TotalSeconds > IdleSeconds;
            if (Math.Abs(_t - prev) > 0.00005) ApplyShape();
            ApplyLayers(idle);
            if (idle) { Blink(); Glance(); }
            else if (EyesT.X != 0) EyesT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));

            if (_mi.Active && _mi.Playing) StepEq();
            UpdateProgress();
            if (_lyricsOn && _t > 0.3) UpdateLyrics();
        }

        void ApplyShape()
        {
            double baseW = Settings.Data.MinimalNotch ? MinW : CompactW;
            double baseH = Settings.Data.MinimalNotch ? MinH : CompactH;
            double w = baseW + (ExpandedW - baseW) * _t;
            double h = baseH + (ExpandedH - baseH) * _t;
            Notch.Width = w;
            Notch.Height = h;
            double rs = Math.Min(Shoulder, h * 0.42);
            double rb = Math.Min(Bottom, h - rs); if (rb < 0) rb = 0;
            NotchBg.Data = BuildNotch(w, h, rs, rb);
        }

        void ApplyLayers(bool idle)
        {
            double compOp = Clamp01(1 - _t * 1.7);
            double expOp = Clamp01((_t - 0.3) * 1.7);
            bool media = _mi.Active;
            bool compHalf = _t < 0.6;

            // Minimal mode: the resting notch is just a thin line, no content.
            bool minRest = Settings.Data.MinimalNotch && _t < 0.5;
            bool showEyes = idle && !minRest;
            bool showCompMedia = !minRest && !showEyes && compHalf && media && _mi.Playing;
            bool showCompTimer = !minRest && !showEyes && !showCompMedia && compHalf && _tmRunning;
            bool showCompClock = !minRest && !showEyes && !showCompMedia && !showCompTimer && compHalf;

            Eyes.Visibility = showEyes ? Visibility.Visible : Visibility.Collapsed;
            CompactMedia.Visibility = showCompMedia ? Visibility.Visible : Visibility.Collapsed;
            CompactTimer.Visibility = showCompTimer ? Visibility.Visible : Visibility.Collapsed;
            CompactClock.Visibility = showCompClock ? Visibility.Visible : Visibility.Collapsed;
            CompactMedia.Opacity = CompactTimer.Opacity = CompactClock.Opacity = compOp;

            // Expanded pages, chosen by _page (scroll to switch).
            var pages = Pages();
            if (!pages.Contains(_page)) _page = "info";
            bool exp = _t > 0.05;
            bool mediaPage = exp && _page == "media";
            ExpMedia.Visibility = mediaPage ? Visibility.Visible : Visibility.Collapsed;
            ExpInfo.Visibility = (exp && _page == "info") ? Visibility.Visible : Visibility.Collapsed;
            ExpCC.Visibility = (exp && _page == "cc") ? Visibility.Visible : Visibility.Collapsed;
            ExpTimer.Visibility = (exp && _page == "timer") ? Visibility.Visible : Visibility.Collapsed;
            ExpWater.Visibility = (exp && _page == "water") ? Visibility.Visible : Visibility.Collapsed;
            ExpTodo.Visibility = (exp && _page == "todo") ? Visibility.Visible : Visibility.Collapsed;
            ExpShelf.Visibility = (exp && _page == "shelf") ? Visibility.Visible : Visibility.Collapsed;
            ExpMedia.Opacity = ExpInfo.Opacity = ExpCC.Opacity = ExpTimer.Opacity = ExpWater.Opacity = ExpTodo.Opacity = ExpShelf.Opacity = expOp;
            if (exp && _page == "water") UpdateWaterUi();
            if (mediaPage)
            {
                PlayerView.Visibility = _lyricsOn ? Visibility.Collapsed : Visibility.Visible;
                LyricsView.Visibility = _lyricsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateDots(pages, exp ? expOp : 0);
        }

        List<string> Pages()
        {
            var l = new List<string>();
            if (_mi.Active) l.Add("media");
            l.Add("info");
            l.Add("cc");
            l.Add("timer");
            l.Add("water");
            l.Add("todo");
            l.Add("shelf");
            return l;
        }

        void UpdateDots(List<string> pages, double op)
        {
            Dots.Opacity = op;
            string key = string.Join(",", pages) + "|" + _page;
            if (key == _lastDotsKey) return;
            _lastDotsKey = key;
            Dots.Children.Clear();
            foreach (var pg in pages)
            {
                Dots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 5, Height = 5, Margin = new Thickness(3, 0, 3, 0),
                    Fill = new SolidColorBrush(pg == _page ? Color.FromRgb(0xE6, 0xE6, 0xEA) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
                });
            }
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

        // ---------- control center ----------
        void InitControlCenter()
        {
            if (!_bri.Supported) BriRow.Visibility = Visibility.Collapsed;
            _syncing = true;
            SlVol.Value = _vol.Get() * 100;
            if (_bri.Supported) { int b = _bri.Get(); if (b >= 0) SlBri.Value = b; }
            _syncing = false;
            UpdateVolIcon();
            VolPct.Text = $"{(int)Math.Round(SlVol.Value)}%";
            BriPct.Text = $"{(int)Math.Round(SlBri.Value)}%";

            SlVol.ValueChanged += (s, e) => { if (_syncing) return; _vol.Set((float)(SlVol.Value / 100)); VolPct.Text = $"{(int)Math.Round(SlVol.Value)}%"; UpdateVolIcon(); };
            SlBri.ValueChanged += (s, e) => { if (_syncing) return; _bri.Set((int)SlBri.Value); BriPct.Text = $"{(int)Math.Round(SlBri.Value)}%"; };
            VolIcon.MouseLeftButtonDown += (s, e) => { _vol.SetMute(!_vol.GetMute()); UpdateVolIcon(); };
        }

        void PollControlCenter()
        {
            if (_vol == null) return;
            if (!(_t > 0.3 && _page == "cc")) return; // only while the CC page is visible
            _syncing = true;
            double v = _vol.Get() * 100;
            if (Math.Abs(v - SlVol.Value) > 0.6) SlVol.Value = v;
            VolPct.Text = $"{(int)Math.Round(SlVol.Value)}%";
            UpdateVolIcon();
            if (_bri != null && _bri.Supported) { int b = _bri.Get(); if (b >= 0 && Math.Abs(b - SlBri.Value) > 0.6) SlBri.Value = b; BriPct.Text = $"{(int)Math.Round(SlBri.Value)}%"; }
            _syncing = false;
        }

        void UpdateVolIcon() => VolIcon.Text = _vol.GetMute() ? "" : "";

        void OnSettingsChanged()
        {
            bool want = Settings.Data.DockEnabled;
            if (want && _dock == null) { _dock = new DockWindow(); _dock.Show(); }
            else if (!want && _dock != null) { _dock.Close(); _dock = null; }

            bool wantWall = Settings.Data.WaterWallpaper;
            if (wantWall && _wall == null) StartWallpaper();
            else if (!wantWall && _wall != null) { _wall.Close(); _wall = null; }

            ApplyShape();
            ApplyLayers(false);
        }

        void StartWallpaper()
        {
            try { _wall = new WaterWallpaper(); _wall.Show(); }
            catch { _wall = null; }
        }

        // ---------- focus timer ----------
        void SetTimer(int minutes)
        {
            _tmTotal = minutes * 60;
            _tmLeft = _tmTotal;
            _tmRunning = false;
            UpdateTimerUi();
        }

        void TickTimer()
        {
            if (!_tmRunning) return;
            if (_tmLeft > 0) _tmLeft--;
            if (_tmLeft <= 0)
            {
                _tmRunning = false;
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
            UpdateTimerUi();
        }

        void UpdateTimerUi()
        {
            string txt = $"{_tmLeft / 60:0}:{_tmLeft % 60:00}";
            TmDisplay.Text = txt;
            CtDisplay.Text = txt;
            TmStart.Content = _tmRunning ? "" : ""; // pause / play
        }

        // ---------- water ----------
        void UpdateWaterUi()
        {
            if (_water.Date != DateTime.Now.ToString("yyyy-MM-dd")) _water = WaterStore.Today();
            WtText.Text = $"{_water.Amount} / {_water.Goal} ml";
            WtGoal.Text = _water.Amount >= _water.Goal ? "✓ goal reached" : "";
            double ratio = _water.Goal > 0 ? Math.Min(1.0, (double)_water.Amount / _water.Goal) : 0;
            double avail = ((FrameworkElement)WtFill.Parent).ActualWidth;
            if (avail > 0) WtFill.Width = ratio * avail;
        }

        // ---------- to-do ----------
        void OpenAddTask()
        {
            if (_addTaskOpen) return;
            _addTaskOpen = true;
            var w = new AddTaskWindow(text => { _todos.Add(new TodoItem { Text = text }); TodoStore.Save(_todos); BuildTodoList(); });
            w.Closed += (s, e) => _addTaskOpen = false;
            w.Show();
        }

        void BuildTodoList()
        {
            TodoList.Children.Clear();
            foreach (var item in _todos.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var check = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.6), BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x76)), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                if (item.Done)
                {
                    var green = new SolidColorBrush(Color.FromRgb(0x32, 0xD7, 0x4B));
                    check.Background = green; check.BorderBrush = green;
                    check.Child = new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                check.MouseLeftButtonUp += (s, e) => { item.Done = !item.Done; TodoStore.Save(_todos); BuildTodoList(); };
                Grid.SetColumn(check, 0); row.Children.Add(check);

                var txt = new TextBlock { Text = item.Text, Foreground = new SolidColorBrush(item.Done ? Color.FromRgb(0x6E, 0x6E, 0x76) : Color.FromRgb(0xE6, 0xE6, 0xEA)), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                if (item.Done) txt.TextDecorations = TextDecorations.Strikethrough;
                Grid.SetColumn(txt, 1); row.Children.Add(txt);

                var del = new TextBlock { Text = "✕", Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x76)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
                del.MouseLeftButtonUp += (s, e) => { _todos.Remove(item); TodoStore.Save(_todos); BuildTodoList(); };
                Grid.SetColumn(del, 2); row.Children.Add(del);

                TodoList.Children.Add(row);
            }
            TodoList.Visibility = _todos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TodoEmpty.Visibility = _todos.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- file shelf ----------
        void OnShelfDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                if (!_shelf.Contains(f)) _shelf.Add(f);
            ShelfStore.Save(_shelf);
            BuildShelfList();
        }

        void BuildShelfList()
        {
            ShelfList.Children.Clear();
            foreach (var path in _shelf.ToList())
            {
                var p = path;
                var icon = new Image { Source = NativeWindows.IconFor(p), Width = 40, Height = 40 };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                var name = new TextBlock { Text = System.IO.Path.GetFileName(p), Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB6)), FontSize = 10.5, MaxWidth = 64, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
                var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 8, 0), Cursor = Cursors.Hand, Width = 64, VerticalAlignment = VerticalAlignment.Center, ToolTip = p };
                stack.Children.Add(icon); stack.Children.Add(name);

                stack.MouseLeftButtonDown += (s, e) => _shelfDragStart = e.GetPosition(this);
                stack.MouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - _shelfDragStart).Length > 8 && File.Exists(p))
                    {
                        try { DragDrop.DoDragDrop(stack, new DataObject(DataFormats.FileDrop, new[] { p }), DragDropEffects.Copy); } catch { }
                    }
                };
                stack.MouseLeftButtonUp += (s, e) =>
                {
                    if ((e.GetPosition(this) - _shelfDragStart).Length <= 8)
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
                };
                var menu = new ContextMenu();
                var rm = new MenuItem { Header = "Remove from shelf" };
                rm.Click += (s, e) => { _shelf.Remove(p); ShelfStore.Save(_shelf); BuildShelfList(); };
                menu.Items.Add(rm); stack.ContextMenu = menu;

                ShelfList.Children.Add(stack);
            }
            bool any = _shelf.Count > 0;
            ShelfScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            ShelfHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        }

        void FetchWeather()
        {
            WeatherService.Fetch().ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                var w = t.Result;
                if (w == null) return;
                _weather = w;
                ShowWeather();
            }));
        }

        void ShowWeather()
        {
            if (_weather == null) { WxText.Visibility = Visibility.Collapsed; return; }
            WxText.Text = $"{_weather.Emoji}  {_weather.Temp}°  {_weather.Label}" + (string.IsNullOrEmpty(_weather.City) ? "" : $" · {_weather.City}");
            WxText.Visibility = Visibility.Visible;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ---------- offscreen capture (verify without the screen) ----------
        void RunCapture(string dir)
        {
            Directory.CreateDirectory(dir);
            // compact clock
            _mi = new MediaInfo(); _page = "info"; ApplyLayers(false);
            _t = 0; ApplyShape(); Shot(System.IO.Path.Combine(dir, "compact.png"));
            // expanded info (with weather)
            _weather = new WeatherInfo(20, "Cloudy", "☁️", "Kitale"); ShowWeather();
            _t = 1; _page = "info"; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "info.png"));
            // control center
            _page = "cc"; _syncing = true; SlVol.Value = 62; VolPct.Text = "62%"; SlBri.Value = 80; BriPct.Text = "80%"; _syncing = false;
            ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "cc.png"));
            // focus timer
            _page = "timer"; _tmTotal = 25 * 60; _tmLeft = 18 * 60 + 25; _tmRunning = true; UpdateTimerUi();
            ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "timer.png"));
            _tmRunning = false;
            // water
            _page = "water"; _water = new WaterData { Date = DateTime.Now.ToString("yyyy-MM-dd"), Amount = 1250, Goal = 2000 };
            ApplyShape(); ApplyLayers(false); Notch.UpdateLayout(); UpdateWaterUi(); Shot(System.IO.Path.Combine(dir, "water.png"));
            // to-do
            _page = "todo"; _todos = new List<TodoItem> { new() { Text = "Revise the report" }, new() { Text = "Reply to Dina", Done = true }, new() { Text = "Post weekly update" } }; BuildTodoList();
            ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "todo.png"));
            // file shelf
            _page = "shelf"; _shelf = new List<string> { System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe"), System.IO.Path.Combine(Environment.SystemDirectory, "calc.exe") }; BuildShelfList();
            ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "shelf.png"));
            // expanded media (fake now-playing so the player renders)
            OnMedia(new MediaInfo { Active = true, Playing = true, Title = "Paper Sky", Artist = "Lumen Coast · Summer Flows", Position = 71, Duration = 193 });
            _page = "media"; ApplyShape(); ApplyLayers(false); UpdateProgress(); BtnLyrics.Visibility = Visibility.Visible; Shot(System.IO.Path.Combine(dir, "media.png"));
            // synced lyrics
            _lyrics = new List<LyricLine> { new(60, "and send them through the sky"), new(63, "to find you by the sea"), new(66, "every wing a little song") };
            _lyricsOn = true; _lyIdx = -2; _mi.Position = 63.5; _mi.Playing = false;
            UpdateLyrics(); _t = 1; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "lyrics.png"));
            _lyricsOn = false;
            // compact now-playing
            _t = 0; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "compact-media.png"));
            // idle eyes
            _mi = new MediaInfo(); _t = 0; ApplyShape(); ApplyLayers(true); Shot(System.IO.Path.Combine(dir, "idle.png"));
            // minimal notch (thin resting line)
            Settings.Data.MinimalNotch = true; _page = "info"; _t = 0; ApplyShape(); ApplyLayers(false); Shot(System.IO.Path.Combine(dir, "minimal.png")); Settings.Data.MinimalNotch = false;
            // dock bar
            try { new DockWindow().RenderBarForCapture(System.IO.Path.Combine(dir, "dock.png")); } catch { }
            // settings window
            try { new SettingsWindow().RenderForCapture(System.IO.Path.Combine(dir, "settings.png")); } catch { }
            try { new InstallWindow().RenderForCapture(System.IO.Path.Combine(dir, "install.png")); } catch { }
            // water wallpaper (ripple over the real desktop wallpaper, offscreen)
            try
            {
                var wall = new WaterWallpaper(true);
                long src = wall.SourceChecksum();
                wall.CaptureTest(System.IO.Path.Combine(dir, "wallpaper.png"), 40);
                long outc = wall.OutputChecksum();
                File.WriteAllText(System.IO.Path.Combine(dir, "wallpaper.txt"),
                    $"src brightness sum={src}\nout brightness sum={outc}\ndelta={outc - src}\n");
            }
            catch (Exception ex) { try { File.WriteAllText(System.IO.Path.Combine(dir, "wallpaper.txt"), "ERR " + ex); } catch { } }
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

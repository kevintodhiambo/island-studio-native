using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IslandNative
{
    // Animated water-ripple desktop wallpaper. A full-screen window parented into the
    // desktop's WorkerW layer (so it sits BEHIND the icons, like a real wallpaper). A
    // classic height-field ripple sim runs on the CPU and refracts the user's current
    // wallpaper; moving the cursor disturbs the surface. No GPU shader needed — WPF
    // WriteableBitmap + WritePixels per frame at a downscaled grid, stretched to screen.
    //
    // Why not the Electron/WebGL approach: a native window avoids Chromium's transparent
    // compositor (the white line). Teardown just closes the window — the real wallpaper,
    // still painted by WorkerW underneath, comes straight back. We never call
    // SystemParametersInfo, which is what used to hang/lock the old Electron build.
    public class WaterWallpaper : Window
    {
        // ----- grid (sim + bitmap) -----
        int GW, GH;                 // grid resolution (downscaled from the screen)
        int ScreenPxW, ScreenPxH;   // physical screen pixels
        short[] _b1 = Array.Empty<short>();   // surface height, previous frame
        short[] _b2 = Array.Empty<short>();   // surface height, working frame
        int[] _src = Array.Empty<int>();      // wallpaper, Bgra32 ints, GW x GH
        int[] _out = Array.Empty<int>();      // refracted output, Bgra32 ints
        WriteableBitmap _wb = null!;
        DispatcherTimer? _timer;

        // ----- cursor-driven disturbance -----
        int _lastCx = -1, _lastCy = -1;
        IntPtr _hwnd;
        bool _forCapture;

        public WaterWallpaper() : this(false) { }

        public WaterWallpaper(bool forCapture)
        {
            _forCapture = forCapture;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            AllowsTransparency = false;   // opaque wallpaper (no Chromium-style transparency seam)
            Background = System.Windows.Media.Brushes.Black;
            Topmost = false;
            Title = "IslandStudioWater";

            ScreenPxW = Math.Max(640, GetSystemMetrics(0));
            ScreenPxH = Math.Max(480, GetSystemMetrics(1));

            // Sim grid: cap the long edge so the per-frame work stays cheap, keep aspect.
            GW = Math.Min(620, ScreenPxW);
            GH = Math.Max(1, (int)Math.Round(GW * (double)ScreenPxH / ScreenPxW));

            _b1 = new short[GW * GH];
            _b2 = new short[GW * GH];
            _src = new int[GW * GH];
            _out = new int[GW * GH];

            LoadWallpaperSource();
            Array.Copy(_src, _out, _src.Length);

            _wb = new WriteableBitmap(GW, GH, 96, 96, PixelFormats.Bgra32, null);
            var img = new System.Windows.Controls.Image
            {
                Source = _wb,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = false
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality); // bilinear = softer water
            Content = img;

            Left = 0; Top = 0;
            Width = ScreenPxW; Height = ScreenPxH;

            if (_forCapture) return;

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
                AttachToDesktop();
                // Cover the whole screen in physical pixels, bottom of the Z-order.
                SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, ScreenPxW, ScreenPxH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            };

            Loaded += (s, e) =>
            {
                // A soft opening ripple at the centre so it isn't dead-flat.
                Splash(GW / 2, GH / 2, 420, 6);
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _timer.Tick += (s2, e2) => Frame();
                _timer.Start();
            };

            Closed += (s, e) => { _timer?.Stop(); _timer = null; };
        }

        // ---------- per-frame ----------
        void Frame()
        {
            PollCursor();
            Step();
            Render();
            try { _wb.WritePixels(new Int32Rect(0, 0, GW, GH), _out, GW * 4, 0); } catch { }
        }

        void PollCursor()
        {
            if (!GetCursorPos(out POINT p)) return;
            int cx = (int)((long)p.X * GW / ScreenPxW);
            int cy = (int)((long)p.Y * GH / ScreenPxH);
            if (cx < 1 || cy < 1 || cx >= GW - 1 || cy >= GH - 1) { _lastCx = _lastCy = -1; return; }

            if (_lastCx >= 0)
            {
                // Interpolate along the movement so fast drags still leave a continuous wake.
                int dx = cx - _lastCx, dy = cy - _lastCy;
                int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (steps > 24) { _lastCx = cx; _lastCy = cy; }   // teleport (alt-tab etc.) — don't streak
                else if (steps == 0) Splash(cx, cy, 70, 1);
                else for (int i = 1; i <= steps; i++)
                        Splash(_lastCx + dx * i / steps, _lastCy + dy * i / steps, 70, 1);
            }
            _lastCx = cx; _lastCy = cy;
        }

        // Drop a disturbance of radius r and amplitude amp into the surface.
        void Splash(int x, int y, int amp, int r)
        {
            for (int oy = -r; oy <= r; oy++)
                for (int ox = -r; ox <= r; ox++)
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= GW || ny >= GH) continue;
                    if (ox * ox + oy * oy > r * r) continue;
                    _b1[ny * GW + nx] = (short)amp;
                }
        }

        // Classic two-buffer ripple integrator with damping.
        void Step()
        {
            var a = _b1; var b = _b2;
            for (int y = 1; y < GH - 1; y++)
            {
                int row = y * GW;
                for (int x = 1; x < GW - 1; x++)
                {
                    int i = row + x;
                    int v = ((a[i - 1] + a[i + 1] + a[i - GW] + a[i + GW]) >> 1) - b[i];
                    v -= v >> 6;   // damping (~1.5%) so ripples fade
                    b[i] = (short)v;
                }
            }
            _b1 = b; _b2 = a;   // swap: _b1 is now the current surface
        }

        // Refract the wallpaper through the surface gradient + a light specular highlight.
        void Render()
        {
            var h = _b1;
            for (int y = 1; y < GH - 1; y++)
            {
                int row = y * GW;
                for (int x = 1; x < GW - 1; x++)
                {
                    int i = row + x;
                    int dx = h[i - 1] - h[i + 1];
                    int dy = h[i - GW] - h[i + GW];
                    int sx = x + (dx >> 3);
                    int sy = y + (dy >> 3);
                    if (sx < 0) sx = 0; else if (sx >= GW) sx = GW - 1;
                    if (sy < 0) sy = 0; else if (sy >= GH) sy = GH - 1;
                    int c = _src[sy * GW + sx];

                    int hl = (dx + dy) >> 2;   // fake specular from the slope
                    if (hl != 0)
                    {
                        int b = (c & 0xFF) + hl;
                        int g = ((c >> 8) & 0xFF) + hl;
                        int r = ((c >> 16) & 0xFF) + hl;
                        if (b < 0) b = 0; else if (b > 255) b = 255;
                        if (g < 0) g = 0; else if (g > 255) g = 255;
                        if (r < 0) r = 0; else if (r > 255) r = 255;
                        c = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                    }
                    _out[i] = c;
                }
            }
        }

        // ---------- wallpaper image ----------
        void LoadWallpaperSource()
        {
            try
            {
                var sb = new StringBuilder(520);
                SystemParametersInfo(SPI_GETDESKWALLPAPER, 520, sb, 0);
                string path = sb.ToString();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    RasterizeTo(bmp);
                    return;
                }
            }
            catch { }
            FillGradient();   // no readable wallpaper → a calm deep-blue gradient
        }

        // Draw the wallpaper into the GW x GH grid (fill, cropping overflow) and read Bgra32 ints.
        void RasterizeTo(BitmapSource bmp)
        {
            try
            {
                double scale = Math.Max((double)GW / bmp.PixelWidth, (double)GH / bmp.PixelHeight);
                double w = bmp.PixelWidth * scale, hgt = bmp.PixelHeight * scale;
                double ox = (GW - w) / 2, oy = (GH - hgt) / 2;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(0, 0, GW, GH));
                    dc.DrawImage(bmp, new Rect(ox, oy, w, hgt));
                }
                var rtb = new RenderTargetBitmap(GW, GH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var fcb = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
                fcb.CopyPixels(_src, GW * 4, 0);
            }
            catch { FillGradient(); }
        }

        void FillGradient()
        {
            for (int y = 0; y < GH; y++)
            {
                double t = (double)y / Math.Max(1, GH - 1);
                int r = (int)(8 + t * 10);
                int g = (int)(22 + t * 26);
                int b = (int)(54 + t * 60);
                int c = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                int row = y * GW;
                for (int x = 0; x < GW; x++) _src[row + x] = c;
            }
        }

        // ---------- desktop (WorkerW) parenting ----------
        void AttachToDesktop()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                // Ask Progman to spawn the WorkerW that hosts the wallpaper behind the icons.
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

                IntPtr workerw = IntPtr.Zero;
                EnumWindows((top, l) =>
                {
                    if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                        workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    return true;
                }, IntPtr.Zero);

                if (workerw != IntPtr.Zero) SetParent(_hwnd, workerw);
                else SetParent(_hwnd, progman);   // fallback: sit on Progman itself
            }
            catch { }
        }

        // ---------- offscreen verification ----------
        // Build the ripple over the real wallpaper (or gradient), splash, run N frames, save a PNG.
        public void CaptureTest(string path, int frames = 40)
        {
            Splash(GW / 2, GH / 2, 500, 7);
            Splash(GW / 3, GH / 2, 400, 5);
            Splash(2 * GW / 3, 2 * GH / 3, 400, 5);
            for (int f = 0; f < frames; f++) { Step(); Render(); }
            _wb.WritePixels(new Int32Rect(0, 0, GW, GH), _out, GW * 4, 0);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(_wb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        // Average brightness of the output buffer — lets the harness assert the ripple
        // actually distorted the image (a flat copy of the wallpaper vs. a rippled one).
        public long OutputChecksum()
        {
            long sum = 0;
            for (int i = 0; i < _out.Length; i++) sum += _out[i] & 0xFF;
            return sum;
        }
        public long SourceChecksum()
        {
            long sum = 0;
            for (int i = 0; i < _src.Length; i++) sum += _src[i] & 0xFF;
            return sum;
        }

        // ---------- interop ----------
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int SPI_GETDESKWALLPAPER = 0x0073;
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindow(string? cls, string? name);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint ms, out IntPtr res);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool SystemParametersInfo(int action, int param, StringBuilder val, int winini);
    }
}

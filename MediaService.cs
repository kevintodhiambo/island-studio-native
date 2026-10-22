using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IslandNative
{
    public class MediaInfo
    {
        public bool Active;
        public string Title = "";
        public string Artist = "";
        public bool Playing;
        public BitmapImage? Art;
        public double Position;   // seconds
        public double Duration;   // seconds
        public DateTime Stamp = DateTime.Now;
    }

    // Now-playing + transport controls via Windows' native media session API
    // (the same source the OS volume flyout reads). Works with Spotify, Apple Music,
    // browsers, etc. — no polling, Windows pushes change events.
    public class MediaService
    {
        GlobalSystemMediaTransportControlsSessionManager? _mgr;
        GlobalSystemMediaTransportControlsSession? _session;
        readonly Dispatcher _disp;

        public event Action<MediaInfo>? Changed;
        public MediaInfo Current { get; private set; } = new();

        public MediaService(Dispatcher disp) { _disp = disp; }

        public async void Start()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, e) => Hook();
                Hook();
            }
            catch { /* media API unavailable */ }
        }

        void Hook()
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnChanged;
                _session.PlaybackInfoChanged -= OnChanged;
                _session.TimelinePropertiesChanged -= OnChanged;
            }
            _session = _mgr?.GetCurrentSession();
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnChanged;
                _session.PlaybackInfoChanged += OnChanged;
                _session.TimelinePropertiesChanged += OnChanged;
            }
            Refresh();
        }

        void OnChanged(object? s, object? e) => Refresh();

        async void Refresh()
        {
            var info = new MediaInfo();
            try
            {
                if (_session != null)
                {
                    var pb = _session.GetPlaybackInfo();
                    info.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var tl = _session.GetTimelineProperties();
                    info.Position = tl.Position.TotalSeconds;
                    info.Duration = tl.EndTime.TotalSeconds;

                    var props = await _session.TryGetMediaPropertiesAsync();
                    if (props != null)
                    {
                        info.Title = props.Title ?? "";
                        info.Artist = props.Artist ?? "";
                        info.Active = !string.IsNullOrWhiteSpace(info.Title);
                        if (props.Thumbnail != null)
                            info.Art = await LoadThumb(props.Thumbnail);
                    }
                }
            }
            catch { }
            info.Stamp = DateTime.Now;
            Current = info;
            try { _disp.BeginInvoke(() => Changed?.Invoke(info)); } catch { }
        }

        static async Task<BitmapImage?> LoadThumb(IRandomAccessStreamReference reference)
        {
            try
            {
                using var stream = await reference.OpenReadAsync();
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                var bytes = new byte[stream.Size];
                reader.ReadBytes(bytes);

                var bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public async void TogglePlay() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async void Prev() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
    }
}

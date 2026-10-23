using System;
using System.Runtime.InteropServices;

namespace IslandNative
{
    // Master volume + mute via the Windows Core Audio API (IAudioEndpointVolume).
    public class VolumeService
    {
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        IAudioEndpointVolume? _vol;
        Guid _ctx = Guid.NewGuid();

        public bool Available => _vol != null;

        public VolumeService()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var dev);
                var iid = typeof(IAudioEndpointVolume).GUID;
                dev.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var o);
                _vol = (IAudioEndpointVolume)o;
            }
            catch { _vol = null; }
        }

        public float Get()
        {
            if (_vol == null) return 0;
            try { _vol.GetMasterVolumeLevelScalar(out float v); return v; } catch { return 0; }
        }

        public void Set(float v)
        {
            if (_vol == null) return;
            try { _vol.SetMasterVolumeLevelScalar(Math.Clamp(v, 0f, 1f), ref _ctx); } catch { }
        }

        public bool GetMute()
        {
            if (_vol == null) return false;
            try { _vol.GetMute(out bool m); return m; } catch { return false; }
        }

        public void SetMute(bool m)
        {
            if (_vol == null) return;
            try { _vol.SetMute(m, ref _ctx); } catch { }
        }
    }
}

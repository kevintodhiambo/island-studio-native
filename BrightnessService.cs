using System;
using System.Management;

namespace IslandNative
{
    // Display brightness via WMI (laptop internal panels). Supported is false on
    // desktops / monitors that don't expose WmiMonitorBrightness.
    public class BrightnessService
    {
        public bool Supported { get; private set; }

        public BrightnessService() { Supported = Get() >= 0; }

        public int Get()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public void Set(int pct)
        {
            pct = Math.Clamp(pct, 0, 100);
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in s.Get())
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct });
            }
            catch { }
        }
    }
}

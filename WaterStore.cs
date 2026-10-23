using System;
using System.IO;
using System.Text.Json;

namespace IslandNative
{
    public class WaterData
    {
        public string Date { get; set; } = "";
        public int Amount { get; set; }
        public int Goal { get; set; } = 2000;
    }

    // Today's water intake, persisted to %APPDATA%\IslandStudio\water.json (resets each day).
    public static class WaterStore
    {
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandStudio");
        static string FilePath => Path.Combine(Dir, "water.json");

        public static WaterData Today()
        {
            var d = Load();
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (d.Date != today) { d.Date = today; d.Amount = 0; Save(d); }
            return d;
        }

        static WaterData Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<WaterData>(File.ReadAllText(FilePath)) ?? new WaterData(); }
            catch { }
            return new WaterData();
        }

        public static void Save(WaterData d)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
    }
}

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace IslandNative
{
    public record WeatherInfo(int Temp, string Label, string Emoji, string City);

    // Current weather with no API key: ipapi.co for coarse (city-level) location from the
    // IP, open-meteo for the forecast. Only the IP-derived coordinates leave the machine.
    public static class WeatherService
    {
        static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        static WeatherService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("IslandStudio/0.7");

        public static async Task<WeatherInfo?> Fetch()
        {
            try
            {
                using var locDoc = JsonDocument.Parse(await _http.GetStringAsync("https://ipapi.co/json/"));
                var loc = locDoc.RootElement;
                if (!loc.TryGetProperty("latitude", out var latEl) || latEl.ValueKind != JsonValueKind.Number) return null;
                double lat = latEl.GetDouble(), lon = loc.GetProperty("longitude").GetDouble();
                string city = loc.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? "") : "";

                string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code&temperature_unit=celsius";
                using var wDoc = JsonDocument.Parse(await _http.GetStringAsync(url));
                if (!wDoc.RootElement.TryGetProperty("current", out var cur)) return null;
                int temp = (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble());
                int code = cur.GetProperty("weather_code").GetInt32();
                var (label, emoji) = Describe(code);
                return new WeatherInfo(temp, label, emoji, city);
            }
            catch { return null; }
        }

        // WMO weather codes → a short label + emoji.
        static (string, string) Describe(int code) => code switch
        {
            0 => ("Clear", "☀️"),
            <= 2 => ("Partly cloudy", "⛅"),
            3 => ("Cloudy", "☁️"),
            <= 48 => ("Fog", "\U0001F32B️"),
            <= 57 => ("Drizzle", "\U0001F327️"),
            <= 67 => ("Rain", "\U0001F327️"),
            <= 77 => ("Snow", "❄️"),
            <= 82 => ("Showers", "\U0001F326️"),
            <= 86 => ("Snow showers", "\U0001F328️"),
            _ => ("Thunderstorm", "⛈️"),
        };
    }
}

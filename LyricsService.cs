using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IslandNative
{
    public record LyricLine(double Time, string Text);

    // Synced lyrics from LRCLIB (lrclib.net) — free, no key. Only the song title,
    // artist and duration are sent; nothing else leaves the machine.
    public static class LyricsService
    {
        static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        static LyricsService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("IslandStudio/0.5 (https://github.com/kevintodhiambo/island-studio-native)");
        }

        public static async Task<List<LyricLine>?> Fetch(string title, string artist, double duration)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            try
            {
                string get = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}&duration={(int)duration}";
                var resp = await _http.GetAsync(get);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String)
                    {
                        var p = Parse(s.GetString());
                        if (p.Count > 0) return p;
                    }
                }

                // fallback: fuzzy search, take the first result with synced lyrics
                string search = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                var sr = await _http.GetAsync(search);
                if (!sr.IsSuccessStatusCode) return null;
                using var arr = JsonDocument.Parse(await sr.Content.ReadAsStringAsync());
                foreach (var el in arr.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String)
                    {
                        var p = Parse(sl.GetString());
                        if (p.Count > 0) return p;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        static List<LyricLine> Parse(string? lrc)
        {
            var list = new List<LyricLine>();
            if (lrc == null) return list;
            foreach (var raw in lrc.Replace("\r", "").Split('\n'))
            {
                int i = 0;
                var times = new List<double>();
                while (i < raw.Length && raw[i] == '[')
                {
                    int close = raw.IndexOf(']', i);
                    if (close < 0) break;
                    if (TryParseTime(raw.Substring(i + 1, close - i - 1), out double t)) times.Add(t);
                    i = close + 1;
                }
                string text = raw.Substring(i).Trim();
                foreach (var t in times) list.Add(new LyricLine(t, text));
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        static bool TryParseTime(string tag, out double sec)
        {
            sec = 0;
            var m = Regex.Match(tag, @"^(\d+):(\d+)(\.\d+)?$");
            if (!m.Success) return false;
            sec = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
            if (m.Groups[3].Success) sec += double.Parse("0" + m.Groups[3].Value, CultureInfo.InvariantCulture);
            return true;
        }
    }
}

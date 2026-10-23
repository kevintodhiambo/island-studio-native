using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IslandNative
{
    public class TodoItem { public string Text { get; set; } = ""; public bool Done { get; set; } }

    public static class TodoStore
    {
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandStudio");
        static string FilePath => Path.Combine(Dir, "todo.json");

        public static List<TodoItem> Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath)) ?? new(); }
            catch { }
            return new List<TodoItem>();
        }

        public static void Save(List<TodoItem> items)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
    }

    // The shelf remembers where files live (paths), not copies of them — like Nusa.
    public static class ShelfStore
    {
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandStudio");
        static string FilePath => Path.Combine(Dir, "shelf.json");

        public static List<string> Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new(); }
            catch { }
            return new List<string>();
        }

        public static void Save(List<string> paths)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
    }
}

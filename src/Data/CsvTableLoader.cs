using System.Collections.Generic;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Utils;
using FileAccess = Godot.FileAccess; // implicit System.IO конфликтует с Godot.FileAccess

namespace GrandStrategy.Data;

/// <summary>
/// Загрузчик CSV-таблиц данных (баланс, товары, страны и т.д.).
/// Первая строка — заголовки. Возвращает список строк-словарей (заголовок -> значение).
/// </summary>
public static class CsvTableLoader
{
    public static List<Dictionary<string, string>> Load(string resPath)
    {
        var rows = new List<Dictionary<string, string>>();
        if (!FileAccess.FileExists(resPath))
        {
            LogService.Instance.Error($"CSV not found: {resPath}");
            return rows;
        }

        using FileAccess file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string content = file.GetAsText();
        List<string[]> parsed = CsvParser.Parse(content);
        if (parsed.Count == 0)
            return rows;

        string[] header = parsed[0];
        for (int r = 1; r < parsed.Count; r++)
        {
            string[] cells = parsed[r];
            var row = new Dictionary<string, string>();
            for (int c = 0; c < header.Length && c < cells.Length; c++)
            {
                row[header[c].Trim()] = cells[c].Trim();
            }
            rows.Add(row);
        }
        return rows;
    }

    // --- Типизированные хелперы чтения ячеек ---------------------------------

    public static int Int(Dictionary<string, string> row, string key, int fallback = 0) =>
        row.TryGetValue(key, out string? v) && int.TryParse(v, out int r) ? r : fallback;

    public static long Long(Dictionary<string, string> row, string key, long fallback = 0) =>
        row.TryGetValue(key, out string? v) && long.TryParse(v, out long r) ? r : fallback;

    public static double Double(Dictionary<string, string> row, string key, double fallback = 0.0)
    {
        if (!row.TryGetValue(key, out string? v))
            return fallback;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : fallback;
    }

    public static float Float(Dictionary<string, string> row, string key, float fallback = 0f) =>
        (float)Double(row, key, fallback);

    public static bool Bool(Dictionary<string, string> row, string key, bool fallback = false) =>
        row.TryGetValue(key, out string? v) && bool.TryParse(v, out bool r) ? r : fallback;

    public static string Str(Dictionary<string, string> row, string key, string fallback = "") =>
        row.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v) ? v : fallback;
}

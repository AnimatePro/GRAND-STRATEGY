using System.Collections.Generic;
using GrandStrategy.Utils;

namespace GrandStrategy.Data;

/// <summary>Загрузка CSV с диска (не res://) — для редакторных инструментов и импорта.</summary>
public static class CsvTableLoaderFile
{
    public static List<Dictionary<string, string>> LoadFile(string absolutePath)
    {
        var rows = new List<Dictionary<string, string>>();
        if (!System.IO.File.Exists(absolutePath))
            return rows;

        string content = System.IO.File.ReadAllText(absolutePath);
        List<string[]> parsed = CsvParser.Parse(content);
        if (parsed.Count == 0)
            return rows;

        string[] header = parsed[0];
        for (int r = 1; r < parsed.Count; r++)
        {
            string[] cells = parsed[r];
            var row = new Dictionary<string, string>();
            for (int c = 0; c < header.Length && c < cells.Length; c++)
                row[header[c].Trim()] = cells[c].Trim();
            rows.Add(row);
        }
        return rows;
    }
}

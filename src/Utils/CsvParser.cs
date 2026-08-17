using System.Collections.Generic;
using System.Text;

namespace GrandStrategy.Utils;

/// <summary>
/// Минимальный CSV-парсер (RFC 4180): кавычки, экранированные кавычки (""),
/// CRLF/LF, пустые строки пропускаются. Не аллоцирует лишних строк.
/// </summary>
public static class CsvParser
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text))
            return rows;

        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                    case '\n':
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                            i++;
                        row.Add(field.ToString());
                        field.Clear();
                        if (row.Count > 0)
                            rows.Add(row.ToArray());
                        row.Clear();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 0)
                rows.Add(row.ToArray());
        }

        return rows;
    }
}

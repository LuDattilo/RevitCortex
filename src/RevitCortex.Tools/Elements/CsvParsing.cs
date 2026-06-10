using System.Collections.Generic;
using System.Text;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Delimiter-aware RFC 4180 CSV parsing shared by the Power BI round-trip
/// tools. RevitCortex emits two CSV dialects: push_to_powerbi writes
/// ','-delimited files, export_elements_data writes ';'-delimited files
/// (Excel-friendly for European locales). The sniffer inspects the header
/// line so import_from_powerbi accepts both without configuration.
/// </summary>
public static class CsvParsing
{
    /// <summary>
    /// Picks ';' or ',' by counting unquoted occurrences in the first record
    /// line. Semicolon wins only when strictly more frequent; ties and
    /// delimiter-free headers default to ','.
    /// </summary>
    public static char SniffDelimiter(string content)
    {
        int commas = 0, semicolons = 0;
        bool inQuotes = false;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') i++;
                    else inQuotes = false;
                }
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') commas++;
            else if (c == ';') semicolons++;
            else if (c == '\n') break;
        }
        return semicolons > commas ? ';' : ',';
    }

    /// <summary>
    /// Minimal RFC 4180 CSV reader: handles quoted fields, doubled quotes,
    /// embedded newlines and delimiters. Returns headers + rows (each as a
    /// fixed-length string array — short rows are padded, blank lines are
    /// skipped). When <paramref name="delimiter"/> is null the separator is
    /// sniffed from the header line via <see cref="SniffDelimiter"/>.
    /// </summary>
    public static (List<string> headers, List<string[]> rows) Parse(string content, char? delimiter = null)
    {
        char sep = delimiter ?? SniffDelimiter(content);

        var fields = new List<List<string>>();
        var current = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == sep)
                {
                    current.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\r')
                {
                    // ignore — handled by the LF that should follow
                }
                else if (c == '\n')
                {
                    current.Add(sb.ToString());
                    sb.Clear();
                    fields.Add(current);
                    current = new List<string>();
                }
                else sb.Append(c);
            }
        }
        if (sb.Length > 0 || current.Count > 0)
        {
            current.Add(sb.ToString());
            fields.Add(current);
        }

        if (fields.Count == 0) return (new(), new());
        var headers = fields[0];
        var rowsOut = new List<string[]>();
        int width = headers.Count;
        for (int r = 1; r < fields.Count; r++)
        {
            var row = fields[r];
            if (row.Count == 0 || (row.Count == 1 && string.IsNullOrEmpty(row[0]))) continue;
            // Pad short rows to the header width
            while (row.Count < width) row.Add("");
            rowsOut.Add(row.ToArray());
        }
        return (headers, rowsOut);
    }
}

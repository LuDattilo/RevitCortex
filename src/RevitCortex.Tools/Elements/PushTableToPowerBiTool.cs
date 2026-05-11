using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Writes an arbitrary tabular result (headers + rows) directly to a CSV in
/// the RevitCortex / OneDrive folder, ready for Power BI consumption.
///
/// Use cases:
///   1. Claude composed a table during analysis and wants it on PBI
///   2. Bridges any computation done outside Revit's native data model
///   3. Multi-document aggregations that <see cref="PushToPowerBiTool"/> can't do
///      because it operates on a single active document
///
/// The companion to push_to_powerbi: same output folder, same metadata sidecar,
/// same drillthrough URL pattern works if rows include an ElementId column.
/// </summary>
public class PushTableToPowerBiTool : ICortexTool
{
    public string Name => "push_table_to_powerbi";
    public string Category => "Elements";
    public bool RequiresDocument => false; // works without an open document
    public bool IsDynamic => false;
    public string Description => "Writes a custom table (headers + rows) to a CSV file in the RevitCortex/OneDrive folder for Power BI.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var headersToken = input["headers"] as JArray;
        var rowsToken = input["rows"] as JArray;
        if (headersToken == null || headersToken.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "headers is required (array of strings, e.g. [\"Level\",\"Volume\"])");
        if (rowsToken == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "rows is required (array of arrays of values)");

        var headers = headersToken.Select(t => t?.ToString() ?? "").ToList();
        var rows = new List<List<string>>();
        foreach (var rowToken in rowsToken)
        {
            if (rowToken is JArray arr)
            {
                rows.Add(arr.Select(t => CellToString(t)).ToList());
            }
            else if (rowToken is JObject obj)
            {
                // Allow object form: { "Level": "L1", "Volume": 12.3 }
                var cells = new List<string>();
                foreach (var h in headers)
                {
                    var v = obj[h];
                    cells.Add(v != null ? CellToString(v) : "");
                }
                rows.Add(cells);
            }
        }

        // Output location
        var outputFolder = input["outputFolder"]?.Value<string>();
        var fileName = input["fileName"]?.Value<string>();
        var subfolder = input["subfolder"]?.Value<string>(); // optional subfolder name

        if (string.IsNullOrEmpty(outputFolder))
        {
            var oneDrive = FindOneDriveFolder();
            // Use document title if available, else "ChatTables"
            string contextName;
            try
            {
                var doc = session.Store.Get<object>("activeDocument") as Document;
                contextName = doc != null ? SanitizeFolderName(doc.Title) : "ChatTables";
            }
            catch { contextName = "ChatTables"; }
            outputFolder = Path.Combine(oneDrive, "RevitCortex", contextName);
        }

        if (!string.IsNullOrEmpty(subfolder))
            outputFolder = Path.Combine(outputFolder, SanitizeFolderName(subfolder!));

        if (string.IsNullOrEmpty(fileName))
            fileName = $"table_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        if (!fileName!.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            fileName += ".csv";

        try
        {
            Directory.CreateDirectory(outputFolder);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Cannot create output folder '{outputFolder}': {ex.Message}");
        }

        var filePath = Path.Combine(outputFolder, fileName);
        var metaPath = Path.Combine(outputFolder, "last_refresh.json");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in rows)
            {
                // Pad short rows to header width
                while (row.Count < headers.Count) row.Add("");
                if (row.Count > headers.Count) row.RemoveRange(headers.Count, row.Count - headers.Count);
                sb.AppendLine(string.Join(",", row.Select(CsvEscape)));
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            var meta = new JObject
            {
                ["refreshed_at"] = DateTime.UtcNow.ToString("o"),
                ["mode"] = "table",
                ["row_count"] = rows.Count,
                ["column_count"] = headers.Count,
                ["file"] = fileName
            };
            File.WriteAllText(metaPath, meta.ToString(), Encoding.UTF8);

            return CortexResult<object>.Ok(new
            {
                filePath,
                metaPath,
                rowCount = rows.Count,
                columnCount = headers.Count,
                tip = "Power BI: 'Get Data → Folder' on the parent path picks up this file together with push_to_powerbi exports."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Write failed: {ex.Message}");
        }
    }

    private static string CellToString(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return "";
        switch (token.Type)
        {
            case JTokenType.Float:
                // Use invariant decimal point so PBI parses numbers cleanly
                return ((double)token).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            case JTokenType.Integer:
                return ((long)token).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case JTokenType.Boolean:
                return ((bool)token) ? "true" : "false";
            case JTokenType.Date:
                return ((DateTime)token).ToString("o");
            default:
                return token.ToString();
        }
    }

    private static string FindOneDriveFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
        {
            Path.Combine(userProfile, "OneDrive - GPA Ingegneria Srl"),
            Path.Combine(userProfile, "OneDrive - GPA Partners"),
            Path.Combine(userProfile, "OneDrive")
        })
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private static string CsvEscape(string value)
    {
        if (value == null) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

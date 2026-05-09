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
/// Round-trip companion of <see cref="PushToPowerBiTool"/>. Reads a CSV that
/// was previously exported (or a hand-edited copy of one) and writes the
/// values back to Revit element parameters. Identification key is the
/// ElementId column. Read-only and computed columns are silently skipped
/// (they're filtered upfront so the user is never blocked by a single bad cell).
///
/// Schedule mode is NOT supported — schedule CSVs use display strings, not
/// raw parameter values, and writing them back would require unit re-parsing
/// per parameter type. Out of scope for this MVP.
/// </summary>
public class ImportFromPowerBiTool : ICortexTool
{
    public string Name => "import_from_powerbi";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reads a Power BI / RevitCortex CSV and writes parameter values back to Revit elements. Use after editing the CSV externally.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var filePath = input["filePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "filePath is required (path to the CSV)");
        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"File not found: {filePath}");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var idColumn = input["idColumn"]?.Value<string>() ?? "ElementId";
        // Optional: only update these columns (alias headers as in the CSV).
        var columnFilter = input["columns"]?.ToObject<List<string>>();

        // Parse CSV
        List<string> headers;
        List<string[]> rows;
        try
        {
            (headers, rows) = ReadCsv(filePath);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"CSV parse failed: {ex.Message}");
        }

        if (headers.Count == 0 || rows.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "CSV is empty.");

        int idColIndex = headers.FindIndex(h => string.Equals(h.Trim(), idColumn, StringComparison.OrdinalIgnoreCase));
        if (idColIndex < 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"ID column '{idColumn}' not found. Available headers: {string.Join(", ", headers)}",
                suggestion: "Use the same column name your CSV uses for the Revit ElementId.");

        // Decide which columns to write back. Skip columns that are clearly
        // read-only built-ins (Category/Family/Type) or computed (header containing
        // any character outside parameter-name set).
        var writable = new List<(int colIndex, string header)>();
        var skipped = new List<string>();
        var builtInsSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ElementId", "Category", "Family", "Type" };

        for (int i = 0; i < headers.Count; i++)
        {
            if (i == idColIndex) continue;
            var h = headers[i].Trim();
            if (string.IsNullOrEmpty(h)) continue;
            if (builtInsSkip.Contains(h)) { skipped.Add(h); continue; }
            if (columnFilter != null && columnFilter.Count > 0 &&
                !columnFilter.Any(f => string.Equals(f, h, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            writable.Add((i, h));
        }

        // Compute write plan and (optionally) ask for confirmation
        int affectedRows = rows.Count;
        if (!dryRun)
        {
            if (!session.RequestConfirmation("write parameters from CSV", affectedRows,
                $"{affectedRows} righe × {writable.Count} colonne dal file:\n{filePath}"))
            {
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
            }
        }

        int updated = 0, missingElems = 0, paramNotFound = 0, readOnlyHits = 0, errors = 0;
        var firstErrors = new List<string>();

        // Open one transaction for the whole batch (Revit allows up to 1M ops).
        using var tx = new Transaction(doc, "RevitCortex: Import from Power BI CSV");
        if (!dryRun)
        {
            try { tx.Start(); }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Cannot start transaction: {ex.Message}");
            }
        }

        foreach (var row in rows)
        {
            if (row.Length <= idColIndex) continue;
            var idCell = row[idColIndex].Trim();
            if (!long.TryParse(idCell, out var idValue)) continue;

#if REVIT2024_OR_GREATER
            var elemId = new ElementId(idValue);
#else
            var elemId = new ElementId((int)idValue);
#endif
            var elem = doc.GetElement(elemId);
            if (elem == null) { missingElems++; continue; }

            foreach (var (colIdx, header) in writable)
            {
                if (row.Length <= colIdx) continue;
                var newValue = row[colIdx];

                // Locate parameter (header may be "[Type] X" → type param, else instance)
                Parameter? p;
                bool isTypeParam = header.StartsWith("[Type] ", StringComparison.OrdinalIgnoreCase);
                string paramName = isTypeParam ? header.Substring("[Type] ".Length) : header;

                if (isTypeParam)
                {
                    var typeId = elem.GetTypeId();
                    var typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                    p = typeElem?.LookupParameter(paramName);
                }
                else
                {
                    p = elem.LookupParameter(paramName);
                }

                if (p == null) { paramNotFound++; continue; }
                if (p.IsReadOnly) { readOnlyHits++; continue; }

                if (!dryRun)
                {
                    try
                    {
                        if (TrySetParameterValue(p, newValue))
                            updated++;
                        else
                            errors++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        if (firstErrors.Count < 10)
                            firstErrors.Add($"id={idValue} '{header}': {ex.Message}");
                    }
                }
                else
                {
                    updated++; // dryRun: count what *would* be updated
                }
            }
        }

        if (!dryRun)
        {
            try { tx.Commit(); }
            catch (Exception ex)
            {
                try { tx.RollBack(); } catch { }
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Commit failed: {ex.Message}");
            }
        }

        return CortexResult<object>.Ok(new
        {
            file = filePath,
            dryRun,
            rows = affectedRows,
            writableColumns = writable.Count,
            updatedCount = updated,
            missingElements = missingElems,
            parameterNotFound = paramNotFound,
            readOnlyHits,
            errors,
            firstErrors,
            skippedHeaders = skipped,
            tip = dryRun
                ? "dryRun=true (default) — re-run with dryRun:false to commit changes."
                : "Changes committed. Open the document if not already, or undo in Revit if needed."
        });
    }

    /// <summary>
    /// Sets a parameter value from a string. Strings, integers, doubles
    /// (with unit-aware AsValueString), and ElementIds are supported.
    /// Returns false on type mismatch.
    /// </summary>
    private static bool TrySetParameterValue(Parameter p, string value)
    {
        switch (p.StorageType)
        {
            case StorageType.String:
                return p.Set(value ?? "");

            case StorageType.Integer:
                if (int.TryParse(value, out var iv)) return p.Set(iv);
                // accept "Yes"/"No" for boolean-like Yes/No params
                if (string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "Si", StringComparison.OrdinalIgnoreCase) ||
                    value == "1") return p.Set(1);
                if (string.Equals(value, "No", StringComparison.OrdinalIgnoreCase) ||
                    value == "0" || string.IsNullOrEmpty(value)) return p.Set(0);
                return false;

            case StorageType.Double:
                // Try unit-aware parse first (accepts "150 mm" / "2.5 m²" formatted values).
                if (p.SetValueString(value)) return true;
                // Fallback: raw double in project internal units.
                if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    return p.Set(dv);
                if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.CurrentCulture, out var dv2))
                    return p.Set(dv2);
                return false;

            case StorageType.ElementId:
                if (long.TryParse(value, out var lv))
                {
#if REVIT2024_OR_GREATER
                    return p.Set(new ElementId(lv));
#else
                    return p.Set(new ElementId((int)lv));
#endif
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Minimal RFC 4180 CSV reader: handles quoted fields, doubled quotes,
    /// embedded newlines and commas. Returns headers + rows (each as a
    /// fixed-length string array — short rows are padded).
    /// </summary>
    private static (List<string> headers, List<string[]> rows) ReadCsv(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
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
                else if (c == ',')
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
        return (headers, rowsOut.ToArray().ToList());
    }
}

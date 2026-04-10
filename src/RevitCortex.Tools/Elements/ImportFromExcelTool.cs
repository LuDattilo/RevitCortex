using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Imports data from Excel (.xlsx) into Revit element parameters.
/// Requires an ElementId column to match elements.
/// </summary>
public class ImportFromExcelTool : ICortexTool
{
    public string Name => "import_from_excel";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Imports data from Excel (.xlsx) into Revit element parameters. Requires an ElementId column to match elements.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var filePath = input["filePath"]?.Value<string>();
        var sheetName = input["sheetName"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "filePath is required and must exist");

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var ws = string.IsNullOrEmpty(sheetName)
                ? workbook.Worksheets.First()
                : workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
                  ?? workbook.Worksheets.First();

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

            // Read headers
            var headers = new List<string>();
            for (int col = 1; col <= lastCol; col++)
                headers.Add(ws.Cell(1, col).GetString().Trim());

            var idColIndex = headers.FindIndex(h => h.Equals("ElementId", StringComparison.OrdinalIgnoreCase));
            if (idColIndex < 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Excel must have an 'ElementId' column", suggestion: "Export with export_to_excel first");

            var paramColumns = headers
                .Select((h, i) => new { Name = h, Index = i })
                .Where(x => x.Index != idColIndex && !string.IsNullOrEmpty(x.Name))
                .Where(x => !x.Name.StartsWith("[Type]")) // Skip type param columns for now
                .ToList();

            int updated = 0, skipped = 0, failed = 0;
            var results = new List<object>();

            if (!dryRun)
            {
                using var tx = new Transaction(doc, "RevitCortex: Import From Excel");
                tx.Start();

                for (int row = 2; row <= lastRow; row++)
                {
                    var idStr = ws.Cell(row, idColIndex + 1).GetString();
                    if (!long.TryParse(idStr, out var elemId)) { skipped++; continue; }

#if REVIT2024_OR_GREATER
                    var elem = doc.GetElement(new ElementId(elemId));
#else
                    var elem = doc.GetElement(new ElementId((int)elemId));
#endif
                    if (elem == null) { skipped++; continue; }

                    int setCount = 0;
                    foreach (var pc in paramColumns)
                    {
                        var param = elem.LookupParameter(pc.Name);
                        if (param == null || param.IsReadOnly) continue;

                        var cellValue = ws.Cell(row, pc.Index + 1).GetString();
                        try
                        {
                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    param.Set(cellValue);
                                    setCount++;
                                    break;
                                case StorageType.Double when double.TryParse(cellValue, out var d):
                                    param.Set(d);
                                    setCount++;
                                    break;
                                case StorageType.Integer when int.TryParse(cellValue, out var i):
                                    param.Set(i);
                                    setCount++;
                                    break;
                            }
                        }
                        catch { failed++; }
                    }

                    if (setCount > 0) updated++;
                    results.Add(new { elementId = elemId, parametersSet = setCount });
                }

                tx.Commit();
            }
            else
            {
                for (int row = 2; row <= lastRow; row++)
                {
                    var idStr = ws.Cell(row, idColIndex + 1).GetString();
                    if (!long.TryParse(idStr, out var elemId)) { skipped++; continue; }

#if REVIT2024_OR_GREATER
                    var elem = doc.GetElement(new ElementId(elemId));
#else
                    var elem = doc.GetElement(new ElementId((int)elemId));
#endif
                    if (elem == null) { skipped++; continue; }

                    var matchCount = paramColumns.Count(pc => elem.LookupParameter(pc.Name) != null);
                    results.Add(new { elementId = elemId, matchingParameters = matchCount });
                    updated++;
                }
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                filePath,
                sheetName = ws.Name,
                totalRows = lastRow - 1,
                updatedCount = updated,
                skippedCount = skipped,
                failedCount = failed,
                parameterColumns = paramColumns.Select(p => p.Name).ToList(),
                results = results.Take(100).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

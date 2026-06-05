using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Imports a CSV/TSV file as a formatted table of text notes in a drafting or legend view.
/// </summary>
public class ImportTableTool : ICortexTool
{
    public string Name => "import_table";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Imports a CSV/TSV file as a formatted table of text notes in a drafting or legend view.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var filePath = input["filePath"]?.Value<string>();
        // H28: restrict reads to user-owned directories; reject traversal/UNC/system paths.
        if (!PathSafety.TryResolveSafe(filePath, out var safePath, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        filePath = safePath;
        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"File not found: {filePath}",
                suggestion: "Provide a valid absolute path to a CSV or TSV file");

        var delimiter = input["delimiter"]?.Value<string>() ?? ",";
        if (delimiter == "\\t") delimiter = "\t";
        var viewType = input["viewType"]?.Value<string>() ?? "drafting";
        var viewName = input["viewName"]?.Value<string>() ?? $"Table - {Path.GetFileNameWithoutExtension(filePath)}";
        var textSizeMm = input["textSize"]?.Value<double>() ?? 2.0;
        var includeHeaders = input["includeHeaders"]?.Value<bool>() ?? true;

        try
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "File is empty");

            // Parse rows
            var rows = lines.Select(l => l.Split(delimiter[0])).ToList();
            var colCount = rows.Max(r => r.Length);
            // Normalize row lengths
            rows = rows.Select(r =>
            {
                if (r.Length >= colCount) return r;
                var padded = new string[colCount];
                Array.Copy(r, padded, r.Length);
                for (int i = r.Length; i < colCount; i++) padded[i] = "";
                return padded;
            }).ToList();

            var textSizeFt = textSizeMm / MmPerFoot;
            var charWidthFt = textSizeFt * 0.6;
            var cellPaddingFt = textSizeFt * 1.5;
            var rowHeightFt = textSizeFt * 2.5;

            // Calculate column widths
            var colWidths = new double[colCount];
            for (int c = 0; c < colCount; c++)
            {
                var maxLen = rows.Max(r => r[c].Length);
                colWidths[c] = maxLen * charWidthFt + cellPaddingFt;
            }

            using var tx = new Transaction(doc, "RevitCortex: Import Table");
            tx.Start();

            try
            {
                // Create view
                var viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => viewType == "legend"
                        ? vft.ViewFamily == ViewFamily.Legend
                        : vft.ViewFamily == ViewFamily.Drafting);

                if (viewFamilyType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                        $"No {viewType} view family type available");

                View tableView;
                if (viewType == "legend")
                {
                    // Legend views require different creation
                    tableView = ViewDrafting.Create(doc, viewFamilyType.Id);
                }
                else
                {
                    tableView = ViewDrafting.Create(doc, viewFamilyType.Id);
                }
                tableView.Name = viewName;

                // Get text note types
                var defaultTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

                int textNotesCreated = 0;

                for (int r = 0; r < rows.Count; r++)
                {
                    double xOffset = 0;
                    for (int c = 0; c < colCount; c++)
                    {
                        var cellText = rows[r][c].Trim();
                        if (string.IsNullOrEmpty(cellText))
                        {
                            xOffset += colWidths[c];
                            continue;
                        }

                        var position = new XYZ(xOffset, -r * rowHeightFt, 0);

                        var options = new TextNoteOptions(defaultTypeId)
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Left
                        };

                        TextNote.Create(doc, tableView.Id, position, cellText, options);
                        textNotesCreated++;
                        xOffset += colWidths[c];
                    }
                }

                tx.Commit();

                return CortexResult<object>.Ok(new
                {
                    viewId = ToolHelpers.GetElementIdValue(tableView.Id),
                    viewName = tableView.Name,
                    rowCount = rows.Count,
                    columnCount = colCount,
                    textNotesCreated,
                    headerRow = includeHeaders && rows.Count > 0 ? string.Join(delimiter, rows[0]) : ""
                });
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to import table: {ex.Message}");
        }
    }
}

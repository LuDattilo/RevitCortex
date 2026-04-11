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
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Workflows;

/// <summary>
/// Exports element parameters to Excel for external editing, then re-imports.
/// Step 1 (export): Creates an Excel file with element data.
/// Step 2 (import): User edits externally, then uses import_from_excel to re-import.
/// </summary>
public class WorkflowDataRoundtripTool : ICortexTool
{
    public string Name => "workflow_data_roundtrip";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports element parameters to Excel for external editing, then re-imports. Step 1 (export): Creates an Excel file with element data. Step 2 (import): User edits externally, then uses import_from_excel to re-import.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? false;
        var filePath = input["filePath"]?.Value<string>();

        if (string.IsNullOrEmpty(filePath))
            filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"RevitRoundtrip_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        try
        {
            // Collect elements
            IEnumerable<Element> elements;
            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => Utilities.CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToList();
                elements = catIds.SelectMany(catId =>
                    new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType());
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "categories required for data roundtrip export");
            }

            var elemList = elements.Take(5000).ToList();
            if (elemList.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No elements found");

            // Discover writable parameters
            var paramNames = new List<string>();
            foreach (var elem in elemList.Take(50))
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.IsReadOnly) continue;
                    if (parameterNames.Count > 0 && !parameterNames.Contains(p.Definition.Name)) continue;
                    if (!paramNames.Contains(p.Definition.Name))
                        paramNames.Add(p.Definition.Name);
                }
            }

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Data");

            // Headers
            ws.Cell(1, 1).Value = "ElementId";
            ws.Cell(1, 2).Value = "Category";
            ws.Cell(1, 3).Value = "FamilyType";
            for (int i = 0; i < paramNames.Count; i++)
                ws.Cell(1, 4 + i).Value = paramNames[i];

            ws.Range(1, 1, 1, 3 + paramNames.Count).Style.Font.Bold = true;
            ws.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.Range(1, 4, 1, 3 + paramNames.Count).Style.Fill.BackgroundColor = XLColor.LightGreen;

            // Data
            int row = 2;
            foreach (var elem in elemList)
            {
                ws.Cell(row, 1).Value = ToolHelpers.GetElementIdValue(elem.Id);
                ws.Cell(row, 2).Value = elem.Category?.Name ?? "";
                ws.Cell(row, 3).Value = elem.Name;

                for (int i = 0; i < paramNames.Count; i++)
                {
                    var p = elem.LookupParameter(paramNames[i]);
                    if (p != null && p.HasValue)
                    {
                        ws.Cell(row, 4 + i).Value = p.StorageType == StorageType.String
                            ? p.AsString() ?? ""
                            : p.AsValueString() ?? "";
                    }
                }
                row++;
            }

            ws.Columns().AdjustToContents(1, 50);
            workbook.SaveAs(filePath);

            return CortexResult<object>.Ok(new
            {
                filePath,
                elementCount = elemList.Count,
                parameterCount = paramNames.Count,
                parameters = paramNames,
                nextStep = "Edit the Excel file externally, then use import_from_excel to re-import changes"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

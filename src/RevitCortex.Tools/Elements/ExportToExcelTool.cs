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

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Exports elements by category to Excel (.xlsx) with color-coded columns.
/// </summary>
public class ExportToExcelTool : ICortexTool
{
    public string Name => "export_to_excel";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports elements by category to Excel (.xlsx) with color-coded columns.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var legacyCategory = input["category"]?.Value<string>();
        if (categories.Count == 0 && !string.IsNullOrWhiteSpace(legacyCategory))
            categories.Add(legacyCategory!);
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? false;
        var includeElementId = input["includeElementId"]?.Value<bool>() ?? true;
        var filePath = input["filePath"]?.Value<string>()
            ?? input["outputPath"]?.Value<string>();
        var sheetName = input["sheetName"]?.Value<string>() ?? "Export";
        var maxElements = input["maxElements"]?.Value<int>() ?? 10000;

        if (string.IsNullOrEmpty(filePath))
            filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"RevitExport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

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
                elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(e => e.Category != null);
            }

            var elemList = elements.Take(maxElements).ToList();
            if (elemList.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No elements found");

            // Discover parameters (with type element cache)
            var instanceParamNames = new LinkedHashSet();
            var typeParamNames = new LinkedHashSet();
            var typeCache = new Dictionary<ElementId, Element?>();

            foreach (var elem in elemList.Take(100)) // Sample first 100 for discovery
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (parameterNames.Count == 0 || parameterNames.Contains(p.Definition.Name))
                        instanceParamNames.Add(p.Definition.Name);
                }

                if (includeTypeParams)
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        if (!typeCache.TryGetValue(typeId, out var typeElem))
                        {
                            typeElem = doc.GetElement(typeId);
                            typeCache[typeId] = typeElem;
                        }
                        if (typeElem != null)
                        {
                            foreach (Parameter p in typeElem.Parameters)
                            {
                                if (parameterNames.Count == 0 || parameterNames.Contains(p.Definition.Name))
                                    typeParamNames.Add(p.Definition.Name);
                            }
                        }
                    }
                }
            }

            // Remove overlapping type params
            foreach (var name in instanceParamNames.Items)
                typeParamNames.Remove(name);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add(sheetName);

            // Header row
            int col = 1;
            if (includeElementId) { ws.Cell(1, col).Value = "ElementId"; col++; }
            ws.Cell(1, col).Value = "Category"; col++;

            var instStartCol = col;
            foreach (var name in instanceParamNames.Items) { ws.Cell(1, col).Value = name; col++; }
            var instEndCol = col - 1;

            var typeStartCol = col;
            foreach (var name in typeParamNames.Items) { ws.Cell(1, col).Value = $"[Type] {name}"; col++; }
            var typeEndCol = col - 1;

            // Style header
            var headerRange = ws.Range(1, 1, 1, col - 1);
            headerRange.Style.Font.Bold = true;

            // Color-code columns
            if (instEndCol >= instStartCol)
                ws.Range(1, instStartCol, 1, instEndCol).Style.Fill.BackgroundColor = XLColor.LightGreen;
            if (typeEndCol >= typeStartCol)
                ws.Range(1, typeStartCol, 1, typeEndCol).Style.Fill.BackgroundColor = XLColor.LightYellow;

            // Data rows
            int row = 2;
            foreach (var elem in elemList)
            {
                col = 1;
                if (includeElementId) { ws.Cell(row, col).Value = ToolHelpers.GetElementIdValue(elem.Id); col++; }
                ws.Cell(row, col).Value = elem.Category?.Name ?? ""; col++;

                foreach (var name in instanceParamNames.Items)
                {
                    var p = elem.LookupParameter(name);
                    ws.Cell(row, col).Value = p != null ? GetParamDisplayValue(p) : "";
                    col++;
                }

                if (includeTypeParams)
                {
                    var typeId = elem.GetTypeId();
                    if (!typeCache.TryGetValue(typeId, out var typeElem))
                    {
                        typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                        typeCache[typeId] = typeElem;
                    }
                    foreach (var name in typeParamNames.Items)
                    {
                        var p = typeElem?.LookupParameter(name);
                        ws.Cell(row, col).Value = p != null ? GetParamDisplayValue(p) : "";
                        col++;
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
                instanceParameterCount = instanceParamNames.Count,
                typeParameterCount = typeParamNames.Count
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static string GetParamDisplayValue(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F4"),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => ""
        };
    }

    /// <summary>Simple ordered set for maintaining parameter discovery order.</summary>
    private class LinkedHashSet
    {
        private readonly List<string> _items = new();
        private readonly HashSet<string> _set = new();
        public IReadOnlyList<string> Items => _items;
        public int Count => _items.Count;
        public void Add(string item) { if (_set.Add(item)) _items.Add(item); }
        public void Remove(string item) { if (_set.Remove(item)) _items.Remove(item); }
    }
}

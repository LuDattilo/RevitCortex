using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Calculates total area and volume of materials across selected or all elements.
/// Heavy query — can take time on large models.
/// </summary>
[ToolSafety(true, false)]
public class GetMaterialQuantitiesTool : ICortexTool
{
    public string Name => "get_material_quantities";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Calculates total area and volume of materials across selected or all elements. Heavy query — can take time on large models.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var categoryFilters      = input["categoryFilters"]?.ToObject<List<string>>() ?? new List<string>();
        var selectedElementsOnly = input["selectedElementsOnly"]?.Value<bool>() ?? false;
        var maxResults           = input["maxResults"]?.Value<int>() ?? 50;
        var maxElements          = input["maxElements"]?.Value<int>() ?? 20000;

        try
        {
            List<Element> elements;

            if (selectedElementsOnly)
            {
                var uiDoc = new UIDocument(doc);
                var selectedIds = uiDoc.Selection.GetElementIds();
                elements = selectedIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            }
            else
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                if (categoryFilters.Count > 0)
                {
                    var catIds = new List<ElementId>();
                    foreach (var catName in categoryFilters)
                    {
                        var catId = CategoryResolver.ResolveToId(doc, catName);
                        if (catId != null && catId != ElementId.InvalidElementId)
                            catIds.Add(catId);
                    }
                    if (catIds.Count > 0)
                        collector = collector.WherePasses(new ElementMulticategoryFilter(catIds, false));
                }

                elements = collector.ToList();
            }

            // GetMaterialArea/GetMaterialVolume are geometry-backed and run on the
            // Revit UI thread: an unbounded full-model pass freezes Revit far past
            // the 120s dispatcher timeout, and partial sums would be silently wrong.
            // Over-cap is therefore a structured failure, not a truncated result.
            if (elements.Count > maxElements)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"{elements.Count} elements match, above the cap of {maxElements}. Processing them all would freeze Revit's UI thread.",
                    suggestion: "Narrow the query with categoryFilters or selectedElementsOnly, or raise maxElements explicitly if you accept the wait.");

            // Accumulate material quantities
            var materialData = new Dictionary<ElementId, (string name, string matClass, double area, double volume, int elementCount, List<long> elementIds)>();

            // Budget kept under the 120s dispatcher timeout so the caller receives
            // this structured error instead of the generic dispatcher Timeout while
            // Revit's UI thread keeps grinding.
            const int TimeBudgetMs = 90000;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var processed = 0;

            foreach (var elem in elements)
            {
                if ((++processed % 500) == 0 && elapsed.ElapsedMilliseconds > TimeBudgetMs)
                    return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                        $"Time budget exceeded after {processed}/{elements.Count} elements; partial totals would be misleading and were discarded.",
                        suggestion: "Narrow the query with categoryFilters or selectedElementsOnly.");

                ICollection<ElementId> matIds;
                try { matIds = elem.GetMaterialIds(false); }
                catch { continue; }

                foreach (var matId in matIds)
                {
                    double area = 0, volume = 0;
                    try { area = elem.GetMaterialArea(matId, false); } catch { }
                    try { volume = elem.GetMaterialVolume(matId); } catch { }

                    if (!materialData.ContainsKey(matId))
                    {
                        var mat = doc.GetElement(matId) as Material;
                        materialData[matId] = (
                            mat?.Name ?? "Unknown",
                            mat?.MaterialClass ?? "",
                            0, 0, 0,
                            new List<long>()
                        );
                    }

                    var entry = materialData[matId];
                    long elemIdLong;
#if REVIT2024_OR_GREATER
                    elemIdLong = elem.Id.Value;
#else
                    elemIdLong = (long)elem.Id.IntegerValue;
#endif
                    materialData[matId] = (
                        entry.name, entry.matClass,
                        entry.area + area,
                        entry.volume + volume,
                        entry.elementCount + 1,
                        entry.elementIds
                    );
                    entry.elementIds.Add(elemIdLong);
                }
            }

            var totalCount = materialData.Count;
            var truncated = totalCount > maxResults;

            var materials = materialData
                .OrderByDescending(kv => kv.Value.volume)
                .Take(maxResults)
                .Select(kv => new
                {
#if REVIT2024_OR_GREATER
                    materialId = kv.Key.Value,
#else
                    materialId = (long)kv.Key.IntegerValue,
#endif
                    materialName  = kv.Value.name,
                    materialClass = kv.Value.matClass,
                    area          = Math.Round(kv.Value.area, 4),
                    volume        = Math.Round(kv.Value.volume, 4),
                    elementCount  = kv.Value.elementCount
                }).ToList();

            return CortexResult<object>.Ok(new
            {
                totalMaterials = materials.Count,
                totalCount,
                truncated,
                totalArea  = Math.Round(materialData.Values.Sum(v => v.area), 4),
                totalVolume = Math.Round(materialData.Values.Sum(v => v.volume), 4),
                materials
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get material quantities: {ex.Message}");
        }
    }
}

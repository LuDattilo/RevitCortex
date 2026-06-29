using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Lists IFC-imported elements that pass the rebuildability confidence threshold.
/// Works best after ifc_analyze_rebuildability has been called (uses cached results).
/// </summary>
[ToolSafety(true, false)]
public class IfcListRebuildCandidatesTool : ICortexTool
{
    public string Name => "ifc_list_rebuild_candidates";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List IFC elements that can be rebuilt as native Revit elements";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var minConfidence = input["minConfidence"]?.Value<double>() ?? 0.5;
        var maxElements = input["maxElements"]?.Value<int>() ?? 100;

        // No cached results — run a fresh lightweight analysis
        BuiltInCategory? builtInCat = null;
        if (!string.IsNullOrWhiteSpace(categoryFilter))
            builtInCat = CategoryResolver.Resolve(categoryFilter!);

        var directShapes = IfcGeometryHelper.GetDirectShapes(doc!, builtInCat);
        var candidates = new List<object>();

        foreach (var ds in directShapes.Take(maxElements * 2)) // scan more to find enough
        {
            var catName = ds.Category?.Name ?? "Unknown";
            var geomType = IfcGeometryHelper.DetectGeometryType(ds);

            double confidence = EstimateConfidence(ds, catName, geomType);
            if (confidence < minConfidence) continue;

            candidates.Add(new
            {
                elementId = ToolHelpers.GetElementIdValue(ds.Id),
                name = ds.Name,
                category = catName,
                geometryType = geomType,
                rebuildConfidence = Math.Round(confidence, 2),
            });

            if (candidates.Count >= maxElements) break;
        }

        return CortexResult<object>.Ok(new
        {
            count = candidates.Count,
            minConfidence,
            source = "fresh_scan",
            candidates,
        });
    }

    private static double EstimateConfidence(DirectShape ds, string category, string geomType)
    {
        if (geomType == "mesh" || geomType == "unknown") return 0.0;

        var catLower = category.ToLowerInvariant();
        double baseScore = geomType == "extrusion" ? 0.8 : (geomType == "sweep" ? 0.5 : 0.3);

        if (catLower.Contains("wall") || catLower.Contains("mur"))
            return IfcGeometryHelper.ExtractWallProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("floor") || catLower.Contains("slab") || catLower.Contains("paviment"))
            return baseScore;
        if (catLower.Contains("column") || catLower.Contains("pilastr"))
            return IfcGeometryHelper.ExtractColumnProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("beam") || catLower.Contains("trave") || catLower.Contains("framing"))
            return IfcGeometryHelper.ExtractBeamProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("roof") || catLower.Contains("tetto"))
            return baseScore - 0.1;
        if (catLower.Contains("door") || catLower.Contains("window"))
            return 0.6;

        return 0.1;
    }
}

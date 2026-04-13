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
/// Scans imported IFC elements (DirectShapes), classifies each as rebuildable or not,
/// and returns confidence scores per element.
/// </summary>
public class IfcAnalyzeRebuildabilityTool : ICortexTool
{
    public string Name => "ifc_analyze_rebuildability";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Analyze IFC-imported elements for native Revit reconstruction feasibility";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var maxElements = input["maxElements"]?.Value<int>() ?? 200;

        BuiltInCategory? builtInCat = null;
        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            if (Enum.TryParse<BuiltInCategory>(categoryFilter, out var parsed))
                builtInCat = parsed;
        }

        var directShapes = IfcGeometryHelper.GetDirectShapes(doc!, builtInCat);
        if (directShapes.Count == 0)
            return CortexResult<object>.Ok(new
            {
                message = "No IFC DirectShape elements found",
                totalAnalyzed = 0,
                results = Array.Empty<object>(),
            });

        var results = new List<object>();
        var categorySummary = new Dictionary<string, int>();
        int rebuildableCount = 0;

        foreach (var ds in directShapes.Take(maxElements))
        {
            var categoryName = ds.Category?.Name ?? "Unknown";
            var geomType = IfcGeometryHelper.DetectGeometryType(ds);
            var ifcEntity = IfcGeometryHelper.GetIfcParameter(ds, "IfcExportAs")
                         ?? IfcGeometryHelper.GetIfcParameter(ds, "IfcType")
                         ?? "";

            var (strategy, confidence) = DetermineStrategy(ds, categoryName, geomType);

            if (confidence >= 0.5) rebuildableCount++;

            if (!categorySummary.ContainsKey(categoryName))
                categorySummary[categoryName] = 0;
            categorySummary[categoryName]++;

            results.Add(new
            {
                elementId = ToolHelpers.GetElementIdValue(ds.Id),
                name = ds.Name,
                category = categoryName,
                ifcEntity,
                geometryType = geomType,
                rebuildStrategy = strategy,
                rebuildConfidence = Math.Round(confidence, 2),
            });
        }

        // Store analysis results in session for use by other IFC tools
        session.Store.Set("ifc_analysis_results", results);

        return CortexResult<object>.Ok(new
        {
            totalAnalyzed = results.Count,
            totalInDocument = directShapes.Count,
            rebuildableCount,
            categorySummary,
            results,
        });
    }

    private static (string strategy, double confidence) DetermineStrategy(
        DirectShape ds, string category, string geomType)
    {
        // Mesh and unknown geometry cannot be rebuilt
        if (geomType == "mesh" || geomType == "unknown")
            return ("none", 0.0);

        // Match by category name patterns (works across locales via OST mapping)
        var catLower = category.ToLowerInvariant();

        if (catLower.Contains("wall") || catLower.Contains("mur"))
        {
            var profile = IfcGeometryHelper.ExtractWallProfile(ds);
            if (profile != null)
                return ("Wall.Create", geomType == "extrusion" ? 0.9 : 0.6);
            return ("Wall.Create (complex)", 0.3);
        }

        if (catLower.Contains("floor") || catLower.Contains("slab") ||
            catLower.Contains("paviment") || catLower.Contains("sol"))
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count > 0 && IfcGeometryHelper.ExtractBottomFootprint(solids[0]) != null)
                return ("Floor.Create", geomType == "extrusion" ? 0.85 : 0.5);
            return ("Floor.Create (complex)", 0.25);
        }

        if (catLower.Contains("roof") || catLower.Contains("tetto") || catLower.Contains("toit"))
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count > 0 && IfcGeometryHelper.ExtractBottomFootprint(solids[0]) != null)
                return ("NewFootPrintRoof", 0.7);
            return ("NewFootPrintRoof (complex)", 0.2);
        }

        if (catLower.Contains("column") || catLower.Contains("pilastr") || catLower.Contains("poteau"))
        {
            var profile = IfcGeometryHelper.ExtractColumnProfile(ds);
            return profile != null
                ? ("NewFamilyInstance.Column", 0.85)
                : ("NewFamilyInstance.Column (complex)", 0.3);
        }

        if (catLower.Contains("beam") || catLower.Contains("trave") ||
            catLower.Contains("telaio") || catLower.Contains("framing") || catLower.Contains("ossature"))
        {
            var profile = IfcGeometryHelper.ExtractBeamProfile(ds);
            return profile != null
                ? ("NewFamilyInstance.Beam", 0.85)
                : ("NewFamilyInstance.Beam (complex)", 0.3);
        }

        if (catLower.Contains("door") || catLower.Contains("port"))
            return ("FamilyInstance.Door", 0.6);

        if (catLower.Contains("window") || catLower.Contains("finestr") || catLower.Contains("fenetr"))
            return ("FamilyInstance.Window", 0.6);

        return ("none", 0.1);
    }
}

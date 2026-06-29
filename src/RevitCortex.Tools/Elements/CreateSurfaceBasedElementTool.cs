using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates one or more surface-based elements: floors, ceilings, or roofs.
/// Mirrors the fork's CreateSurfaceElementEventHandler logic.
/// </summary>
[ToolSafety(false, false)]
public class CreateSurfaceBasedElementTool : ICortexTool
{
    public string Name => "create_surface_based_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more surface-based elements: floors, ceilings, or roofs. Mirrors the fork's CreateSurfaceElementEventHandler logic.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var dataToken = input["data"];
        if (dataToken == null || dataToken.Type != JTokenType.Array)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "data array is required",
                suggestion: "Provide {\"data\": [{\"category\": \"OST_Floors\", \"boundary\": {\"outerLoop\": [{\"p0\":{...},\"p1\":{...}}]}, ...}]}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var createdIds = new List<long>();
        var warnings   = new List<string>();

        foreach (var item in dataToken)
        {
            try
            {
                ProcessSurfaceElement(doc, (JObject)item, createdIds, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to create element: {ex.Message}");
            }
        }

        var message = $"Successfully created {createdIds.Count} element(s).";
        if (warnings.Count > 0)
            message += "\n\nWarnings:\n  - " + string.Join("\n  - ", warnings);

        return CortexResult<object>.Ok(new
        {
            message,
            createdElementIds = createdIds
        });
    }

    private static void ProcessSurfaceElement(Document doc, JObject item, List<long> createdIds, List<string> warnings)
    {
        // Parse category
        var categoryStr = item["category"]?.Value<string>() ?? "";
        if (!Enum.TryParse(
                categoryStr.Replace(".", "").Replace("BuiltInCategory", ""),
                true,
                out BuiltInCategory builtInCategory) ||
            builtInCategory == BuiltInCategory.INVALID)
        {
            warnings.Add($"Invalid or unrecognized category: '{categoryStr}'. Expected OST_Floors, OST_Ceilings, or OST_Roofs.");
            return;
        }

        // Parse boundary
        var boundaryToken = item["boundary"];
        if (boundaryToken == null)
        {
            warnings.Add("boundary is required");
            return;
        }
        var outerLoopToken = boundaryToken["outerLoop"];
        if (outerLoopToken == null || outerLoopToken.Type != JTokenType.Array)
        {
            warnings.Add("boundary.outerLoop array is required");
            return;
        }
        var outerLoopSegments = outerLoopToken.ToArray();
        if (outerLoopSegments.Length < 3)
        {
            warnings.Add("boundary.outerLoop must have at least 3 line segments");
            return;
        }

        // Parse optional parameters
        var requestedTypeId = item["typeId"]?.Value<long?>() ?? -1;
        var baseLevelMm     = item["baseLevel"]?.Value<double?>() ?? 0.0;
        var baseOffsetMm    = item["baseOffset"]?.Value<double?>() ?? 0.0;

        var baseLevelFt = baseLevelMm / MmPerFoot;
        var baseLevel   = FindNearestLevel(doc, baseLevelFt);
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }
        var baseOffset = (baseOffsetMm + baseLevelMm) / MmPerFoot - baseLevel.Elevation;

        // Build curve list from boundary segments
        var curves = new List<Curve>();
        foreach (var segment in outerLoopSegments)
        {
            var p0 = ParseXYZ(segment["p0"]!);
            var p1 = ParseXYZ(segment["p1"]!);
            if (p0.IsAlmostEqualTo(p1))
            {
                warnings.Add("Skipped zero-length boundary segment");
                continue;
            }
            curves.Add(Line.CreateBound(p0, p1));
        }
        if (curves.Count < 3)
        {
            warnings.Add("Not enough valid boundary segments (minimum 3 required after filtering)");
            return;
        }

        // Resolve type by category
        FloorType?   floorType   = null;
        RoofType?    roofType    = null;
        CeilingType? ceilingType = null;

        if (requestedTypeId > 0)
        {
#if REVIT2024_OR_GREATER
            var typeElemId = new ElementId(requestedTypeId);
#else
            var typeElemId = new ElementId((int)requestedTypeId);
#endif
            var typeElem = doc.GetElement(typeElemId);
            if (typeElem is FloorType ft)
            {
                floorType = ft;
#if REVIT2024_OR_GREATER
                builtInCategory = (BuiltInCategory)floorType.Category.Id.Value;
#else
                builtInCategory = (BuiltInCategory)floorType.Category.Id.IntegerValue;
#endif
            }
            else if (typeElem is RoofType rt)
            {
                roofType = rt;
#if REVIT2024_OR_GREATER
                builtInCategory = (BuiltInCategory)roofType.Category.Id.Value;
#else
                builtInCategory = (BuiltInCategory)roofType.Category.Id.IntegerValue;
#endif
            }
            else if (typeElem is CeilingType ct)
            {
                ceilingType = ct;
#if REVIT2024_OR_GREATER
                builtInCategory = (BuiltInCategory)ceilingType.Category.Id.Value;
#else
                builtInCategory = (BuiltInCategory)ceilingType.Category.Id.IntegerValue;
#endif
            }
        }

        // Type fallback per category
        switch (builtInCategory)
        {
            case BuiltInCategory.OST_Floors:
                if (floorType == null)
                {
                    floorType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .OfCategory(BuiltInCategory.OST_Floors)
                        .Cast<FloorType>()
                        .FirstOrDefault();
                    if (floorType == null)
                    {
                        warnings.Add("No floor types available in project.");
                        return;
                    }
                    if (requestedTypeId > 0)
                        warnings.Add($"Requested floor typeId {requestedTypeId} not found. Defaulted to '{floorType.Name}' (ID: {ToolHelpers.GetElementIdValue(floorType.Id)})");
                }
                break;

            case BuiltInCategory.OST_Roofs:
                if (roofType == null)
                {
                    roofType = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofType))
                        .OfCategory(BuiltInCategory.OST_Roofs)
                        .Cast<RoofType>()
                        .FirstOrDefault();
                    if (roofType == null)
                    {
                        warnings.Add("No roof types available in project.");
                        return;
                    }
                    if (requestedTypeId > 0)
                        warnings.Add($"Requested roof typeId {requestedTypeId} not found. Defaulted to '{roofType.Name}' (ID: {ToolHelpers.GetElementIdValue(roofType.Id)})");
                }
                break;

            case BuiltInCategory.OST_Ceilings:
                if (ceilingType == null)
                {
                    ceilingType = new FilteredElementCollector(doc)
                        .OfClass(typeof(CeilingType))
                        .OfCategory(BuiltInCategory.OST_Ceilings)
                        .Cast<CeilingType>()
                        .FirstOrDefault();
                    if (ceilingType == null)
                    {
                        warnings.Add("No ceiling types available in project.");
                        return;
                    }
                    if (requestedTypeId > 0)
                        warnings.Add($"Requested ceiling typeId {requestedTypeId} not found. Defaulted to '{ceilingType.Name}' (ID: {ToolHelpers.GetElementIdValue(ceilingType.Id)})");
                }
                break;

            default:
                warnings.Add($"Unsupported category '{builtInCategory}'. Supported: OST_Floors, OST_Ceilings, OST_Roofs.");
                return;
        }

        using var tx = new Transaction(doc, "RevitCortex: Create Surface Element");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            switch (builtInCategory)
            {
                case BuiltInCategory.OST_Floors:
                {
                    var curveLoop = CurveLoop.Create(curves);
                    var floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType!.Id, baseLevel.Id);
                    if (floor != null)
                    {
                        var offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                        offsetParam?.Set(baseOffset);
                        createdIds.Add(ToolHelpers.GetElementIdValue(floor.Id));
                    }
                    break;
                }

                case BuiltInCategory.OST_Roofs:
                {
                    var roofCurves = new CurveArray();
                    foreach (var c in curves)
                        roofCurves.Append(c);

                    var modelCurves = new ModelCurveArray();
                    var roof = doc.Create.NewFootPrintRoof(roofCurves, baseLevel, roofType!, out modelCurves);
                    if (roof != null)
                    {
                        // Flat roof — disable slopes on all edges
                        foreach (ModelCurve mc in modelCurves)
                            roof.set_DefinesSlope(mc, false);

                        var offsetParam = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                        offsetParam?.Set(baseOffset);
                        createdIds.Add(ToolHelpers.GetElementIdValue(roof.Id));
                    }
                    break;
                }

                case BuiltInCategory.OST_Ceilings:
                {
                    var curveLoop = CurveLoop.Create(curves);
                    var ceiling = Ceiling.Create(doc, new List<CurveLoop> { curveLoop }, ceilingType!.Id, baseLevel.Id);
                    if (ceiling != null)
                    {
                        var offsetParam = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                        offsetParam?.Set(baseOffset);
                        createdIds.Add(ToolHelpers.GetElementIdValue(ceiling.Id));
                    }
                    break;
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                warnings.Add($"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Level? FindNearestLevel(Document doc, double elevationFt)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => Math.Abs(l.Elevation - elevationFt))
            .FirstOrDefault();
    }

    private static XYZ ParseXYZ(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }
}

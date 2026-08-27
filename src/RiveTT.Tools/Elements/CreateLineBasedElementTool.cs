using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates one or more line-based elements (walls, beams, structural framing, etc.).
/// Mirrors the fork's CreateLineElementEventHandler logic.
/// </summary>
[ToolSafety(false, false)]
public class CreateLineBasedElementTool : ICortexTool
{
    public string Name => "create_line_based_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more line-based elements (walls, beams, structural framing, etc.). Each locationLine has p0 and p1 (mm); add an optional pMid point to create a curved (arc) wall or beam.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var dataToken = input["data"];
        if (dataToken == null || dataToken.Type != JTokenType.Array)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "data array is required",
                suggestion: "Provide {\"data\": [{\"category\": \"OST_Walls\", \"locationLine\": {\"p0\":{...}, \"p1\":{...}}, ...}]}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var createdIds = new List<long>();
        var warnings = new List<string>();
        var details = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);

        foreach (var item in dataToken)
        {
            try
            {
                ProcessLineElement(doc, (JObject)item, createdIds, warnings, details, dryRun);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to create element: {ex.Message}");
            }
        }

        var message = dryRun
            ? $"Previewed {details.Count} line-based element specification(s)."
            : $"Successfully created {createdIds.Count} element(s).";
        if (warnings.Count > 0)
            message += "\n\nWarnings:\n  - " + string.Join("\n  - ", warnings);

        return CortexResult<object>.Ok(new
        {
            message,
            dryRun,
            processed = dataToken.Count(),
            created = createdIds.Count,
            skipped = warnings.Count,
            createdElementIds = createdIds,
            details
        });
    }

    private static void ProcessLineElement(Document doc, JObject item, List<long> createdIds,
        List<string> warnings, List<object> details, bool dryRun)
    {
        // Parse category
        var categoryStr = item["category"]?.Value<string>() ?? "";
        if (!Enum.TryParse(categoryStr.Replace(".", ""), true, out BuiltInCategory builtInCategory) ||
            builtInCategory == BuiltInCategory.INVALID)
        {
            warnings.Add($"Invalid or unrecognized category: '{categoryStr}'");
            return;
        }

        // Parse locationLine
        var locationLineToken = item["locationLine"];
        if (locationLineToken == null)
        {
            warnings.Add("locationLine is required");
            return;
        }
        var p0 = ParseXYZ(locationLineToken["p0"]!);
        var p1 = ParseXYZ(locationLineToken["p1"]!);

        if (p0.IsAlmostEqualTo(p1))
        {
            warnings.Add("locationLine start and end points are too close — line has zero length");
            return;
        }

        // Optional mid point (pMid) turns the location into an arc (curved wall/beam).
        var pMidToken = locationLineToken["pMid"];
        Curve locationLine;
        try
        {
            if (pMidToken != null && pMidToken.Type != JTokenType.Null)
            {
                var pMid = ParseXYZ(pMidToken);
                locationLine = Arc.Create(p0, p1, pMid);
            }
            else
            {
                locationLine = Line.CreateBound(p0, p1);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Cannot create location curve: {ex.Message}");
            return;
        }

        // Parse optional parameters
        var requestedTypeId = item["typeId"]?.Value<long?>() ?? -1;
        var heightMm       = item["height"]?.Value<double?>() ?? 3000.0;
        var baseLevelMm    = item["baseLevel"]?.Value<double?>() ?? 0.0;
        var baseOffsetMm   = item["baseOffset"]?.Value<double?>() ?? 0.0;
        var baseLevelId    = item["baseLevelId"]?.Value<long?>() ?? -1;
        var topLevelId     = item["topLevelId"]?.Value<long?>() ?? -1;
        var topOffsetMm    = item["topOffset"]?.Value<double?>() ?? 0.0;
        var strictType     = item["strictType"]?.Value<bool?>() ?? false;

        var baseLevelFt = baseLevelMm / MmPerFoot;
        var heightFt    = heightMm / MmPerFoot;

        // Resolve nearest level
        var baseLevel = baseLevelId > 0
            ? doc.GetElement(ToolHelpers.ToElementId(baseLevelId)) as Level
            : FindNearestLevel(doc, baseLevelFt);
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }
        // With an explicit level ID, baseOffset is relative to that level.
        // The legacy elevation-based schema keeps its absolute-Z conversion.
        var baseOffset = baseLevelId > 0
            ? baseOffsetMm / MmPerFoot
            : (baseOffsetMm + baseLevelMm) / MmPerFoot - baseLevel.Elevation;

        // Resolve type
        FamilySymbol? symbol = null;
        WallType?     wallType = null;

        if (requestedTypeId > 0)
        {
            var typeElemId = new ElementId(requestedTypeId);
            var typeElem = doc.GetElement(typeElemId);
            if (typeElem is FamilySymbol fs)
            {
                symbol = fs;
                builtInCategory = (BuiltInCategory)symbol.Category.Id.Value;
            }
            else if (typeElem is WallType wt)
            {
                wallType = wt;
                builtInCategory = (BuiltInCategory)wallType.Category.Id.Value;
            }
        }

        switch (builtInCategory)
        {
            case BuiltInCategory.OST_Walls:
                if (wallType == null)
                {
                    if (strictType)
                    {
                        warnings.Add($"A valid wall typeId is required; {requestedTypeId} was not found.");
                        return;
                    }
                    wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault(wt => wt.Kind == WallKind.Basic);
                    if (wallType == null)
                    {
                        warnings.Add("No wall types available in project.");
                        return;
                    }
                    if (requestedTypeId > 0)
                        warnings.Add($"Requested wall typeId {requestedTypeId} not found. Defaulted to '{wallType.Name}' (ID: {ToolHelpers.GetElementIdValue(wallType.Id)})");
                }

                if (dryRun)
                {
                    details.Add(new
                    {
                        kind = "wall",
                        wallTypeId = ToolHelpers.GetElementIdValue(wallType.Id),
                        wallTypeName = wallType.Name,
                        baseLevelId = ToolHelpers.GetElementIdValue(baseLevel.Id),
                        baseLevelName = baseLevel.Name,
                        baseLevelElevationMm = baseLevel.Elevation * MmPerFoot,
                        requestedBaseOffsetMm = baseOffsetMm,
                        resultingBaseElevationMm = (baseLevel.Elevation + baseOffset) * MmPerFoot,
                        topLevelId = topLevelId > 0 ? (long?)topLevelId : null,
                        requestedTopOffsetMm = topOffsetMm,
                        unconnectedHeightMm = topLevelId > 0 ? (double?)null : heightMm
                    });
                    return;
                }

                using (var tx = new Transaction(doc, "RiveTT: Create Wall"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    try
                    {
                        var wall = Wall.Create(doc, locationLine, wallType.Id, baseLevel.Id, heightFt, baseOffset, false, false);
                        if (wall != null)
                        {
                            if (topLevelId > 0)
                            {
                                var topLevel = doc.GetElement(ToolHelpers.ToElementId(topLevelId)) as Level;
                                if (topLevel == null)
                                {
                                    warnings.Add($"topLevelId {topLevelId} is not a valid level; wall kept unconnected.");
                                }
                                else
                                {
                                    var topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                                    var topOffset = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                                    if (topConstraint != null && !topConstraint.IsReadOnly)
                                        topConstraint.Set(topLevel.Id);
                                    if (topOffset != null && !topOffset.IsReadOnly)
                                        topOffset.Set(topOffsetMm / MmPerFoot);
                                }
                            }
                        }
                        if (tx.Commit() != TransactionStatus.Committed)
                            warnings.Add($"Revit rolled back the wall transaction: {TransactionFailureHandling.Describe(txFailures)}");
                        else if (wall != null)
                        {
                            var actualBaseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? baseOffset;
                            var actualTopOffset = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0;
                            var actualUnconnectedHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();
                            var actualTopLevel = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();
                            var wallId = ToolHelpers.GetElementIdValue(wall.Id);
                            createdIds.Add(wallId);
                            details.Add(new
                            {
                                kind = "wall",
                                elementId = wallId,
                                baseLevelId = ToolHelpers.GetElementIdValue(baseLevel.Id),
                                baseElevationMm = (baseLevel.Elevation + actualBaseOffset) * MmPerFoot,
                                baseOffsetMm = actualBaseOffset * MmPerFoot,
                                topLevelId = ToolHelpers.GetElementIdValue(actualTopLevel),
                                topOffsetMm = actualTopOffset * MmPerFoot,
                                unconnectedHeightMm = actualUnconnectedHeight * MmPerFoot,
                                coordinates = "absolute_project_coordinates_mm",
                                offsets = "relative_to_constraint_level_mm"
                            });
                        }
                    }
                    catch
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        throw;
                    }
                }
                break;

            default:
                // Generic line-based family instance (structural framing, etc.)
                if (symbol == null)
                {
                    if (strictType)
                    {
                        warnings.Add($"A valid typeId is required; {requestedTypeId} was not found.");
                        return;
                    }
                    symbol = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(builtInCategory)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(s => s.IsActive)
                        ?? new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(builtInCategory)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault();

                    if (symbol == null)
                    {
                        warnings.Add($"No family types available for category {builtInCategory}.");
                        return;
                    }
                    if (requestedTypeId > 0)
                        warnings.Add($"Requested typeId {requestedTypeId} not found. Defaulted to '{symbol.FamilyName}: {symbol.Name}' (ID: {ToolHelpers.GetElementIdValue(symbol.Id)})");
                }

                if (dryRun)
                {
                    details.Add(new
                    {
                        kind = "line_based_family_instance",
                        typeId = ToolHelpers.GetElementIdValue(symbol.Id),
                        familyName = symbol.FamilyName,
                        typeName = symbol.Name,
                        baseLevelId = ToolHelpers.GetElementIdValue(baseLevel.Id),
                        baseOffsetMm = baseOffset * MmPerFoot
                    });
                    return;
                }

                using (var tx2 = new Transaction(doc, "RiveTT: Create Line-Based Element"))
                {
                    var tx2Failures = TransactionFailureHandling.SuppressWarnings(tx2);
                    tx2.Start();
                    try
                    {
                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                            doc.Regenerate();
                        }

                        // Determine StructuralType based on category
                        var structuralType = builtInCategory == BuiltInCategory.OST_StructuralFraming
                            ? Autodesk.Revit.DB.Structure.StructuralType.Beam
                            : Autodesk.Revit.DB.Structure.StructuralType.NonStructural;

                        var instance = doc.Create.NewFamilyInstance(locationLine, symbol, baseLevel, structuralType);
                        if (instance != null)
                        {
                            // Apply base offset (start+end elevation for beams, else free-host offset)
                            if (Math.Abs(baseOffset) > 1e-9)
                            {
                                var startElev = instance.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
                                var endElev = instance.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);
                                if (startElev != null && !startElev.IsReadOnly && endElev != null && !endElev.IsReadOnly)
                                {
                                    startElev.Set(baseOffset);
                                    endElev.Set(baseOffset);
                                }
                                else
                                {
                                    var freeOffset = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                                    if (freeOffset != null && !freeOffset.IsReadOnly)
                                        freeOffset.Set(baseOffset);
                                }
                            }
                        }
                        if (tx2.Commit() != TransactionStatus.Committed)
                            warnings.Add($"Revit rolled back the line-based element transaction: {TransactionFailureHandling.Describe(tx2Failures)}");
                        else if (instance != null)
                        {
                            var instanceId = ToolHelpers.GetElementIdValue(instance.Id);
                            createdIds.Add(instanceId);
                            details.Add(new { kind = "line_based_family_instance", elementId = instanceId });
                        }
                    }
                    catch
                    {
                        if (tx2.GetStatus() == TransactionStatus.Started) tx2.RollBack();
                        throw;
                    }
                }
                break;
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

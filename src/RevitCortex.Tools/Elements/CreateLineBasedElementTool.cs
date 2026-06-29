using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

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
    private const double MmPerFoot = 304.8;

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

        foreach (var item in dataToken)
        {
            try
            {
                ProcessLineElement(doc, (JObject)item, createdIds, warnings);
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

    private static void ProcessLineElement(Document doc, JObject item, List<long> createdIds, List<string> warnings)
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

        var baseLevelFt = baseLevelMm / MmPerFoot;
        var heightFt    = heightMm / MmPerFoot;

        // Resolve nearest level
        var baseLevel = FindNearestLevel(doc, baseLevelFt);
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }
        var baseOffset = (baseOffsetMm + baseLevelMm) / MmPerFoot - baseLevel.Elevation;

        // Resolve type
        FamilySymbol? symbol = null;
        WallType?     wallType = null;

        if (requestedTypeId > 0)
        {
#if REVIT2024_OR_GREATER
            var typeElemId = new ElementId(requestedTypeId);
#else
            var typeElemId = new ElementId((int)requestedTypeId);
#endif
            var typeElem = doc.GetElement(typeElemId);
            if (typeElem is FamilySymbol fs)
            {
                symbol = fs;
#if REVIT2024_OR_GREATER
                builtInCategory = (BuiltInCategory)symbol.Category.Id.Value;
#else
                builtInCategory = (BuiltInCategory)symbol.Category.Id.IntegerValue;
#endif
            }
            else if (typeElem is WallType wt)
            {
                wallType = wt;
#if REVIT2024_OR_GREATER
                builtInCategory = (BuiltInCategory)wallType.Category.Id.Value;
#else
                builtInCategory = (BuiltInCategory)wallType.Category.Id.IntegerValue;
#endif
            }
        }

        switch (builtInCategory)
        {
            case BuiltInCategory.OST_Walls:
                if (wallType == null)
                {
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

                using (var tx = new Transaction(doc, "RevitCortex: Create Wall"))
                {
                    tx.Start();
                    try
                    {
                        var wall = Wall.Create(doc, locationLine, wallType.Id, baseLevel.Id, heightFt, baseOffset, false, false);
                        if (wall != null)
                            createdIds.Add(ToolHelpers.GetElementIdValue(wall.Id));
                        tx.Commit();
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

                using (var tx2 = new Transaction(doc, "RevitCortex: Create Line-Based Element"))
                {
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
                            createdIds.Add(ToolHelpers.GetElementIdValue(instance.Id));
                        }
                        tx2.Commit();
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

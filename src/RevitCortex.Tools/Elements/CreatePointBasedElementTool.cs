using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates one or more point-based family instances (furniture, doors, windows, columns, etc.).
/// Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement,
/// door/window facing auto-detection, and rotation support.
/// </summary>
public class CreatePointBasedElementTool : ICortexTool
{
    public string Name => "create_point_based_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more point-based family instances (furniture, doors, windows, columns, etc.). Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement, door/window facing auto-detection, and rotation support.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var dataToken = input["data"];
        if (dataToken == null || dataToken.Type != JTokenType.Array)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "data array is required",
                suggestion: "Provide {\"data\": [{\"typeId\": 123, \"locationPoint\": {\"x\":0,\"y\":0,\"z\":0}, ...}]}");

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
                ProcessPointElement(doc, (JObject)item, createdIds, warnings);
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

    private static void ProcessPointElement(Document doc, JObject item, List<long> createdIds, List<string> warnings)
    {
        // Parse category (optional — inferred from typeId)
        var categoryStr = item["category"]?.Value<string>() ?? "";
        BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
        if (!string.IsNullOrWhiteSpace(categoryStr))
            Enum.TryParse(categoryStr.Replace(".", ""), true, out builtInCategory);

        // Parse locationPoint
        var locationPtToken = item["locationPoint"];
        if (locationPtToken == null)
        {
            warnings.Add("locationPoint is required");
            return;
        }
        var locationPoint = ParseXYZ(locationPtToken);

        // Parse optional parameters
        var requestedTypeId = item["typeId"]?.Value<long?>() ?? -1;
        var baseLevelMm     = item["baseLevel"]?.Value<double?>() ?? 0.0;
        var baseOffsetMm    = item["baseOffset"]?.Value<double?>() ?? 0.0;
        var rotationDeg     = item["rotation"]?.Value<double?>() ?? 0.0;
        var hostWallId      = item["hostWallId"]?.Value<long?>() ?? -1;
        var facingFlipped   = item["facingFlipped"]?.Value<bool?>() ?? false;

        // Resolve levels
        var baseLevelFt = baseLevelMm / MmPerFoot;
        var baseLevel   = FindNearestLevel(doc, baseLevelFt);
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }

        // Resolve family symbol
        FamilySymbol? symbol = null;
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
        }

        if (builtInCategory == BuiltInCategory.INVALID)
        {
            warnings.Add($"Could not determine category — provide 'category' field or a valid 'typeId'");
            return;
        }

        if (symbol == null)
        {
            // Fallback: prefer active symbol
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

        using var tx = new Transaction(doc, "RevitCortex: Create Point-Based Element");
        tx.Start();
        try
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            FamilyInstance? instance = null;

            // Resolve explicit host wall
            Wall? hostWall = null;
            if (hostWallId > 0)
            {
#if REVIT2024_OR_GREATER
                var hostElemId = new ElementId(hostWallId);
#else
                var hostElemId = new ElementId((int)hostWallId);
#endif
                var hostElem = doc.GetElement(hostElemId);
                if (hostElem is Wall w)
                    hostWall = w;
                else
                    warnings.Add($"Requested hostWallId {hostWallId} is not a valid wall. Using auto-detection.");
            }

            // Create instance
            if (hostWall != null)
            {
                // Wall-hosted (doors, windows)
                instance = doc.Create.NewFamilyInstance(locationPoint, symbol, hostWall, baseLevel, StructuralType.NonStructural);
            }
            else
            {
                instance = doc.Create.NewFamilyInstance(locationPoint, symbol, baseLevel, StructuralType.NonStructural);
            }

            if (instance != null)
            {
                // Handle door/window facing
                if (builtInCategory == BuiltInCategory.OST_Doors ||
                    builtInCategory == BuiltInCategory.OST_Windows)
                {
                    doc.Regenerate();

                    bool shouldFlip = facingFlipped;

                    // Auto-detect facing based on which side of the wall the placement point is on
                    if (!shouldFlip)
                    {
                        var wall = instance.Host as Wall;
                        if (wall != null)
                        {
                            var locCurve = wall.Location as LocationCurve;
                            if (locCurve != null)
                            {
                                var wallStart = locCurve.Curve.GetEndPoint(0);
                                var wallEnd   = locCurve.Curve.GetEndPoint(1);
                                var wallDir   = new XYZ(wallEnd.X - wallStart.X, wallEnd.Y - wallStart.Y, 0).Normalize();
                                var wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();

                                var ir = locCurve.Curve.Project(locationPoint);
                                if (ir != null)
                                {
                                    var centerPt = ir.XYZPoint;
                                    double side = (locationPoint - centerPt).DotProduct(wallNormal);
                                    double facingDot = instance.FacingOrientation.DotProduct(wallNormal);

                                    if ((side < -1e-10 && facingDot > 0) ||
                                        (side > 1e-10  && facingDot < 0))
                                    {
                                        shouldFlip = true;
                                    }
                                }
                            }
                        }
                    }

                    if (shouldFlip)
                    {
                        instance.flipFacing();
                        doc.Regenerate();
                    }
                }

                // Handle rotation for non-hosted elements
                if (rotationDeg != 0 &&
                    builtInCategory != BuiltInCategory.OST_Doors &&
                    builtInCategory != BuiltInCategory.OST_Windows)
                {
                    var origin = locationPoint;
                    var rotAxis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                    var angleRad = rotationDeg * Math.PI / 180.0;
                    ElementTransformUtils.RotateElement(doc, instance.Id, rotAxis, angleRad);
                }

                createdIds.Add(ToolHelpers.GetElementIdValue(instance.Id));
            }

            tx.Commit();
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

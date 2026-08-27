using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates one or more point-based family instances (furniture, doors, windows, columns, etc.).
/// Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement,
/// door/window facing auto-detection, and rotation support.
/// </summary>
[ToolSafety(false, false)]
public class CreatePointBasedElementTool : ICortexTool
{
    public string Name => "create_point_based_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more point-based family instances (furniture, doors, windows, columns, etc.). Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement, door/window facing auto-detection, and rotation support.";

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
        var details = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);

        foreach (var item in dataToken)
        {
            try
            {
                ProcessPointElement(doc, (JObject)item, createdIds, warnings, details, dryRun);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to create element: {ex.Message}");
            }
        }

        var message = dryRun
            ? $"Previewed {details.Count} point-based element specification(s)."
            : $"Successfully created {createdIds.Count} element(s).";
        if (warnings.Count > 0)
            message += "\n\nWarnings:\n  - " + string.Join("\n  - ", warnings);

        return CortexResult<object>.Ok(new
        {
            message,
            dryRun,
            processed = dataToken.Count(),
            created = createdIds.Count,
            createdElementIds = createdIds,
            details
        });
    }

    private static void ProcessPointElement(Document doc, JObject item, List<long> createdIds,
        List<string> warnings, List<object> details, bool dryRun)
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
        var levelId         = item["levelId"]?.Value<long?>() ?? -1;
        var rotationDeg     = item["rotation"]?.Value<double?>() ?? 0.0;
        var hostWallId      = item["hostWallId"]?.Value<long?>() ?? -1;
        var facingFlipped   = item["facingFlipped"]?.Value<bool?>() ?? false;
        var handFlipped     = item["handFlipped"]?.Value<bool?>() ?? false;
        var strictType      = item["strictType"]?.Value<bool?>() ?? false;
        // z semantics were the single most expensive ambiguity of the connector:
        // create_wall ignores locationLine.z (baseLevelId governs) while a hosted
        // insertion point needs an ABSOLUTE project elevation. Passing z=0 by
        // analogy produced 9 consecutive failures whose only message was Revit's
        // "instances do not cut anything", which never mentions elevation.
        var zMode = (item["zMode"]?.Value<string>() ?? "absolute").Trim().ToLowerInvariant();
        if (zMode is not ("absolute" or "relativetolevel"))
        {
            warnings.Add($"zMode '{zMode}' is not recognized. Use \"absolute\" (default) or \"relativeToLevel\".");
            return;
        }

        // Resolve levels
        var baseLevelFt = baseLevelMm / MmPerFoot;
        var baseLevel   = levelId > 0
            ? doc.GetElement(ToolHelpers.ToElementId(levelId)) as Level
            : FindNearestLevel(doc, baseLevelFt);
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }

        if (zMode == "relativetolevel")
        {
            locationPoint = new XYZ(locationPoint.X, locationPoint.Y,
                baseLevel.Elevation + locationPoint.Z);
        }

        // Resolve family symbol
        FamilySymbol? symbol = null;
        if (requestedTypeId > 0)
        {
            var typeElemId = new ElementId(requestedTypeId);
            var typeElem = doc.GetElement(typeElemId);
            if (typeElem is FamilySymbol fs)
            {
                symbol = fs;
                builtInCategory = (BuiltInCategory)symbol.Category.Id.Value;
            }
        }

        if (builtInCategory == BuiltInCategory.INVALID)
        {
            warnings.Add($"Could not determine category — provide 'category' field or a valid 'typeId'");
            return;
        }

        if (symbol == null)
        {
            if (strictType)
            {
                warnings.Add($"A valid typeId is required; {requestedTypeId} was not found.");
                return;
            }
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

        // Resolve the requested host now: both the preview and the real call must
        // validate the insertion point against it.
        Wall? hostWall = null;
        if (hostWallId > 0)
        {
            var hostElem = doc.GetElement(ToolHelpers.ToElementId(hostWallId));
            if (hostElem is Wall resolvedWall)
                hostWall = resolvedWall;
            else
                warnings.Add($"Requested hostWallId {hostWallId} is not a valid wall. Using auto-detection.");
        }

        if (hostWall != null)
        {
            var hostError = DescribeHostFit(hostWall, symbol, locationPoint, baseLevel);
            if (hostError != null)
            {
                warnings.Add(hostError);
                return;
            }
        }

        if (dryRun)
        {
            details.Add(new
            {
                kind = "point_based_family_instance",
                typeId = ToolHelpers.GetElementIdValue(symbol.Id),
                familyName = symbol.FamilyName,
                typeName = symbol.Name,
                category = builtInCategory.ToString(),
                levelId = ToolHelpers.GetElementIdValue(baseLevel.Id),
                levelElevationMm = Math.Round(baseLevel.Elevation * MmPerFoot, 1),
                hostWallId = hostWallId > 0 ? (long?)hostWallId : null,
                locationPointMm = locationPtToken.DeepClone(),
                zMode,
                resolvedZmm = Math.Round(locationPoint.Z * MmPerFoot, 1),
                rotationDeg,
                facingFlipped,
                handFlipped
            });
            return;
        }

        using var tx = new Transaction(doc, "RiveTT: Create Point-Based Element");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            FamilyInstance? instance = null;

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
                    if (handFlipped && instance.CanFlipHand)
                    {
                        instance.flipHand();
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

            }

            if (tx.Commit() != TransactionStatus.Committed)
                warnings.Add($"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");
            else if (instance != null)
            {
                var instanceId = ToolHelpers.GetElementIdValue(instance.Id);
                createdIds.Add(instanceId);
                details.Add(new
                {
                    kind = "point_based_family_instance",
                    elementId = instanceId,
                    levelId = ToolHelpers.GetElementIdValue(instance.LevelId),
                    hostId = ToolHelpers.GetElementIdValue(instance.Host?.Id),
                    facingFlipped = instance.FacingFlipped,
                    handFlipped = instance.HandFlipped
                });
            }
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

    /// <summary>
    /// Explains, in millimetres, why an insertion point cannot work in this host —
    /// before Revit answers with "instances do not cut anything" or "cannot cut
    /// instance out of wall", messages that name neither the elevation nor the
    /// width that was actually the problem. Returns null when the fit is plausible.
    /// </summary>
    private static string? DescribeHostFit(Wall hostWall, FamilySymbol symbol, XYZ point, Level level)
    {
        try
        {
            var box = hostWall.get_BoundingBox(null);
            if (box != null)
            {
                var minZ = box.Min.Z;
                var maxZ = box.Max.Z;
                // Tolerance: an insertion exactly at the base or top is legitimate.
                if (point.Z < minZ - 1e-6 || point.Z > maxZ + 1e-6)
                {
                    return $"Insertion point z={point.Z * MmPerFoot:F0} mm is outside the vertical range of " +
                           $"host wall {ToolHelpers.GetElementIdValue(hostWall.Id)} " +
                           $"({minZ * MmPerFoot:F0} mm to {maxZ * MmPerFoot:F0} mm). " +
                           $"locationPoint.z is an ABSOLUTE project elevation: level '{level.Name}' sits at " +
                           $"{level.Elevation * MmPerFoot:F0} mm, so pass that plus the sill height, " +
                           "or set zMode=\"relativeToLevel\" to have z added to the level elevation.";
                }
            }

            var openingWidthFt = OpeningWidthFt(symbol);
            if (openingWidthFt > 0 && hostWall.Location is LocationCurve locationCurve)
            {
                var wallLengthFt = locationCurve.Curve.Length;
                if (openingWidthFt >= wallLengthFt)
                {
                    return $"Opening '{symbol.FamilyName} / {symbol.Name}' is {openingWidthFt * MmPerFoot:F0} mm wide " +
                           $"but host wall {ToolHelpers.GetElementIdValue(hostWall.Id)} is only " +
                           $"{wallLengthFt * MmPerFoot:F0} mm long, so the cut cannot fit. " +
                           "Pick a narrower type or lengthen the wall.";
                }
            }
        }
        catch
        {
            // A geometry probe must never block a placement Revit would accept.
        }

        return null;
    }

    private static double OpeningWidthFt(FamilySymbol symbol)
    {
        foreach (var builtIn in new[]
                 {
                     BuiltInParameter.DOOR_WIDTH,
                     BuiltInParameter.WINDOW_WIDTH,
                     BuiltInParameter.GENERIC_WIDTH,
                     BuiltInParameter.FAMILY_WIDTH_PARAM
                 })
        {
            var parameter = symbol.get_Parameter(builtIn);
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
                return parameter.AsDouble();
        }

        return 0;
    }
}

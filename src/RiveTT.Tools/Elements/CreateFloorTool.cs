using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a floor from boundary points or a room boundary.
/// </summary>
[ToolSafety(false, false)]
public class CreateFloorTool : ICortexTool
{
    public string Name => "create_floor";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates an architectural floor (category: Floors) from boundary points or a room boundary, optionally with holes (inner loops). For structural foundation slabs use create_surface_based_element with category OST_StructuralFoundation. If a floorTypeName is not provided, defaults to the first architectural floor type (OST_Floors) in the project.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var boundaryPoints = input["boundaryPoints"] as JArray;
        var roomId = input["roomId"]?.Value<long>() ?? 0;
        var floorTypeName = input["floorTypeName"]?.Value<string>();
        var levelElevationMm = input["levelElevation"]?.Value<double?>();

        try
        {
            // Resolve floor type
            string? floorTypeWarning = null;
            var floorType = !string.IsNullOrEmpty(floorTypeName)
                ? new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .FirstOrDefault(ft => ft.Name.Equals(floorTypeName, StringComparison.OrdinalIgnoreCase))
                : null;
            if (floorType == null && !string.IsNullOrEmpty(floorTypeName))
            {
                var defaultType = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .OfCategory(BuiltInCategory.OST_Floors).Cast<FloorType>().FirstOrDefault();
                if (defaultType != null)
                    floorTypeWarning = $"Floor type '{floorTypeName}' not found. Used default architectural floor type '{defaultType.Name}'.";
                floorType = defaultType;
            }
            floorType ??= new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                .OfCategory(BuiltInCategory.OST_Floors).Cast<FloorType>().FirstOrDefault();

            if (floorType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No floor types available");

            // Build curve loop
            CurveLoop loop;
            if (roomId > 0)
            {
                var room = doc.GetElement(new ElementId(roomId)) as Room;
                if (room == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Room {roomId} not found");

                var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (segments == null || segments.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Room has no boundary");

                loop = new CurveLoop();
                foreach (var seg in segments[0])
                    loop.Append(seg.GetCurve());
            }
            else if (boundaryPoints != null && boundaryPoints.Count >= 3)
            {
                loop = new CurveLoop();
                var points = boundaryPoints.Select(p => new XYZ(
                    p["x"]!.Value<double>() / MmPerFoot,
                    p["y"]!.Value<double>() / MmPerFoot,
                    0)).ToList();

                for (int i = 0; i < points.Count; i++)
                    loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Provide boundaryPoints (min 3) or roomId");
            }

            // Resolve level: use room's level when creating from room, otherwise elevation or lowest
            Level? level = null;
            if (roomId > 0)
            {
                var roomForLevel = doc.GetElement(new ElementId(roomId)) as Room;
                if (roomForLevel != null)
                    level = doc.GetElement(roomForLevel.LevelId) as Level;
            }

            if (level == null && levelElevationMm.HasValue)
            {
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - levelElevationMm.Value / MmPerFoot)).FirstOrDefault();
            }

            level ??= new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();

            if (level == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No levels found");

            var warnings = new List<string>();
            if (floorTypeWarning != null) warnings.Add(floorTypeWarning);

            // Outer loop + optional holes (inner loops). Floor.Create takes IList<CurveLoop>.
            var loops = new List<CurveLoop> { loop };
            var holes = input["holes"] as JArray;
            if (holes != null)
            {
                int holeIndex = 0;
                foreach (var hole in holes.OfType<JArray>())
                {
                    holeIndex++;
                    if (hole.Count < 3)
                    {
                        warnings.Add($"Hole {holeIndex} skipped: needs at least 3 points");
                        continue;
                    }
                    try
                    {
                        var hpts = hole.Select(p => new XYZ(
                            p["x"]!.Value<double>() / MmPerFoot,
                            p["y"]!.Value<double>() / MmPerFoot,
                            0)).ToList();
                        var hloop = new CurveLoop();
                        for (int i = 0; i < hpts.Count; i++)
                            hloop.Append(Line.CreateBound(hpts[i], hpts[(i + 1) % hpts.Count]));
                        loops.Add(hloop);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Hole {holeIndex} skipped: {ex.Message}");
                    }
                }
            }

            // Preview last, once everything is resolved: the useful part of a floor preview
            // is which TYPE and which LEVEL it would land on, and both are decided above by
            // fallbacks the caller never stated (nearest level, first architectural type).
            if (ToolHelpers.GetDryRun(input))
            {
                var areaM2 = 0.0;
                try
                {
                    // Outer loop area minus the holes, in m2. A quick sanity figure: a
                    // boundary entered in metres instead of millimetres shows up here as a
                    // number a million times too large, before anything is created.
                    areaM2 = LoopAreaM2(loops[0]) - loops.Skip(1).Sum(LoopAreaM2);
                }
                catch
                {
                    // A self-intersecting loop has no meaningful area; the rest still stands.
                }

                return CortexResult<object>.Ok(new
                {
                    dryRun = true,
                    message = $"DryRun: a floor of type '{floorType.Name}' would be created on level "
                            + $"'{level.Name}'. Set dryRun=false to execute.",
                    floorTypeName = floorType.Name,
                    levelName = level.Name,
                    levelElevationMm = Math.Round(level.Elevation * MmPerFoot, 1),
                    holeCount = loops.Count - 1,
                    approxAreaM2 = Math.Round(areaM2, 2),
                    boundarySource = roomId > 0 ? $"room {roomId}" : "boundaryPoints",
                    warnings
                });
            }

            using var tx = new Transaction(doc, "RiveTT: Create Floor");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var floor = Floor.Create(doc, loops, floorType.Id, level.Id);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                floorId = ToolHelpers.GetElementIdValue(floor.Id),
                floorTypeName = floorType.Name,
                levelName = level.Name,
                holeCount = loops.Count - 1,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create floor: {ex.Message}",
                suggestion: "Run with dryRun=true to see the resolved type, level and boundary area "
                          + "before committing. A boundary must be a closed, non-self-intersecting "
                          + "loop of at least 3 points, in millimetres.");
        }
    }

    /// <summary>
    /// Planar area of a closed loop in m2, by the shoelace formula on the XY projection.
    /// Absolute value: loop orientation carries no meaning here.
    /// </summary>
    private static double LoopAreaM2(CurveLoop loop)
    {
        var points = loop.Select(curve => curve.GetEndPoint(0)).ToList();
        if (points.Count < 3) return 0;

        var twiceArea = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }

        // Revit stores feet; 1 ft2 = 0.09290304 m2.
        return Math.Abs(twiceArea / 2.0) * 0.09290304;
    }
}

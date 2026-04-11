using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates a floor from boundary points or a room boundary.
/// </summary>
public class CreateFloorTool : ICortexTool
{
    public string Name => "create_floor";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a floor from boundary points or a room boundary.";
    private const double MmPerFoot = 304.8;

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
                var defaultType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
                if (defaultType != null)
                    floorTypeWarning = $"Floor type '{floorTypeName}' not found. Used default type '{defaultType.Name}'.";
                floorType = defaultType;
            }
            floorType ??= new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();

            if (floorType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No floor types available");

            // Build curve loop
            CurveLoop loop;
            if (roomId > 0)
            {
#if REVIT2024_OR_GREATER
                var room = doc.GetElement(new ElementId(roomId)) as Room;
#else
                var room = doc.GetElement(new ElementId((int)roomId)) as Room;
#endif
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
#if REVIT2024_OR_GREATER
                var roomForLevel = doc.GetElement(new ElementId(roomId)) as Room;
#else
                var roomForLevel = doc.GetElement(new ElementId((int)roomId)) as Room;
#endif
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

            using var tx = new Transaction(doc, "RevitCortex: Create Floor");
            tx.Start();
            var floor = Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id);
            tx.Commit();

            var warnings = new List<string>();
            if (floorTypeWarning != null) warnings.Add(floorTypeWarning);

            return CortexResult<object>.Ok(new
            {
                floorId = ToolHelpers.GetElementIdValue(floor.Id),
                floorTypeName = floorType.Name,
                levelName = level.Name,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create floor: {ex.Message}");
        }
    }
}

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
/// Creates a room at the specified location point inside enclosed walls.
/// </summary>
[ToolSafety(false, false)]
public class CreateRoomTool : ICortexTool
{
    public string Name => "create_room";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a room at the specified location point inside enclosed walls. Supports dryRun. An unenclosed result (area 0) is refused and nothing is left in the model, unless allowUnenclosed=true.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>() ?? "";
        var location = input["location"];
        if (location == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "location {x, y, z} in mm is required");

        var number = input["number"]?.Value<string>();
        var levelId = input["levelId"]?.Value<long>() ?? 0;
        var department = input["department"]?.Value<string>();
        var comments = input["comments"]?.Value<string>();
        var limitOffsetMm = input["limitOffset"]?.Value<double>() ?? 0;
        var baseOffsetMm = input["baseOffset"]?.Value<double>() ?? 0;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;
        // An unenclosed room (area = 0) is unusable for schedules, area
        // takeoffs or tags. Refuse it by default rather than leaving a dead
        // room in the model — see P2.3 in PLAN_CORRECTION.md.
        var allowUnenclosed = input["allowUnenclosed"]?.Value<bool>() ?? false;

        try
        {
            var xFt = location["x"]!.Value<double>() / MmPerFoot;
            var yFt = location["y"]!.Value<double>() / MmPerFoot;
            var zFt = location["z"]?.Value<double>() ?? 0;
            zFt /= MmPerFoot;

            // Resolve level
            Level? level;
            if (levelId > 0)
            {
                level = doc.GetElement(new ElementId(levelId)) as Level;
            }
            else
            {
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - zFt)).FirstOrDefault();
            }

            if (level == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No levels found in document");

            // Placing a point inside an area an existing room already owns creates an
            // unbounded, overlapping room that Revit accepts silently. Report the
            // occupant up front instead of letting the caller discover a room whose
            // Area/Perimeter/Volume are all null.
            var occupant = FindRoomAt(doc, level, xFt, yFt, zFt);

            if (dryRun)
            {
                return CortexResult<object>.Ok(new
                {
                    message = occupant == null
                        ? $"DryRun: a room would be created on level '{level.Name}' at ({location["x"]}, {location["y"]}) mm."
                        : $"DryRun: point already inside room '{DescribeRoom(occupant)}' — creating here would " +
                          "produce an overlapping, unbounded room.",
                    levelId = ToolHelpers.GetElementIdValue(level.Id),
                    levelName = level.Name,
                    locationMm = new { x = location["x"], y = location["y"], z = location["z"] },
                    occupiedBy = occupant == null
                        ? null
                        : new
                        {
                            roomId = ToolHelpers.GetElementIdValue(occupant.Id),
                            name = DescribeRoom(occupant)
                        },
                    warnings = occupant == null
                        ? Array.Empty<string>()
                        : new[]
                        {
                            "This point is inside an existing room. Split the space first " +
                            "(create_wall or create_room_separation_line), then place the second room."
                        }
                });
            }

            using var tx = new Transaction(doc, "RiveTT: Create Room");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var uv = new UV(xFt, yFt);
            var room = doc.Create.NewRoom(level, uv);

            if (room == null)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Room creation failed — location may not be inside enclosed walls");

            if (!string.IsNullOrEmpty(name))
            {
                var nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                if (nameParam != null) nameParam.Set(name);
            }

            if (!string.IsNullOrEmpty(number))
            {
                var numParam = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                if (numParam != null) numParam.Set(number);
            }

            if (!string.IsNullOrEmpty(department))
            {
                var deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                if (deptParam != null) deptParam.Set(department);
            }

            if (!string.IsNullOrEmpty(comments))
            {
                var cmtParam = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (cmtParam != null) cmtParam.Set(comments);
            }

            if (limitOffsetMm != 0)
            {
                var limitParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
                if (limitParam != null) limitParam.Set(limitOffsetMm / MmPerFoot);
            }

            if (baseOffsetMm != 0)
            {
                var baseParam = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                if (baseParam != null) baseParam.Set(baseOffsetMm / MmPerFoot);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            // Enclosure is the whole point of a room. Report it in the creation
            // response: an unbounded room reports Area = 0 and is useless downstream,
            // and previously only a separate get_element_parameters call revealed it.
            var areaFt2 = room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
            var enclosed = areaFt2 > 1e-6;
            var areaM2 = Math.Round(areaFt2 * 0.09290304, 3);

            if (!enclosed && !allowUnenclosed)
            {
                var roomId = room.Id;
                using var deleteTx = new Transaction(doc, "RiveTT: Discard Unenclosed Room");
                TransactionFailureHandling.SuppressWarnings(deleteTx);
                deleteTx.Start();
                doc.Delete(roomId);
                deleteTx.Commit();

                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "The room would not be enclosed (area = 0): the point is not inside a closed loop of " +
                    "room-bounding elements, or it falls inside a room that already exists. Nothing was left " +
                    "in the model.",
                    suggestion: "Close the boundary (walls or a room separation line) and retry, or pass " +
                                "allowUnenclosed=true to keep an unenclosed room deliberately.");
            }

            return CortexResult<object>.Ok(new
            {
                roomId = ToolHelpers.GetElementIdValue(room.Id),
                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? name,
                roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                levelName = level.Name,
                levelId = ToolHelpers.GetElementIdValue(level.Id),
                enclosed,
                areaM2 = enclosed ? areaM2 : (double?)null,
                warnings = enclosed
                    ? Array.Empty<string>()
                    : new[]
                    {
                        "The room was created but is NOT enclosed (area = 0): the point is not inside a closed " +
                        "loop of room-bounding elements, or it falls inside a room that already exists. " +
                        "Delete it, close the boundary, then place it again."
                    }
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create room: {ex.Message}");
        }
    }

    /// <summary>The placed room whose boundary contains the point, if any.</summary>
    private static Room? FindRoomAt(Document doc, Level level, double xFt, double yFt, double zFt)
    {
        try
        {
            var probe = new XYZ(xFt, yFt, Math.Abs(zFt) > 1e-9 ? zFt : level.Elevation + 0.1);
            foreach (var element in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_Rooms)
                         .WhereElementIsNotElementType())
            {
                if (element is not Room room) continue;
                if (room.Area <= 1e-6) continue;
                if (room.LevelId != level.Id) continue;
                if (room.IsPointInRoom(probe)) return room;
            }
        }
        catch
        {
            // Point-in-room is geometry-dependent; a failure here must not block a
            // legitimate creation.
        }

        return null;
    }

    private static string DescribeRoom(Room room)
    {
        var number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
        var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
        return string.IsNullOrWhiteSpace(number) ? name ?? "" : $"{number} - {name}";
    }
}

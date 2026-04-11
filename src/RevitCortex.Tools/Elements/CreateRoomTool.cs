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
/// Creates a room at the specified location point inside enclosed walls.
/// </summary>
public class CreateRoomTool : ICortexTool
{
    public string Name => "create_room";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a room at the specified location point inside enclosed walls.";
    private const double MmPerFoot = 304.8;

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
#if REVIT2024_OR_GREATER
                level = doc.GetElement(new ElementId(levelId)) as Level;
#else
                level = doc.GetElement(new ElementId((int)levelId)) as Level;
#endif
            }
            else
            {
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - zFt)).FirstOrDefault();
            }

            if (level == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No levels found in document");

            using var tx = new Transaction(doc, "RevitCortex: Create Room");
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

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                roomId = ToolHelpers.GetElementIdValue(room.Id),
                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? name,
                roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                levelName = level.Name
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create room: {ex.Message}");
        }
    }
}

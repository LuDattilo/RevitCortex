using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Tags all or specified rooms in the current view.
/// </summary>
public class TagRoomsTool : ICortexTool
{
    public string Name => "tag_rooms";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var useLeader = input["useLeader"]?.Value<bool>() ?? false;
        var roomIds = input["roomIds"]?.ToObject<List<long>>();

        try
        {
            var view = doc.ActiveView;

            // Get rooms
            IEnumerable<Room> rooms;
            if (roomIds != null && roomIds.Count > 0)
            {
                rooms = roomIds.Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return doc.GetElement(new ElementId(id)) as Room;
#else
                    return doc.GetElement(new ElementId((int)id)) as Room;
#endif
                }).Where(r => r != null)!;
            }
            else
            {
                rooms = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0);
            }

            // Find room tag type
            var tagType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            // Get existing tagged rooms to avoid duplicates
            var alreadyTagged = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<RoomTag>()
                .Select(rt => GetIdLong(rt.Room?.Id ?? ElementId.InvalidElementId))
                .ToHashSet();

            int taggedCount = 0;
            int skippedCount = 0;
            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RevitCortex: Tag Rooms");
            tx.Start();

            foreach (var room in rooms)
            {
                if (alreadyTagged.Contains(GetIdLong(room.Id)))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    var loc = room.Location as LocationPoint;
                    if (loc == null) continue;

                    var point = loc.Point;
                    var uv = new UV(point.X, point.Y);
                    var tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), uv, view.Id);
                    if (tag != null)
                    {
                        tag.HasLeader = useLeader;
                        taggedCount++;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to tag room {room.Name}: {ex.Message}");
                }
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                taggedCount,
                skippedCount,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to tag rooms: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

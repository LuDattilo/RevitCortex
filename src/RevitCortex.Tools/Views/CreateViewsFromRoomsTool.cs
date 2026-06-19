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

namespace RevitCortex.Tools.Views;

/// <summary>
/// Creates callout, section, or elevation views from room bounding boxes.
/// Combines create_callout_from_rooms, create_elevations_from_rooms, and create_views_from_rooms.
/// </summary>
public class CreateViewsFromRoomsTool : ICortexTool
{
    public string Name => "create_views_from_rooms";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates callout, section, or elevation views from room bounding boxes. Combines create_callout_from_rooms, create_elevations_from_rooms, and create_views_from_rooms.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var roomIds = input["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
        var viewType = input["viewType"]?.Value<string>() ?? "callout";
        var offsetMm = input["offset"]?.Value<double>() ?? 500;
        var scale = input["scale"]?.Value<int>() ?? 50;
        var namingPattern = input["namingPattern"]?.Value<string>() ?? "{RoomNumber} - {RoomName}";

        if (roomIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "roomIds array is required");

        try
        {
            var offset = offsetMm / MmPerFoot;
            var createdViews = new List<object>();
            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RevitCortex: Create Views From Rooms");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var rid in roomIds)
            {
#if REVIT2024_OR_GREATER
                var room = doc.GetElement(new ElementId(rid)) as Room;
#else
                var room = doc.GetElement(new ElementId((int)rid)) as Room;
#endif
                if (room == null) { warnings.Add($"Room {rid} not found"); continue; }

                var bb = room.get_BoundingBox(null);
                if (bb == null) { warnings.Add($"Room {room.Name} has no bounding box"); continue; }

                var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                var roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                var viewNameBase = namingPattern
                    .Replace("{RoomName}", roomName)
                    .Replace("{RoomNumber}", roomNumber);

                switch (viewType.ToLowerInvariant())
                {
                    case "section":
                        var sectionView = CreateSectionFromBB(doc, bb, offset, "north");
                        if (sectionView != null)
                        {
                            TrySetName(sectionView, $"Section - {viewNameBase}");
                            sectionView.Scale = scale;
                            createdViews.Add(new { id = ToolHelpers.GetElementIdValue(sectionView.Id), name = sectionView.Name, type = "section" });
                        }
                        break;
                    case "elevation":
                        var directions = new[] { "north", "south", "east", "west" };
                        foreach (var dir in directions)
                        {
                            var elevView = CreateSectionFromBB(doc, bb, offset, dir);
                            if (elevView != null)
                            {
                                TrySetName(elevView, $"{dir.Substring(0,1).ToUpper()}{dir.Substring(1)} - {viewNameBase}");
                                elevView.Scale = scale;
                                createdViews.Add(new { id = ToolHelpers.GetElementIdValue(elevView.Id), name = elevView.Name, type = $"elevation_{dir}" });
                            }
                        }
                        break;
                    default: // callout
                        var parentView = FindParentFloorPlan(doc, room);
                        if (parentView == null) { warnings.Add($"No floor plan found for room {roomNumber}"); continue; }

                        var min = new XYZ(bb.Min.X - offset, bb.Min.Y - offset, 0);
                        var max = new XYZ(bb.Max.X + offset, bb.Max.Y + offset, 0);
                        var calloutVft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                            .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                        if (calloutVft == null) { warnings.Add("No floor plan ViewFamilyType"); continue; }

                        var callout = ViewSection.CreateCallout(doc, parentView.Id, calloutVft.Id, min, max);
                        TrySetName(callout, $"Callout - {viewNameBase}");
                        callout.Scale = scale;
                        createdViews.Add(new { id = ToolHelpers.GetElementIdValue(callout.Id), name = callout.Name, type = "callout" });
                        break;
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                createdViewCount = createdViews.Count,
                createdViews,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static ViewSection? CreateSectionFromBB(Document doc, BoundingBoxXYZ roomBB, double offset, string direction)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
        if (vft == null) return null;

        var center = (roomBB.Min + roomBB.Max) / 2.0;
        var halfW = (roomBB.Max.X - roomBB.Min.X) / 2.0 + offset;
        var halfH = (roomBB.Max.Z - roomBB.Min.Z) / 2.0 + offset;
        var depth = (roomBB.Max.Y - roomBB.Min.Y) / 2.0 + offset;

        XYZ right, up, viewDir;
        switch (direction)
        {
            case "south": right = -XYZ.BasisX; up = XYZ.BasisZ; viewDir = XYZ.BasisY; break;
            case "east": right = XYZ.BasisY; up = XYZ.BasisZ; viewDir = -XYZ.BasisX; halfW = (roomBB.Max.Y - roomBB.Min.Y) / 2.0 + offset; depth = (roomBB.Max.X - roomBB.Min.X) / 2.0 + offset; break;
            case "west": right = -XYZ.BasisY; up = XYZ.BasisZ; viewDir = XYZ.BasisX; halfW = (roomBB.Max.Y - roomBB.Min.Y) / 2.0 + offset; depth = (roomBB.Max.X - roomBB.Min.X) / 2.0 + offset; break;
            default: right = XYZ.BasisX; up = XYZ.BasisZ; viewDir = -XYZ.BasisY; break; // north
        }

        var bb = new BoundingBoxXYZ
        {
            Min = new XYZ(-halfW, -halfH, 0),
            Max = new XYZ(halfW, halfH, depth * 2)
        };
        var transform = Transform.Identity;
        transform.Origin = center;
        transform.BasisX = right;
        transform.BasisY = up;
        transform.BasisZ = viewDir;
        bb.Transform = transform;

        return ViewSection.CreateSection(doc, vft.Id, bb);
    }

    private static ViewPlan? FindParentFloorPlan(Document doc, Room room)
    {
        var level = room.Level;
        if (level == null) return null;
        return new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level.Id && v.ViewType == ViewType.FloorPlan);
    }

    private static void TrySetName(View view, string name)
    {
        try { view.Name = name; }
        catch
        {
            // Name already exists — try with suffix
            for (int i = 2; i <= 20; i++)
            {
                try { view.Name = $"{name} ({i})"; return; }
                catch { }
            }
        }
    }
}

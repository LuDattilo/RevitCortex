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
[ToolSafety(false, false)]
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
                        var ownerPlan = FindOwnerFloorPlan(doc, room);
                        if (ownerPlan == null) { warnings.Add($"No floor plan found for room {roomNumber}"); continue; }

                        createdViews.AddRange(CreateElevationsFromRoom(doc, room, bb, offset, scale, ownerPlan, viewNameBase, warnings));
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
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {FormatException(ex)}");
        }
    }

    private static List<object> CreateElevationsFromRoom(
        Document doc,
        Room room,
        BoundingBoxXYZ roomBB,
        double offset,
        int scale,
        ViewPlan ownerPlan,
        string viewNameBase,
        List<string> warnings)
    {
        var createdViews = new List<object>();
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
        if (vft == null)
        {
            warnings.Add("No elevation ViewFamilyType");
            return createdViews;
        }

        var center = (roomBB.Min + roomBB.Max) / 2.0;
        var z = room.Level != null ? room.Level.Elevation : roomBB.Min.Z;
        var markerOrigin = new XYZ(center.X, center.Y, z);
        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, markerOrigin, scale);

        var directions = new[] { "north", "south", "east", "west" };
        foreach (var dir in directions)
        {
            try
            {
                var index = ElevationIndexForDirection(dir);
                var elevView = marker.CreateElevation(doc, ownerPlan.Id, index);
                TrySetName(elevView, $"{Capitalize(dir)} - {viewNameBase}");
                elevView.Scale = scale;
                ApplyRoomCrop(elevView, roomBB, offset);
                createdViews.Add(new { id = ToolHelpers.GetElementIdValue(elevView.Id), name = elevView.Name, type = $"elevation_{dir}" });
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to create {dir} elevation for room {room.Number}: {FormatException(ex)}");
            }
        }

        if (createdViews.Count == 0)
        {
            try { doc.Delete(marker.Id); } catch { }
        }

        return createdViews;
    }

    private static int ElevationIndexForDirection(string direction)
    {
        switch (direction)
        {
            case "east": return 1;
            case "south": return 2;
            case "west": return 3;
            default: return 0;
        }
    }

    private static void ApplyRoomCrop(ViewSection view, BoundingBoxXYZ roomBB, double offset)
    {
        try
        {
            var crop = view.CropBox;
            var inverse = crop.Transform.Inverse;
            var points = GetBoundingBoxCorners(roomBB).Select(p => inverse.OfPoint(p)).ToList();

            var minX = points.Min(p => p.X) - offset;
            var maxX = points.Max(p => p.X) + offset;
            var minY = points.Min(p => p.Y) - offset;
            var maxY = points.Max(p => p.Y) + offset;

            crop.Min = new XYZ(minX, minY, crop.Min.Z);
            crop.Max = new XYZ(maxX, maxY, crop.Max.Z);
            view.CropBox = crop;
            view.CropBoxActive = true;
            view.CropBoxVisible = true;
        }
        catch
        {
            // Some elevation types/templates reject crop-box edits; the view itself is still valid.
        }
    }

    private static IEnumerable<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ bb)
    {
        var transform = bb.Transform ?? Transform.Identity;
        var min = bb.Min;
        var max = bb.Max;

        yield return transform.OfPoint(new XYZ(min.X, min.Y, min.Z));
        yield return transform.OfPoint(new XYZ(max.X, min.Y, min.Z));
        yield return transform.OfPoint(new XYZ(min.X, max.Y, min.Z));
        yield return transform.OfPoint(new XYZ(max.X, max.Y, min.Z));
        yield return transform.OfPoint(new XYZ(min.X, min.Y, max.Z));
        yield return transform.OfPoint(new XYZ(max.X, min.Y, max.Z));
        yield return transform.OfPoint(new XYZ(min.X, max.Y, max.Z));
        yield return transform.OfPoint(new XYZ(max.X, max.Y, max.Z));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Substring(0, 1).ToUpperInvariant() + value.Substring(1);
    }

    private static string FormatException(Exception ex)
    {
        var message = ex.Message;
        var typeName = ex.GetType().FullName ?? ex.GetType().Name;
        return string.IsNullOrWhiteSpace(message) ? typeName : $"{typeName}: {message}";
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

    private static ViewPlan? FindOwnerFloorPlan(Document doc, Room room)
    {
        var activePlan = doc.ActiveView as ViewPlan;
        if (IsOwnerFloorPlanForRoom(activePlan, room))
            return activePlan;

        return FindParentFloorPlan(doc, room);
    }

    private static bool IsOwnerFloorPlanForRoom(ViewPlan? view, Room room)
    {
        if (view == null || view.IsTemplate || view.ViewType != ViewType.FloorPlan)
            return false;

        var roomLevel = room.Level;
        if (roomLevel == null || view.GenLevel == null)
            return true;

        return ToolHelpers.GetElementIdValue(view.GenLevel.Id) == ToolHelpers.GetElementIdValue(roomLevel.Id);
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

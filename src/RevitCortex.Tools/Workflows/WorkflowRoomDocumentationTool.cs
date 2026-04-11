using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Workflows;

/// <summary>
/// Auto-generates room documentation: callout views and optional sections from rooms.
/// </summary>
public class WorkflowRoomDocumentationTool : ICortexTool
{
    public string Name => "workflow_room_documentation";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Auto-generates room documentation: callout views and optional sections from rooms.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var levelName = input["levelName"]?.Value<string>();
        var createSections = input["createSections"]?.Value<bool>() ?? true;
        var offsetMm = input["offset"]?.Value<double>() ?? 300;

        try
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            if (!string.IsNullOrEmpty(levelName))
                rooms = rooms.Where(r => r.Level?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (rooms.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No placed rooms found");

            var offset = offsetMm / MmPerFoot;
            var createdViews = new List<object>();

            using var tx = new Transaction(doc, "RevitCortex: Room Documentation");
            tx.Start();

            var planVft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
            var sectionVft = createSections
                ? new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section)
                : null;

            foreach (var room in rooms)
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) continue;

                var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                var roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";

                // Create callout
                if (planVft != null)
                {
                    var parentView = FindFloorPlan(doc, room);
                    if (parentView != null)
                    {
                        var min = new XYZ(bb.Min.X - offset, bb.Min.Y - offset, 0);
                        var max = new XYZ(bb.Max.X + offset, bb.Max.Y + offset, 0);
                        try
                        {
                            var callout = ViewSection.CreateCallout(doc, parentView.Id, planVft.Id, min, max);
                            try { callout.Name = $"Callout - {roomNumber} {roomName}"; } catch { }
                            callout.Scale = 50;
                            createdViews.Add(new { id = GetIdLong(callout.Id), name = callout.Name, type = "callout" });
                        }
                        catch { }
                    }
                }

                // Create sections (N/S)
                if (createSections && sectionVft != null)
                {
                    foreach (var dir in new[] { "north", "south" })
                    {
                        try
                        {
                            var sectionView = CreateSection(doc, sectionVft.Id, bb, offset, dir);
                            if (sectionView != null)
                            {
                                try { sectionView.Name = $"Section {dir.Substring(0, 1).ToUpper()} - {roomNumber} {roomName}"; } catch { }
                                sectionView.Scale = 50;
                                createdViews.Add(new { id = GetIdLong(sectionView.Id), name = sectionView.Name, type = $"section_{dir}" });
                            }
                        }
                        catch { }
                    }
                }
            }

            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                roomCount = rooms.Count,
                createdViewCount = createdViews.Count,
                createdViews
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static ViewPlan? FindFloorPlan(Document doc, Room room)
    {
        var level = room.Level;
        if (level == null) return null;
        return new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level.Id && v.ViewType == ViewType.FloorPlan);
    }

    private static ViewSection? CreateSection(Document doc, ElementId vftId, BoundingBoxXYZ roomBB, double offset, string direction)
    {
        var center = (roomBB.Min + roomBB.Max) / 2.0;
        var halfW = (roomBB.Max.X - roomBB.Min.X) / 2.0 + offset;
        var halfH = (roomBB.Max.Z - roomBB.Min.Z) / 2.0 + offset;
        var depth = (roomBB.Max.Y - roomBB.Min.Y) / 2.0 + offset;

        XYZ right, up, viewDir;
        if (direction == "south") { right = -XYZ.BasisX; up = XYZ.BasisZ; viewDir = XYZ.BasisY; }
        else { right = XYZ.BasisX; up = XYZ.BasisZ; viewDir = -XYZ.BasisY; }

        var bb = new BoundingBoxXYZ { Min = new XYZ(-halfW, -halfH, 0), Max = new XYZ(halfW, halfH, depth * 2) };
        var transform = Transform.Identity;
        transform.Origin = center; transform.BasisX = right; transform.BasisY = up; transform.BasisZ = viewDir;
        bb.Transform = transform;
        return ViewSection.CreateSection(doc, vftId, bb);
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

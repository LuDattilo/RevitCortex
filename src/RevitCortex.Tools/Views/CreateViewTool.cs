using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Creates a new view (floor plan, ceiling plan, section, elevation, 3D).
/// </summary>
public class CreateViewTool : ICortexTool
{
    public string Name => "create_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var viewType = input["viewType"]?.Value<string>() ?? "floorplan";
        var name = input["name"]?.Value<string>();
        var scale = input["scale"]?.Value<int?>() ?? 100;
        var levelElevationMm = input["levelElevation"]?.Value<double?>();
        var levelId = input["levelId"]?.Value<long>() ?? 0;

        try
        {
            using var tx = new Transaction(doc, "RevitCortex: Create View");
            tx.Start();

            View? createdView = null;

            switch (viewType.ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            {
                case "floorplan":
                case "floor":
                    createdView = CreatePlanView(doc, ViewFamily.FloorPlan, levelId, levelElevationMm);
                    break;
                case "ceilingplan":
                case "ceiling":
                    createdView = CreatePlanView(doc, ViewFamily.CeilingPlan, levelId, levelElevationMm);
                    break;
                case "3d":
                case "isometric":
                    var vft3d = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    if (vft3d != null) createdView = View3D.CreateIsometric(doc, vft3d.Id);
                    break;
                case "section":
                    createdView = CreateSectionView(doc, input);
                    break;
                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unsupported viewType: {viewType}",
                        suggestion: "Use: floorplan, ceilingplan, section, 3d");
            }

            if (createdView == null)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Could not create view");

            if (!string.IsNullOrEmpty(name))
                try { createdView.Name = name; } catch { /* duplicate name */ }

            createdView.Scale = scale;

            // Detail level
            var detailLevel = input["detailLevel"]?.Value<string>();
            if (!string.IsNullOrEmpty(detailLevel))
            {
                createdView.DetailLevel = detailLevel.ToLowerInvariant() switch
                {
                    "fine" => ViewDetailLevel.Fine,
                    "medium" => ViewDetailLevel.Medium,
                    _ => ViewDetailLevel.Coarse
                };
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                viewId = GetIdLong(createdView.Id),
                viewName = createdView.Name,
                viewType = createdView.ViewType.ToString(),
                scale = createdView.Scale
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create view: {ex.Message}");
        }
    }

    private static ViewPlan? CreatePlanView(Document doc, ViewFamily family, long levelIdLong, double? levelElevationMm)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == family);
        if (vft == null) return null;

        Level? level = null;
        if (levelIdLong > 0)
        {
#if REVIT2024_OR_GREATER
            level = doc.GetElement(new ElementId(levelIdLong)) as Level;
#else
            level = doc.GetElement(new ElementId((int)levelIdLong)) as Level;
#endif
        }
        level ??= levelElevationMm.HasValue
            ? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - levelElevationMm.Value / MmPerFoot)).FirstOrDefault()
            : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();

        return level != null ? ViewPlan.Create(doc, vft.Id, level.Id) : null;
    }

    private static ViewSection? CreateSectionView(Document doc, JObject input)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
        if (vft == null) return null;

        var originX = (input["originX"]?.Value<double>() ?? 0) / MmPerFoot;
        var originY = (input["originY"]?.Value<double>() ?? 0) / MmPerFoot;
        var originZ = (input["originZ"]?.Value<double>() ?? 0) / MmPerFoot;
        var widthMm = input["width"]?.Value<double>() ?? 10000;
        var heightMm = input["height"]?.Value<double>() ?? 5000;
        var depthMm = input["depth"]?.Value<double>() ?? 5000;

        var halfW = widthMm / MmPerFoot / 2.0;
        var halfH = heightMm / MmPerFoot / 2.0;
        var d = depthMm / MmPerFoot;

        var bb = new BoundingBoxXYZ
        {
            Min = new XYZ(-halfW, -halfH, 0),
            Max = new XYZ(halfW, halfH, d)
        };

        var direction = input["direction"]?.Value<string>() ?? "north";
        var origin = new XYZ(originX, originY, originZ);

        XYZ right, up, viewDir;
        switch (direction.ToLowerInvariant())
        {
            case "south": right = -XYZ.BasisX; up = XYZ.BasisZ; viewDir = XYZ.BasisY; break;
            case "east": right = XYZ.BasisY; up = XYZ.BasisZ; viewDir = -XYZ.BasisX; break;
            case "west": right = -XYZ.BasisY; up = XYZ.BasisZ; viewDir = XYZ.BasisX; break;
            default: right = XYZ.BasisX; up = XYZ.BasisZ; viewDir = -XYZ.BasisY; break; // north
        }

        var transform = Transform.Identity;
        transform.Origin = origin;
        transform.BasisX = right;
        transform.BasisY = up;
        transform.BasisZ = viewDir;
        bb.Transform = transform;

        return ViewSection.CreateSection(doc, vft.Id, bb);
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

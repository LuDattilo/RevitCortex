using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Creates a new view (floor plan, ceiling plan, section, elevation, 3D).
/// </summary>
[ToolSafety(false, false)]
public class CreateViewTool : ICortexTool
{
    public string Name => "create_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a new view (floor plan, ceiling plan, section, elevation, 3D).";
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
        var levelName = input["levelName"]?.Value<string>();

        try
        {
            using var tx = new Transaction(doc, "RevitCortex: Create View");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            View? createdView = null;

            switch (viewType.ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            {
                case "floorplan":
                case "floor":
                    createdView = CreatePlanView(doc, ViewFamily.FloorPlan, levelId, levelElevationMm, levelName);
                    break;
                case "ceilingplan":
                case "ceiling":
                    createdView = CreatePlanView(doc, ViewFamily.CeilingPlan, levelId, levelElevationMm, levelName);
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
                case "elevation":
                    createdView = CreateElevationView(doc, input);
                    break;
                case "drafting":
                case "draftingview":
                    var vftDraft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (vftDraft != null) createdView = ViewDrafting.Create(doc, vftDraft.Id);
                    break;
                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unsupported viewType: {viewType}",
                        suggestion: "Use: floorplan, ceilingplan, section, elevation, drafting, 3d");
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
                createdView.DetailLevel = detailLevel!.ToLowerInvariant() switch
                {
                    "fine" => ViewDetailLevel.Fine,
                    "medium" => ViewDetailLevel.Medium,
                    _ => ViewDetailLevel.Coarse
                };
            }

            // Optional crop box (mm bounds in the view's coordinate system) + crop toggle
            ApplyCropBox(createdView, input);

            // Optional view template by id or name
            ApplyViewTemplate(doc, createdView, input);

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                viewId = ToolHelpers.GetElementIdValue(createdView.Id),
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

    private static ViewPlan? CreatePlanView(Document doc, ViewFamily family, long levelIdLong, double? levelElevationMm, string? levelName)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == family);
        if (vft == null) return null;

        var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

        Level? level = null;
        if (levelIdLong > 0)
        {
#if REVIT2024_OR_GREATER
            level = doc.GetElement(new ElementId(levelIdLong)) as Level;
#else
            level = doc.GetElement(new ElementId((int)levelIdLong)) as Level;
#endif
        }
        level ??= !string.IsNullOrEmpty(levelName)
            ? allLevels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase))
            : levelElevationMm.HasValue
                ? allLevels.OrderBy(l => Math.Abs(l.Elevation - levelElevationMm.Value / MmPerFoot)).FirstOrDefault()
                : null;

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

    private static ViewSection? CreateElevationView(Document doc, JObject input)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
        if (vft == null) return null;

        // An elevation needs a marker placed on a plan view at an origin point.
        var ownerPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate);
        if (ownerPlan == null) return null;

        var originX = (input["originX"]?.Value<double>() ?? 0) / MmPerFoot;
        var originY = (input["originY"]?.Value<double>() ?? 0) / MmPerFoot;
        var originZ = (input["originZ"]?.Value<double>() ?? 0) / MmPerFoot;
        var origin = new XYZ(originX, originY, originZ);
        var scale = input["scale"]?.Value<int?>() ?? 100;

        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale);
        // Marker indices run 0..3 around the marker; map a cardinal direction onto one.
        var dirStr = (input["direction"]?.Value<string>() ?? "north").ToLowerInvariant();
        int index = dirStr switch { "east" => 1, "south" => 2, "west" => 3, _ => 0 };
        return marker.CreateElevation(doc, ownerPlan.Id, index);
    }

    /// <summary>
    /// Applies an optional crop box. cropActive toggles cropping; cropMin/cropMax (mm,
    /// {x,y} in the view plane) set the crop rectangle when both are supplied.
    /// </summary>
    private static void ApplyCropBox(View view, JObject input)
    {
        var cropActive = input["cropActive"]?.Value<bool?>();
        if (cropActive.HasValue)
        {
            view.CropBoxActive = cropActive.Value;
            view.CropBoxVisible = cropActive.Value;
        }

        var minToken = input["cropMin"];
        var maxToken = input["cropMax"];
        if (minToken == null || maxToken == null) return;

        try
        {
            var current = view.CropBox; // keep Z range + transform; only swap XY extents
            var minX = (minToken["x"]?.Value<double>() ?? 0) / MmPerFoot;
            var minY = (minToken["y"]?.Value<double>() ?? 0) / MmPerFoot;
            var maxX = (maxToken["x"]?.Value<double>() ?? 0) / MmPerFoot;
            var maxY = (maxToken["y"]?.Value<double>() ?? 0) / MmPerFoot;

            current.Min = new XYZ(minX, minY, current.Min.Z);
            current.Max = new XYZ(maxX, maxY, current.Max.Z);
            view.CropBox = current;
            view.CropBoxActive = true;
            view.CropBoxVisible = true;
        }
        catch { /* some view types reject an explicit crop box; ignore */ }
    }

    /// <summary>Applies an optional view template by templateId or templateName.</summary>
    private static void ApplyViewTemplate(Document doc, View view, JObject input)
    {
        var templateIdLong = input["templateId"]?.Value<long?>() ?? 0;
        var templateName = input["templateName"]?.Value<string>();

        View? template = null;
        if (templateIdLong > 0)
            template = doc.GetElement(ToolHelpers.ToElementId(templateIdLong)) as View;
        if (template == null && !string.IsNullOrEmpty(templateName))
            template = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));

        if (template != null && template.IsTemplate)
        {
            try { view.ViewTemplateId = template.Id; } catch { /* incompatible template type */ }
        }
    }
}

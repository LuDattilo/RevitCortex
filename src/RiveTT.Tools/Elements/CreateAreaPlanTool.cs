using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Builds SHAB/SU/SDP-style regulatory surfaces: area schemes, area plans, area
/// boundary lines, and Area elements — the "plans de surface" gap create_room cannot
/// close (a Room is a spatial container, not a regulatory area calculation).
///
/// AreaScheme itself has no public creation API (confirmed: the Revit API team's own
/// tracked idea request for one is still open) — action=duplicate_scheme works around
/// it with ElementTransformUtils.CopyElement on an existing scheme (every template
/// ships "Gross Building" and usually "Rentable"), which is the workaround Autodesk's
/// own forum recommends. Everything else (area plan, boundary lines, areas) has a
/// real, verified creation API.
/// </summary>
[ToolSafety(false, false)]
public class CreateAreaPlanTool : ICortexTool
{
    public string Name => "manage_area_plans";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Builds regulatory area surfaces (SHAB/SU/SDP): area schemes, area plan views, area boundary lines, " +
        "and Area elements. action=list_schemes|duplicate_scheme|create_plan|create_boundary|create_area. " +
        "AreaScheme creation from scratch is confirmed unsupported by the public Revit API: duplicate_scheme " +
        "copies an existing one (every template ships 'Gross Building') instead. create_plan needs " +
        "areaSchemeId+levelId. create_boundary needs viewId+curves (closed loop, mm). create_area needs " +
        "viewId+point ({x,y} mm, inside a closed boundary).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list_schemes").ToLowerInvariant();
        try
        {
            return action switch
            {
                "list_schemes" => ListSchemes(doc),
                "duplicate_scheme" => DuplicateScheme(doc, input),
                "create_plan" => CreatePlan(doc, input),
                "create_boundary" => CreateBoundary(doc, input),
                "create_area" => CreateArea(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported action: {action}",
                    suggestion: "Use: list_schemes | duplicate_scheme | create_plan | create_boundary | create_area")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListSchemes(Document doc)
    {
        var schemes = new FilteredElementCollector(doc)
            .OfClass(typeof(AreaScheme))
            .Cast<AreaScheme>()
            .Select(s => new { id = ToolHelpers.GetElementIdValue(s.Id), name = s.Name })
            .ToList();
        return CortexResult<object>.Ok(new { count = schemes.Count, areaSchemes = schemes });
    }

    private static CortexResult<object> DuplicateScheme(Document doc, JObject input)
    {
        var sourceIdLong = input["sourceSchemeId"]?.Value<long?>() ?? 0;
        var newName = input["newName"]?.Value<string>();
        if (sourceIdLong <= 0 || string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "sourceSchemeId and newName are required",
                suggestion: "List existing schemes with action=list_schemes first");

        var source = doc.GetElement(ToolHelpers.ToElementId(sourceIdLong)) as AreaScheme;
        if (source == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{sourceIdLong} is not an AreaScheme");

        using var tx = new Transaction(doc, "RiveTT: Duplicate Area Scheme");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var copiedIds = ElementTransformUtils.CopyElement(doc, source.Id, XYZ.Zero);
        var copy = copiedIds.Select(doc.GetElement).OfType<AreaScheme>().FirstOrDefault();
        if (copy == null)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "ElementTransformUtils.CopyElement did not return an AreaScheme");
        }

        try { copy.Name = newName!; }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not rename the copy to '{newName}': {ex.Message}",
                suggestion: "Pick a name not already used by another area scheme");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { id = ToolHelpers.GetElementIdValue(copy.Id), name = copy.Name });
    }

    private static CortexResult<object> CreatePlan(Document doc, JObject input)
    {
        var areaSchemeIdLong = input["areaSchemeId"]?.Value<long?>() ?? 0;
        var levelIdLong = input["levelId"]?.Value<long?>() ?? 0;
        if (areaSchemeIdLong <= 0 || levelIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "areaSchemeId and levelId are required");

        var schemeId = ToolHelpers.ToElementId(areaSchemeIdLong);
        var levelId = ToolHelpers.ToElementId(levelIdLong);
        if (doc.GetElement(schemeId) is not AreaScheme)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{areaSchemeIdLong} is not an AreaScheme");
        if (doc.GetElement(levelId) is not Level)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{levelIdLong} is not a Level");

        using var tx = new Transaction(doc, "RiveTT: Create Area Plan");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        ViewPlan plan;
        try
        {
            plan = ViewPlan.CreateAreaPlan(doc, schemeId, levelId);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"ViewPlan.CreateAreaPlan failed: {ex.Message}",
                suggestion: "An area plan for this scheme+level combination may already exist");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new
        {
            viewId = ToolHelpers.GetElementIdValue(plan.Id),
            viewName = plan.Name
        });
    }

    private static CortexResult<object> CreateBoundary(Document doc, JObject input)
    {
        var viewIdLong = input["viewId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (viewIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "viewId and a non-empty curves array are required",
                suggestion: "Provide {\"viewId\":123, \"curves\":[{\"type\":\"line\",\"start\":{...},\"end\":{...}}, ...]} forming a closed loop");

        var view = doc.GetElement(ToolHelpers.ToElementId(viewIdLong)) as ViewPlan;
        if (view == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{viewIdLong} is not a plan view");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, curveError);

        var plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, view.GenLevel?.Elevation ?? 0)));

        using var tx = new Transaction(doc, "RiveTT: Create Area Boundary");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var createdIds = new List<long>();
        var warnings = new List<string>();
        foreach (var curve in curves)
        {
            try
            {
                var mc = doc.Create.NewAreaBoundaryLine(plane, curve, view);
                if (mc != null) createdIds.Add(ToolHelpers.GetElementIdValue(mc.Id));
            }
            catch (Exception ex)
            {
                warnings.Add($"Segment failed: {ex.Message}");
            }
        }

        if (createdIds.Count == 0)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "No boundary line segment was created", suggestion: string.Join("; ", warnings));
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { createdCount = createdIds.Count, createdIds, warnings });
    }

    private static CortexResult<object> CreateArea(Document doc, JObject input)
    {
        var viewIdLong = input["viewId"]?.Value<long?>() ?? 0;
        var pointToken = input["point"];
        if (viewIdLong <= 0 || pointToken == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "viewId and point ({x,y} in mm, inside a closed area boundary) are required");

        var view = doc.GetElement(ToolHelpers.ToElementId(viewIdLong)) as ViewPlan;
        if (view == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{viewIdLong} is not a plan view");

        var uv = new UV(
            (pointToken["x"]?.Value<double>() ?? 0) / MmPerFoot,
            (pointToken["y"]?.Value<double>() ?? 0) / MmPerFoot);

        using var tx = new Transaction(doc, "RiveTT: Create Area");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        Area area;
        try
        {
            area = doc.Create.NewArea(view, uv);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"NewArea failed: {ex.Message}",
                suggestion: "The point must fall inside a closed loop of area boundary lines in this view " +
                            "(create_boundary first).");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        double areaM2 = 0;
        try { areaM2 = (area.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) * 0.09290304; } catch { }

        return CortexResult<object>.Ok(new
        {
            areaId = ToolHelpers.GetElementIdValue(area.Id),
            areaM2 = Math.Round(areaM2, 2)
        });
    }
}

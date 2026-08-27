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
/// Pure curve elements — detail lines, model lines and room separation lines.
///
/// Why these are separate tools: create_line_based_element resolves a FamilySymbol
/// for every request, so any category without family types (OST_Lines,
/// OST_RoomSeparationLines, view-owned detail lines) failed with
/// "No family types available for category ...". Those categories are created
/// through Autodesk.Revit.Creation.Document instead, with a sketch plane rather
/// than a type.
/// </summary>
internal static class CurveInput
{

    /// <summary>
    /// Reads <c>[{x,y,z}, ...]</c> (mm) or <c>{p0:{...},p1:{...}}</c> (mm) into
    /// consecutive Revit lines in internal units.
    /// </summary>
    internal static bool TryReadPolyline(
        JToken? token, out List<Line> lines, out XYZ? origin, out string error)
    {
        lines = new List<Line>();
        origin = null;
        error = "";

        var points = new List<XYZ>();
        if (token is JArray array)
        {
            foreach (var item in array)
            {
                if (!TryReadPoint(item, out var point, out error)) return false;
                points.Add(point!);
            }
        }
        else if (token is JObject obj && obj["p0"] != null && obj["p1"] != null)
        {
            if (!TryReadPoint(obj["p0"], out var start, out error)) return false;
            if (!TryReadPoint(obj["p1"], out var end, out error)) return false;
            points.Add(start!);
            points.Add(end!);
        }
        else
        {
            error = "path must be [{x,y,z}, ...] in mm, or {p0:{x,y,z}, p1:{x,y,z}} in mm";
            return false;
        }

        if (points.Count < 2)
        {
            error = "path needs at least two points";
            return false;
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].DistanceTo(points[i - 1]) < 1e-6)
            {
                error = $"segment {i} is shorter than Revit's minimum curve length (points {i - 1} and {i} coincide)";
                return false;
            }
            lines.Add(Line.CreateBound(points[i - 1], points[i]));
        }

        origin = points[0];
        return true;
    }

    private static bool TryReadPoint(JToken? token, out XYZ? point, out string error)
    {
        point = null;
        error = "";
        if (token is not JObject obj || obj["x"] == null || obj["y"] == null)
        {
            error = "each point must be {x, y, z} in mm";
            return false;
        }

        point = new XYZ(
            obj["x"]!.Value<double>() / MmPerFoot,
            obj["y"]!.Value<double>() / MmPerFoot,
            (obj["z"]?.Value<double>() ?? 0) / MmPerFoot);
        return true;
    }

    internal static SketchPlane CreateHorizontalSketchPlane(Document doc, double elevationFt)
    {
        var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevationFt));
        return SketchPlane.Create(doc, plane);
    }

    internal static View? ResolveView(Document doc, long viewId)
    {
        if (viewId > 0)
        {
            return doc.GetElement(new ElementId(viewId)) as View;
        }

        return doc.ActiveView;
    }
}

/// <summary>Draws detail lines (view-owned 2D lines) in a specific view.</summary>
[ToolSafety(false, false)]
public sealed class CreateDetailLineTool : ICortexTool
{
    public string Name => "create_detail_line";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Draws 2D detail lines in a view (OST_Lines, view-owned). Path is [{x,y,z}, ...] in mm. " +
        "Detail lines belong to one view only; use create_model_line for 3D model lines.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var lineStyleName = input["lineStyleName"]?.Value<string>();

        if (!CurveInput.TryReadPolyline(input["path"], out var lines, out _, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, pathError);

        var view = CurveInput.ResolveView(doc, viewId);
        if (view == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                viewId > 0 ? $"View {viewId} not found" : "No active view");

        if (view.ViewType == ViewType.ThreeD)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Detail lines cannot be drawn in a 3D view",
                suggestion: "Target a plan, section, elevation, drafting or detail view, or use create_model_line.");

        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: {lines.Count} detail line(s) would be drawn in view '{view.Name}'.",
                segmentCount = lines.Count,
                viewId = ToolHelpers.GetElementIdValue(view.Id),
                viewName = view.Name,
                lineStyleName
            });

        try
        {
            using var tx = new Transaction(doc, "RiveTT: Create Detail Lines");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var created = new List<long>();
            GraphicsStyle? style = ResolveLineStyle(doc, lineStyleName);

            foreach (var line in lines)
            {
                var detailCurve = doc.Create.NewDetailCurve(view, line);
                if (style != null) detailCurve.LineStyle = style;
                created.Add(ToolHelpers.GetElementIdValue(detailCurve.Id));
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

            return CortexResult<object>.Ok(new
            {
                message = $"Created {created.Count} detail line(s) in view '{view.Name}'.",
                createdElementIds = created,
                createdCount = created.Count,
                viewId = ToolHelpers.GetElementIdValue(view.Id),
                appliedLineStyle = style?.Name
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create detail lines: {exception.Message}");
        }
    }

    internal static GraphicsStyle? ResolveLineStyle(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(GraphicsStyle))
            .Cast<GraphicsStyle>()
            .FirstOrDefault(style =>
                ParameterNameResolver.Normalize(style.Name) == ParameterNameResolver.Normalize(name!));
    }
}

/// <summary>Draws model lines (3D lines visible in every view) on a horizontal sketch plane.</summary>
[ToolSafety(false, false)]
public sealed class CreateModelLineTool : ICortexTool
{
    public string Name => "create_model_line";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Draws 3D model lines (OST_Lines) on a horizontal sketch plane at the given elevation. " +
        "Path is [{x,y,z}, ...] in mm; z of the first point sets the plane elevation.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var lineStyleName = input["lineStyleName"]?.Value<string>();

        if (!CurveInput.TryReadPolyline(input["path"], out var lines, out var origin, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, pathError);

        var elevationFt = origin!.Z;
        if (lines.Any(line => Math.Abs(line.GetEndPoint(0).Z - elevationFt) > 1e-6 ||
                              Math.Abs(line.GetEndPoint(1).Z - elevationFt) > 1e-6))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "All points must share the same z: model lines are created on one horizontal sketch plane.",
                suggestion: "Split the path into one call per elevation.");

        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: {lines.Count} model line(s) would be created at z={elevationFt * MmPerFoot:F0} mm.",
                segmentCount = lines.Count,
                elevationMm = elevationFt * MmPerFoot,
                lineStyleName
            });

        try
        {
            using var tx = new Transaction(doc, "RiveTT: Create Model Lines");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var sketchPlane = CurveInput.CreateHorizontalSketchPlane(doc, elevationFt);
            var style = CreateDetailLineTool.ResolveLineStyle(doc, lineStyleName);
            var created = new List<long>();

            foreach (var line in lines)
            {
                var modelCurve = doc.Create.NewModelCurve(line, sketchPlane);
                if (style != null) modelCurve.LineStyle = style;
                created.Add(ToolHelpers.GetElementIdValue(modelCurve.Id));
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

            return CortexResult<object>.Ok(new
            {
                message = $"Created {created.Count} model line(s) at z={elevationFt * MmPerFoot:F0} mm.",
                createdElementIds = created,
                createdCount = created.Count,
                elevationMm = elevationFt * MmPerFoot,
                appliedLineStyle = style?.Name
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create model lines: {exception.Message}");
        }
    }
}

/// <summary>
/// Draws room separation lines — the correct way to split a room without building
/// a physical wall.
/// </summary>
[ToolSafety(false, false)]
public sealed class CreateRoomSeparationLineTool : ICortexTool
{
    public string Name => "create_room_separation_line";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Draws room separation lines (OST_RoomSeparationLines) in a plan view, to split or bound rooms " +
        "without a physical wall. Path is [{x,y,z}, ...] in mm; z sets the sketch plane elevation.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var viewId = input["viewId"]?.Value<long>() ?? 0;

        if (!CurveInput.TryReadPolyline(input["path"], out var lines, out var origin, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, pathError);

        var view = CurveInput.ResolveView(doc, viewId);
        if (view is not ViewPlan plan)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                viewId > 0
                    ? $"View {viewId} is not a plan view; room separation lines are plan-only."
                    : "The active view is not a plan view; room separation lines are plan-only.",
                suggestion: "Pass viewId of a floor plan or area plan view.");

        var elevationFt = origin!.Z;
        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: {lines.Count} room separation line(s) would be drawn in plan '{plan.Name}'.",
                segmentCount = lines.Count,
                viewId = ToolHelpers.GetElementIdValue(plan.Id),
                viewName = plan.Name,
                elevationMm = elevationFt * MmPerFoot
            });

        try
        {
            using var tx = new Transaction(doc, "RiveTT: Create Room Separation Lines");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var sketchPlane = CurveInput.CreateHorizontalSketchPlane(doc, elevationFt);
            var curveArray = new CurveArray();
            foreach (var line in lines) curveArray.Append(line);

            var createdCurves = doc.Create.NewRoomBoundaryLines(sketchPlane, curveArray, plan);
            var created = createdCurves
                .Cast<ModelCurve>()
                .Select(curve => ToolHelpers.GetElementIdValue(curve.Id))
                .ToList();

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

            return CortexResult<object>.Ok(new
            {
                message = $"Created {created.Count} room separation line(s) in plan '{plan.Name}'. " +
                          "Rooms re-compute their boundaries on the next regeneration.",
                createdElementIds = created,
                createdCount = created.Count,
                viewId = ToolHelpers.GetElementIdValue(plan.Id)
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create room separation lines: {exception.Message}");
        }
    }
}

/// <summary>
/// Places a title block instance on an existing sheet — the repair path for sheets
/// created before create_sheet honored titleBlockId.
/// </summary>
[ToolSafety(false, false)]
public sealed class PlaceTitleBlockTool : ICortexTool
{
    public string Name => "place_title_block";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Places a title block instance on an existing sheet. Use it to repair a sheet created without " +
        "a title block; call with no titleBlockId to list the title blocks available in the document.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var titleBlockId = input["titleBlockId"]?.Value<long>()
                           ?? input["titleBlockTypeId"]?.Value<long>()
                           ?? 0;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId is required");

        var sheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
        if (sheet == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Element {sheetId} is not a sheet (ViewSheet)");

        var available = Project.CreateSheetTool.ListTitleBlocks(doc);
        if (titleBlockId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "titleBlockId is required",
                suggestion: "Pick one of the ids listed in availableTitleBlocks.",
                context: new Dictionary<string, object> { ["availableTitleBlocks"] = available });

        var symbol = doc.GetElement(new ElementId(titleBlockId)) as FamilySymbol;
        if (symbol == null || symbol.Category?.Id != new ElementId(BuiltInCategory.OST_TitleBlocks))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"titleBlockId {titleBlockId} is not an OST_TitleBlocks family type",
                context: new Dictionary<string, object> { ["availableTitleBlocks"] = available });

        var existing = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .ToList();

        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                message = existing.Count > 0
                    ? $"DryRun: sheet '{sheet.SheetNumber} - {sheet.Name}' already carries {existing.Count} title block instance(s); a second one would be added."
                    : $"DryRun: '{symbol.FamilyName} / {symbol.Name}' would be placed on sheet '{sheet.SheetNumber} - {sheet.Name}'.",
                sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                titleBlockId,
                existingTitleBlockCount = existing.Count
            });

        try
        {
            using var tx = new Transaction(doc, "RiveTT: Place Title Block");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            var instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol, sheet);

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

            return CortexResult<object>.Ok(new
            {
                message = $"Placed '{symbol.FamilyName} / {symbol.Name}' on sheet '{sheet.SheetNumber} - {sheet.Name}'.",
                sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                titleBlockInstanceId = ToolHelpers.GetElementIdValue(instance.Id),
                titleBlockId
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to place title block: {exception.Message}");
        }
    }
}

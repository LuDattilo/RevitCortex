using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates a shape-driven rebar in a host from a RebarShape (stable across all Revit versions).
/// Input (mm): hostId, shapeId|shapeName, barTypeId|barTypeName, origin{x,y,z}, xVec{x,y,z}, yVec{x,y,z},
/// optional layout{...}. Returns created rebar id + applied layout.
/// </summary>
public class CreateRebarFromShapeTool : ICortexTool
{
    public string Name => "create_rebar_from_shape";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a shape-driven rebar in a host from a rebar shape. Provide hostId, shapeId|shapeName, barTypeId|barTypeName, and origin/xVec/yVec (mm). Optional layout spec sets spacing/number.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        var shape = RebarToolHelpers.ResolveRebarShape(doc!, input["shapeId"]?.Value<long?>(), input["shapeName"]?.Value<string>());
        if (shape == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No rebar shape resolved", suggestion: "Use list_rebar_shapes to find a shapeId");

        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No rebar bar type resolved", suggestion: "Use list_rebar_bar_types to find a barTypeId");

        if (input["origin"] == null || input["xVec"] == null || input["yVec"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "origin, xVec and yVec are required (mm / direction vectors)");

        var origin = RebarToolHelpers.ParseXyzMm(input["origin"]!);
        var xVec = RebarToolHelpers.ParseXyzMm(input["xVec"]!).Normalize();
        var yVec = RebarToolHelpers.ParseXyzMm(input["yVec"]!).Normalize();

        RebarToolHelpers.LayoutSpec? layout = null;
        if (input["layout"] is JObject layoutObj)
        {
            layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
            if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);
        }

        if (!session.RequestConfirmation("create rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Rebar From Shape");
        tx.Start();
        try
        {
            var rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromRebarShape(
                doc!, shape, barType, host!, origin, xVec, yVec);
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit returned no rebar (shape/host/plane may be incompatible)");
            }

            string? appliedLayout = null;
            if (layout != null && rebar.IsRebarShapeDriven())
            {
                var acc = rebar.GetShapeDrivenAccessor();
                RebarToolHelpers.ApplyLayout(acc, layout);
                appliedLayout = layout.Rule.ToString();
            }

            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created rebar {id} in host {ToolHelpers.GetElementIdValue(host!)}",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                barTypeId = ToolHelpers.GetElementIdValue(barType),
                barTypeName = barType.Name,
                shapeId = ToolHelpers.GetElementIdValue(shape),
                appliedLayout
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to create rebar: {ex.Message}");
        }
    }
}

/// <summary>
/// Creates a rebar from explicit curves (mm) in a host. Curves must be coplanar; 'normal' is the
/// plane normal. Hooks optional via startHookId/endHookId. Version-aware: uses BarTerminationsData
/// on Revit 2026+, the legacy hook overload on 2023-2025.
/// </summary>
public class CreateRebarFromCurvesTool : ICortexTool
{
    public string Name => "create_rebar_from_curves";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a rebar from explicit coplanar curves (mm) in a host. Provide hostId, barTypeId|barTypeName, curves[], normal{x,y,z}, style (Standard|StirrupTie), optional startHookId/endHookId and layout.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved",
                suggestion: "Use list_rebar_bar_types to find a barTypeId");

        if (!(input["curves"] is JArray curvesArr))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "curves array is required");
        var curves = RebarToolHelpers.ParseCurveSpecsMm(curvesArr, out var cerr);
        if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);

        if (input["normal"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "normal{x,y,z} is required");
        var normal = RebarToolHelpers.ParseXyzMm(input["normal"]!).Normalize();

        var style = RebarToolHelpers.ParseEnum<RebarStyle>(input["style"]?.Value<string>() ?? "Standard", "style", out var serr);
        if (serr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, serr);

        var startHook = RebarToolHelpers.ResolveRebarHookType(doc!, input["startHookId"]?.Value<long?>(), input["startHookName"]?.Value<string>());
        var endHook = RebarToolHelpers.ResolveRebarHookType(doc!, input["endHookId"]?.Value<long?>(), input["endHookName"]?.Value<string>());

        RebarToolHelpers.LayoutSpec? layout = null;
        if (input["layout"] is JObject layoutObj)
        {
            layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
            if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);
        }

        if (!session.RequestConfirmation("create rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Rebar From Curves");
        tx.Start();
        try
        {
            Autodesk.Revit.DB.Structure.Rebar? rebar;
#if REVIT2026_OR_GREATER
            // Verified against the 2026/2027 ref assemblies (Nice3point 2026.4.10 / 2027.0.20):
            //   ctor is BarTerminationsData(Document) — NOT (RebarBarType);
            //   hooks are set via the ElementId properties HookTypeIdAtStart/HookTypeIdAtEnd —
            //   there is no SetHook(int, ElementId) method.
            //   CreateFromCurves overload: (Document, RebarStyle, RebarBarType, Element, XYZ,
            //   IList<Curve>, BarTerminationsData, bool, bool) -> Rebar.
            var terminations = new BarTerminationsData(doc!);
            if (startHook != null) terminations.HookTypeIdAtStart = startHook.Id;
            if (endHook != null) terminations.HookTypeIdAtEnd = endHook.Id;
            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                doc!, style, barType, host!, normal, curves, terminations,
                useExistingShapeIfPossible: true, createNewShape: true);
#else
            // Legacy hook-orientation overload, verified present on the 2023/2024/2025 ref
            // assemblies: (Document, RebarStyle, RebarBarType, RebarHookType, RebarHookType,
            // Element, XYZ, IList<Curve>, RebarHookOrientation, RebarHookOrientation, bool, bool).
            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                doc!, style, barType, startHook, endHook, host!, normal, curves,
                RebarHookOrientation.Right, RebarHookOrientation.Right,
                useExistingShapeIfPossible: true, createNewShape: true);
#endif
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit returned no rebar (curves may be non-coplanar or invalid for the host)");
            }

            string? appliedLayout = null;
            if (layout != null && rebar.IsRebarShapeDriven())
            {
                RebarToolHelpers.ApplyLayout(rebar.GetShapeDrivenAccessor(), layout);
                appliedLayout = layout.Rule.ToString();
            }

            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created rebar {id} from {curves.Count} curve(s)",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                barTypeId = ToolHelpers.GetElementIdValue(barType),
                appliedLayout
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to create rebar from curves: {ex.Message}");
        }
    }
}

/// <summary>
/// Creates an unconstrained free-form rebar from one or more curve loops (mm) in a host.
/// Does NOT accept arbitrary server code — only the unconstrained curve-loop path.
/// </summary>
public class CreateFreeFormRebarTool : ICortexTool
{
    public string Name => "create_free_form_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create an unconstrained free-form rebar from curve loops (mm) in a host. Provide hostId, barTypeId|barTypeName, style, and loops: an array of loops, each an array of curve specs.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved");
        if (!(input["loops"] is JArray loopsArr) || loopsArr.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "loops (array of curve-spec arrays) is required");

        var loops = new List<CurveLoop>();
        foreach (var loopTok in loopsArr.OfType<JArray>())
        {
            var curves = RebarToolHelpers.ParseCurveSpecsMm(loopTok, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
            var cl = new CurveLoop();
            foreach (var c in curves) cl.Append(c);
            loops.Add(cl);
        }

        var style = RebarToolHelpers.ParseEnum<RebarStyle>(input["style"]?.Value<string>() ?? "Standard", "style", out var serr);
        if (serr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, serr);

        if (!session.RequestConfirmation("create free-form rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Free-Form Rebar");
        tx.Start();
        try
        {
            // CreateFreeForm overloads verified by reflection across all five ref assemblies:
            //   2026/2027: (Document, RebarBarType, Element, IList<CurveLoop>, RebarStyle)
            //              -> RebarFreeFormCreationResult (read .Rebar; .Error is the validation enum).
            //   2023/2024/2025: (Document, RebarBarType, Element, IList<CurveLoop>,
            //              out RebarFreeFormValidationResult) -> Rebar. There is NO RebarStyle
            //              overload before 2026, so 'style' cannot be applied at creation on those
            //              versions (the input is still accepted for forward compatibility).
            Autodesk.Revit.DB.Structure.Rebar? rebar;
#if REVIT2026_OR_GREATER
            var creation = Autodesk.Revit.DB.Structure.Rebar.CreateFreeForm(doc!, barType, host!, loops, style);
            rebar = creation?.Rebar;
#else
            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFreeForm(
                doc!, barType, host!, loops, out RebarFreeFormValidationResult validation);
            if (rebar == null && validation != RebarFreeFormValidationResult.Success)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit could not create free-form rebar: {validation}");
            }
#endif
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no free-form rebar");
            }
            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created free-form rebar {id} from {loops.Count} loop(s)",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!)
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create free-form rebar: {ex.Message}");
        }
    }
}

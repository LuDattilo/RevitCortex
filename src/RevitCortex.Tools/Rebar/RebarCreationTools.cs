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

/// <summary>Re-applies a layout rule to an existing shape-driven rebar.</summary>
public class SetRebarLayoutTool : ICortexTool
{
    public string Name => "set_rebar_layout";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the distribution layout of a shape-driven rebar. Provide rebarId and a layout spec (rule: single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Layout can only be set on shape-driven rebar");
        if (!(input["layout"] is JObject layoutObj))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "layout object is required");
        var layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
        if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);

        if (!session.RequestConfirmation("set rebar layout", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Layout");
        tx.Start();
        try
        {
            RebarToolHelpers.ApplyLayout(rebar.GetShapeDrivenAccessor(), layout);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                appliedLayout = layout.Rule.ToString(),
                numberOfBarPositions = rebar.NumberOfBarPositions
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set layout: {ex.Message}");
        }
    }
}

/// <summary>Changes the RebarShape of a shape-driven rebar.</summary>
public class SetRebarShapeTool : ICortexTool
{
    public string Name => "set_rebar_shape";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Change the shape of a shape-driven rebar. Provide rebarId and shapeId|shapeName.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Shape can only be set on shape-driven rebar");
        var shape = RebarToolHelpers.ResolveRebarShape(doc!, input["shapeId"]?.Value<long?>(), input["shapeName"]?.Value<string>());
        if (shape == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar shape resolved");

        if (!session.RequestConfirmation("set rebar shape", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Shape");
        tx.Start();
        try
        {
            rebar.GetShapeDrivenAccessor().SetRebarShapeId(shape.Id);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), shapeId = ToolHelpers.GetElementIdValue(shape) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set shape: {ex.Message}");
        }
    }
}

/// <summary>Reassigns a rebar to a new valid host.</summary>
public class SetRebarHostTool : ICortexTool
{
    public string Name => "set_rebar_host";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reassign a rebar to a new host. Provide rebarId and newHostId (must be a valid rebar host).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["newHostId"]?.Value<long?>());
        if (herr != null) return herr;

        if (!session.RequestConfirmation("reassign rebar host", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Host");
        tx.Start();
        try
        {
            // SetHostId is (Document, ElementId) on all ref assemblies 2023-2027.
            rebar!.SetHostId(doc!, host!.Id);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), hostId = ToolHelpers.GetElementIdValue(host) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set host: {ex.Message}");
        }
    }
}

/// <summary>Sets unobscured/solid presentation of a rebar in a view (post-2024 API).</summary>
public class SetRebarVisibilityTool : ICortexTool
{
    public string Name => "set_rebar_visibility";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set rebar view presentation. Provide rebarId, viewId, and unobscured (show in front of host). Uses SetUnobscuredInView (SetSolidInView was removed in Revit 2024).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required");
        var view = doc!.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No view with id {viewId}");
        var unobscured = input["unobscured"]?.Value<bool?>() ?? true;

        if (!session.RequestConfirmation("set rebar visibility", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Rebar Visibility");
        tx.Start();
        try
        {
            rebar!.SetUnobscuredInView(view, unobscured);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), viewId, unobscured });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set visibility: {ex.Message}");
        }
    }
}

/// <summary>Sets the hook type at one or both ends of a rebar (works on all Revit versions).</summary>
public class SetRebarHooksTool : ICortexTool
{
    public string Name => "set_rebar_hooks";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the hook type at rebar ends. Provide rebarId, optional startHookId/endHookId (omit or pass 0 to clear an end's hook). Works on all Revit versions.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        bool hasStart = input["startHookId"] != null;
        bool hasEnd = input["endHookId"] != null;
        if (!hasStart && !hasEnd)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Provide startHookId and/or endHookId (0 to clear)");

        if (!session.RequestConfirmation("set rebar hooks", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Hooks");
        tx.Start();
        try
        {
            if (hasStart)
            {
                var sid = input["startHookId"]!.Value<long>();
                rebar!.SetHookTypeId(0, sid > 0 ? ToolHelpers.ToElementId(sid) : ElementId.InvalidElementId);
            }
            if (hasEnd)
            {
                var eid = input["endHookId"]!.Value<long>();
                rebar!.SetHookTypeId(1, eid > 0 ? ToolHelpers.ToElementId(eid) : ElementId.InvalidElementId);
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set hooks: {ex.Message}");
        }
    }
}

/// <summary>Sets full termination data on a rebar end (Revit 2026+ only).</summary>
public class SetRebarTerminationsTool : ICortexTool
{
    public string Name => "set_rebar_terminations";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set rebar end terminations (hook + orientation/rotation). Revit 2026+ only; returns a version error on older targets. Provide rebarId, end (0|1), orientation, rotationDegrees.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
#if REVIT2026_OR_GREATER
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var end = input["end"]?.Value<int?>() ?? 0;
        var orientation = RebarToolHelpers.ParseEnum<RebarTerminationOrientation>(
            input["orientation"]?.Value<string>() ?? "Right", "orientation", out var oerr);
        if (oerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, oerr);
        var rotDeg = input["rotationDegrees"]?.Value<double?>() ?? 0.0;

        if (!session.RequestConfirmation("set rebar terminations", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Terminations");
        tx.Start();
        try
        {
            // Verified on the 2026/2027 ref assemblies: Rebar.SetTerminationOrientation(int, RebarTerminationOrientation)
            // and Rebar.SetTerminationRotationAngle(int, double radians). Neither exists before 2026.
            rebar!.SetTerminationOrientation(end, orientation);
            rebar.SetTerminationRotationAngle(end, rotDeg * Math.PI / 180.0);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), end, orientation = orientation.ToString() });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set terminations: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar terminations", 2026);
#endif
    }
}

/// <summary>Moves a single bar within a shape-driven set.</summary>
public class MoveRebarInSetTool : ICortexTool
{
    public string Name => "move_rebar_in_set";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Move a single bar within a rebar set by a translation vector (mm). Provide rebarId, barPositionIndex, translation{x,y,z}. Pass reset:true to clear a prior move.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var idx = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var reset = input["reset"]?.Value<bool?>() ?? false;

        if (!session.RequestConfirmation("move bar in set", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Move Bar In Set");
        tx.Start();
        try
        {
            if (reset)
            {
                rebar!.ResetMovedBarTransform(idx);
            }
            else
            {
                if (input["translation"] == null)
                    { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "translation{x,y,z} required unless reset:true"); }
                var v = RebarToolHelpers.ParseXyzMm(input["translation"]!);
                rebar!.MoveBarInSet(idx, Transform.CreateTranslation(v));
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), barPositionIndex = idx, reset });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to move bar: {ex.Message}");
        }
    }
}

/// <summary>Shows/hides a single bar of a set in a view.</summary>
public class IncludeExcludeRebarBarsTool : ICortexTool
{
    public string Name => "include_exclude_rebar_bars";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Show or hide a single bar of a rebar set in a view. Provide rebarId, viewId, barPositionIndex, hidden (true=hide).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required");
        var view = doc!.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No view with id {viewId}");
        var idx = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var hidden = input["hidden"]?.Value<bool?>() ?? true;

        if (!session.RequestConfirmation("change bar visibility", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Include/Exclude Bar");
        tx.Start();
        try
        {
            rebar!.SetBarHiddenStatus(view, idx, hidden);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), viewId, barPositionIndex = idx, hidden });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to change bar visibility: {ex.Message}");
        }
    }
}

/// <summary>
/// "Splits" a rebar set by reducing the original to the first N positions and creating a
/// duplicate set for the remaining positions, so each piece can be edited independently.
/// Implemented via ElementTransformUtils copy + layout adjustment (the API has no single Split call).
/// </summary>
public class SplitRebarTool : ICortexTool
{
    public string Name => "split_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Split a shape-driven rebar set into two sets at a given bar position. Provide rebarId and splitAtPosition (1..count-1). Returns the original and new rebar ids.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "split_rebar requires a shape-driven set");
        var total = rebar.NumberOfBarPositions;
        var splitAt = input["splitAtPosition"]?.Value<int?>() ?? 0;
        if (splitAt < 1 || splitAt >= total)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"splitAtPosition must be between 1 and {total - 1} (set has {total} positions)");

        if (!session.RequestConfirmation("split rebar set", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Split Rebar");
        tx.Start();
        try
        {
            var acc = rebar.GetShapeDrivenAccessor();
            var arrayLen = acc.ArrayLength;
            var spacing = total > 1 ? arrayLen / (total - 1) : arrayLen;

            // Duplicate the set.
            var copied = ElementTransformUtils.CopyElement(doc!, rebar.Id, XYZ.Zero);
            var newRebar = doc!.GetElement(copied.First()) as Autodesk.Revit.DB.Structure.Rebar;
            if (newRebar == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Copy did not yield a rebar"); }

            // Original keeps first `splitAt` positions; new keeps the rest, shifted along the normal.
            // SetLayoutAsFixedNumber requires >= 2 positions; use SetLayoutAsSingle for a lone bar.
            if (splitAt == 1)
                acc.SetLayoutAsSingle();
            else
                acc.SetLayoutAsFixedNumber(splitAt, spacing * (splitAt - 1), acc.BarsOnNormalSide, true, true);

            var newAcc = newRebar.GetShapeDrivenAccessor();
            var remaining = total - splitAt;
            // Shift the new set so it begins where the original ends. Normal lives on the
            // shape-driven accessor (not on Rebar) across all ref assemblies 2023-2027.
            var shift = newAcc.Normal.Normalize().Multiply(spacing * splitAt);
            ElementTransformUtils.MoveElement(doc, newRebar.Id, shift);
            // SetLayoutAsFixedNumber requires >= 2 positions; use SetLayoutAsSingle for a lone bar.
            if (remaining == 1)
                newAcc.SetLayoutAsSingle();
            else
                newAcc.SetLayoutAsFixedNumber(remaining, spacing * (remaining - 1), newAcc.BarsOnNormalSide, true, true);

            var origId = ToolHelpers.GetElementIdValue(rebar);
            var newId = ToolHelpers.GetElementIdValue(newRebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Split rebar {origId}: original keeps {splitAt} positions, new {newId} keeps {remaining}",
                originalRebarId = origId,
                newRebarId = newId,
                splitAtPosition = splitAt
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to split rebar: {ex.Message}");
        }
    }
}

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

// =====================================================================================================
// Module 5 — couplers, constraints, propagation, annotations, splices.
//
// API verification (reflected from Nice3point ref assemblies R23/R24/R25/R26/R27 before writing):
//   * RebarCouplerType DOES NOT EXIST as an element class on any version. Couplers are family instances
//     of BuiltInCategory.OST_Coupler; their "types" are FamilySymbols in that category. Resolved via
//     RebarToolHelpers.ResolveCouplerType.
//   * RebarCoupler.Create(Document, ElementId typeId, ReinforcementData first, ReinforcementData second,
//     out RebarCouplerError) — 'second' may be null to cap one bar. Takes the base ReinforcementData type.
//   * RebarReinforcementData.Create(ElementId rebarId, int iEnd) — TWO args only (no barPositionIndex).
//     Exposes RebarId / End. Present on all versions.
//   * Constraints are version-safe on the subset we use: GetRebarConstraintsManager(),
//     RebarConstraintsManager.GetAllHandles()/GetConstraintCandidatesForHandle(handle)/
//     GetPreferredConstraintOnHandle(handle)/SetPreferredConstraintForHandle(handle, c)/
//     RemovePreferredConstraintFromHandle(handle), RebarConstrainedHandle.GetHandleType()/GetHandleName()/
//     GetEdgeNumber()/IsEdgeHandle()/IsCustomHandle() all exist on R23..R27. (GetAutomaticConstraintCandidatesForHandle
//     does NOT exist on any version — the plan's name was wrong; GetConstraintCandidatesForHandle is the real one.)
//   * Propagation: NO rebar-propagation API exists on any version. ReinforcementUtils is an empty static
//     class R24..R27 (absent R23); the only "Propagate*" methods in the assembly are DatumPlane (grids/levels).
//     propagate_rebar therefore returns a structured Fail(InvalidInput) on every target.
//   * Splices (R2025+ only — RebarSpliceUtils & the Rebar splice instance methods are absent on R23/R24):
//       Rebar.GetRebarSplice(int barEnd) -> RebarSplice; Rebar.RemoveSplice(int barEnd);
//       Rebar.GetLapLength(int barEnd); Rebar.GetSpliceStaggerLength(int barEnd);
//       RebarSpliceUtils.SpliceRebar(Document, ElementId, RebarSpliceOptions, IList<RebarSpliceGeometry>);
//       RebarSpliceUtils.GetSpliceGeometries(Document, ElementId, RebarSpliceOptions, RebarSpliceRules) -> RebarSpliceByRulesResult;
//       RebarSpliceUtils.GetSpliceChain(Rebar) -> IList<ElementId>;
//       RebarSpliceUtils.UnifyRebarsIntoOne(Document, ElementId, ElementId) -> ElementId  (used by unify_rebars).
//       RebarSpliceTypeUtils.GetAllRebarSpliceTypes(Document) / CreateRebarSpliceType(Document, string).
//       => unify_rebars is therefore also 2025+ gated (its only API entry point lives on RebarSpliceUtils).
//   * MultiReferenceAnnotation.Create(Document, ElementId ownerViewId, MultiReferenceAnnotationOptions) and
//     MultiReferenceAnnotationType.CreateDefault(Document) exist on ALL versions (transfer_rebar_annotations).
// =====================================================================================================

/// <summary>
/// Version-bridging helpers for the rebar constraints API. The candidate-listing and set-preferred
/// methods diverge across targets (verified by reflection):
///   * GetConstraintCandidatesForHandle(handle) [1-arg]    : R23/R24/R25 ONLY (removed in R26+).
///   * GetConstraintCandidatesForHandle(handle, ElementId)  : ALL versions (use InvalidElementId = all).
///   * SetPreferredConstraintForHandle(handle, c)           : R23/R24/R25 ONLY (removed in R26+).
///   * SetPreferredConstraint(c)                            : R25/R26/R27 ONLY (added in R25).
/// We bridge each so the tools compile and run on all 5 targets.
/// </summary>
internal static class RebarConstraintCompat
{
    public static IList<RebarConstraint> GetCandidates(RebarConstraintsManager mgr, RebarConstrainedHandle handle)
    {
#if REVIT2025_OR_GREATER
        // The (handle, ElementId) overload is non-obsolete on R25 and is the only form on R26+ (1-arg removed there).
        // Using it on R25/R26/R27 avoids the R25 CS0618 deprecation; passing InvalidElementId requests candidates
        // without restricting to a specific target element. R23/R24 keep the (non-obsolete) 1-arg form below.
        return mgr.GetConstraintCandidatesForHandle(handle, ElementId.InvalidElementId);
#else
        return mgr.GetConstraintCandidatesForHandle(handle);
#endif
    }

    public static void SetPreferred(RebarConstraintsManager mgr, RebarConstrainedHandle handle, RebarConstraint constraint)
    {
#if REVIT2025_OR_GREATER
        // SetPreferredConstraint(constraint) exists on R25/R26/R27. The constraint already carries its handle.
        mgr.SetPreferredConstraint(constraint);
#else
        // R23/R24: the per-handle setter is the only option.
        mgr.SetPreferredConstraintForHandle(handle, constraint);
#endif
    }
}

/// <summary>
/// Creates a rebar coupler connecting two bar ends, or caps a single bar end. Provide a coupler type
/// (a FamilySymbol of category OST_Coupler) and one or two reinforcement-data descriptors {rebarId, end}.
/// </summary>
public class CreateRebarCouplerTool : ICortexTool
{
    public string Name => "create_rebar_coupler";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a rebar coupler connecting two bar ends (or cap one). Provide couplerTypeId|couplerTypeName (a Coupler-category family type), end1{rebarId,end}, optional end2{rebarId,end}. 'end' is 0 or 1.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        // RebarCouplerType does not exist; resolve the coupler type as an OST_Coupler FamilySymbol.
        var couplerType = RebarToolHelpers.ResolveCouplerType(doc!,
            input["couplerTypeId"]?.Value<long?>(), input["couplerTypeName"]?.Value<string>());
        if (couplerType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No coupler type resolved (no FamilySymbol in the Coupler category / OST_Coupler).",
                suggestion: "Load a rebar coupler family first, then pass its type id via couplerTypeId.");

        var (d1, e1) = ParseReinforcementData(doc!, input["end1"], "end1");
        if (e1 != null) return e1;
        var (d2, e2) = ParseReinforcementData(doc!, input["end2"], "end2", optional: true);
        if (e2 != null) return e2;

        if (!session.RequestConfirmation("create rebar coupler", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        // Activate the coupler symbol if needed (must be inside the transaction).
        using var tx = new Transaction(doc, "RevitCortex: Create Rebar Coupler");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            if (!couplerType.IsActive) couplerType.Activate();

            // RebarCoupler.Create takes the base ReinforcementData type; d2 may be null to cap one bar.
            var coupler = RebarCoupler.Create(doc, couplerType.Id, d1, d2, out RebarCouplerError err);
            if (coupler == null || err != RebarCouplerError.ValidationSuccessfuly)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Coupler not created (validation: {err}).",
                    suggestion: "Ensure both bar ends are touching, parallel and of compatible diameter for this coupler type.");
            }
            var id = ToolHelpers.GetElementIdValue(coupler);
            var mark = coupler.CouplerMark;
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = d2 != null ? $"Created coupler {id} linking two bars" : $"Created coupler {id} capping one bar",
                couplerId = id,
                couplerMark = mark,
                linkedTwoBars = d2 != null
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create coupler: {ex.Message}");
        }
    }

    // Parses {rebarId, end} into a RebarReinforcementData (validating the rebar exists).
    private static (RebarReinforcementData? data, CortexResult<object>? error) ParseReinforcementData(
        Document doc, JToken? token, string field, bool optional = false)
    {
        if (token is not JObject o)
        {
            if (optional) return (null, null);
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{field}{{rebarId,end}} is required"));
        }
        var rid = o["rebarId"]?.Value<long?>();
        if (rid == null || rid <= 0)
        {
            // The token is present as a JObject (we're past the null/absent check above): a missing/invalid
            // rebarId is a malformed end, not an omitted one. Fail rather than silently capping a single bar.
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{field} is present but its rebarId is missing or invalid"));
        }
        var rebar = doc.GetElement(ToolHelpers.ToElementId(rid.Value)) as Autodesk.Revit.DB.Structure.Rebar;
        if (rebar == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"{field}.rebarId {rid} is not a Rebar element"));
        var end = RebarToolHelpers.ParseBarEnd(o["end"]);
        // RebarReinforcementData.Create(ElementId rebarId, int iEnd) — two args only.
        return (RebarReinforcementData.Create(ToolHelpers.ToElementId(rid.Value), end), null);
    }
}

/// <summary>Sets whether a coupler is drawn unobscured (solid) in a given view.</summary>
public class SetRebarCouplerVisibilityTool : ICortexTool
{
    public string Name => "set_rebar_coupler_visibility";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set a coupler unobscured (solid) or obscured in a view. Provide couplerId, viewId, unobscured (bool).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var couplerId = input["couplerId"]?.Value<long?>();
        if (couplerId == null || couplerId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "couplerId is required");
        var coupler = doc!.GetElement(ToolHelpers.ToElementId(couplerId.Value)) as RebarCoupler;
        if (coupler == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No RebarCoupler with id {couplerId}");

        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required");
        var view = doc.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No View with id {viewId}");

        if (input["unobscured"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "unobscured (bool) is required");
        var unobscured = input["unobscured"]!.Value<bool>();

        if (!session.RequestConfirmation("set coupler visibility", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Coupler Visibility");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            coupler.SetUnobscuredInView(view, unobscured);
            var isUnobscured = coupler.IsUnobscuredInView(view);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                couplerId = ToolHelpers.GetElementIdValue(coupler),
                viewId = ToolHelpers.GetElementIdValue(view),
                unobscured = isUnobscured
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set coupler visibility: {ex.Message}");
        }
    }
}

/// <summary>
/// Inspects and edits rebar constraints. 'manage_' is a WRITE prefix, but the list/read actions do not
/// open a transaction. Mutating actions (set_preferred/remove_preferred/recompute) confirm + transact.
/// Constraint targets are addressed by handleIndex (never a raw serialized Reference).
/// </summary>
public class ManageRebarConstraintsTool : ICortexTool
{
    public string Name => "manage_rebar_constraints";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Inspect/edit rebar constraints. Provide rebarId and action: list_handles | list_candidates(handleIndex) | set_preferred(handleIndex, candidateIndex) | remove_preferred(handleIndex) | recompute. list_* actions are read-only (no transaction).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        var action = (input["action"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(action))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action is required: list_handles | list_candidates | set_preferred | remove_preferred | recompute");

        RebarConstraintsManager mgr;
        try
        {
            mgr = rebar!.GetRebarConstraintsManager();
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Could not obtain constraints manager: {ex.Message}");
        }
        if (mgr == null || !mgr.HasValidRebar())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "This rebar has no editable constraints manager (constrained placement may be disabled).",
                suggestion: "Constraints are available on shape-driven bars hosted in concrete; enable rebar constrained placement first.");

        var handles = mgr.GetAllHandles();
        var canBeEdited = rebar.ConstraintsCanBeEdited();

        switch (action)
        {
            case "list_handles":
                return CortexResult<object>.Ok(new
                {
                    rebarId = ToolHelpers.GetElementIdValue(rebar),
                    constraintsCanBeEdited = canBeEdited,
                    handleCount = handles.Count,
                    handles = handles.Select((h, i) => DescribeHandle(h, i, mgr)).ToList()
                });

            case "list_candidates":
            {
                var (handle, hidx, herr) = ResolveHandle(handles, input["handleIndex"]?.Value<int?>());
                if (herr != null) return herr;
                var candidates = RebarConstraintCompat.GetCandidates(mgr, handle!);
                return CortexResult<object>.Ok(new
                {
                    rebarId = ToolHelpers.GetElementIdValue(rebar),
                    handleIndex = hidx,
                    handle = DescribeHandle(handle!, hidx, mgr),
                    candidateCount = candidates.Count,
                    candidates = candidates.Select((c, i) => DescribeConstraint(c, i)).ToList()
                });
            }

            case "set_preferred":
            {
                if (!canBeEdited)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Constraints on this rebar cannot be edited.");
                var (handle, hidx, herr) = ResolveHandle(handles, input["handleIndex"]?.Value<int?>());
                if (herr != null) return herr;
                var candidates = RebarConstraintCompat.GetCandidates(mgr, handle!);
                var cidx = input["candidateIndex"]?.Value<int?>() ?? -1;
                if (cidx < 0 || cidx >= candidates.Count)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"candidateIndex must be between 0 and {candidates.Count - 1} (use action=list_candidates first)");

                if (!session.RequestConfirmation("set preferred rebar constraint", 1))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using (var tx = new Transaction(doc, "RevitCortex: Set Preferred Rebar Constraint"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    try
                    {
                        // Bridged across targets: SetPreferredConstraint(c) on R25+, per-handle setter on R23/R24.
                        RebarConstraintCompat.SetPreferred(mgr, handle!, candidates[cidx]);
                        if (tx.Commit() != TransactionStatus.Committed)
                            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                                suggestion: "Fix the reported model errors and retry.");
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set preferred constraint: {ex.Message}");
                    }
                }
                return CortexResult<object>.Ok(new
                {
                    message = $"Set preferred constraint {cidx} on handle {hidx}",
                    rebarId = ToolHelpers.GetElementIdValue(rebar),
                    handleIndex = hidx,
                    candidateIndex = cidx
                });
            }

            case "remove_preferred":
            {
                if (!canBeEdited)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Constraints on this rebar cannot be edited.");
                var (handle, hidx, herr) = ResolveHandle(handles, input["handleIndex"]?.Value<int?>());
                if (herr != null) return herr;

                if (!session.RequestConfirmation("remove preferred rebar constraint", 1))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using (var tx = new Transaction(doc, "RevitCortex: Remove Preferred Rebar Constraint"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    try
                    {
                        mgr.RemovePreferredConstraintFromHandle(handle!);
                        if (tx.Commit() != TransactionStatus.Committed)
                            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                                suggestion: "Fix the reported model errors and retry.");
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove preferred constraint: {ex.Message}");
                    }
                }
                return CortexResult<object>.Ok(new
                {
                    message = $"Removed preferred constraint from handle {hidx}",
                    rebarId = ToolHelpers.GetElementIdValue(rebar),
                    handleIndex = hidx
                });
            }

            case "recompute":
            {
                // "Recompute" re-applies preferred constraints to surfaces. Only available on 2025+; on
                // older targets surface the limitation rather than silently no-op.
#if REVIT2025_OR_GREATER
                if (!canBeEdited)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Constraints on this rebar cannot be edited.");
                if (!session.RequestConfirmation("recompute rebar constraints", handles.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
                using (var tx = new Transaction(doc, "RevitCortex: Recompute Rebar Constraints"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    try
                    {
                        mgr.SetPreferredConstraintsToSurfaceForHandles(handles);
                        if (tx.Commit() != TransactionStatus.Committed)
                            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                                suggestion: "Fix the reported model errors and retry.");
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to recompute constraints: {ex.Message}");
                    }
                }
                return CortexResult<object>.Ok(new
                {
                    message = $"Recomputed (set-to-surface) preferred constraints for {handles.Count} handle(s)",
                    rebarId = ToolHelpers.GetElementIdValue(rebar),
                    handleCount = handles.Count
                });
#else
                return RebarToolHelpers.MinVersionError("Recomputing rebar constraints to surface", 2025);
#endif
            }

            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action '{action}'. Valid: list_handles, list_candidates, set_preferred, remove_preferred, recompute");
        }
    }

    private static (RebarConstrainedHandle? handle, int index, CortexResult<object>? error) ResolveHandle(
        IList<RebarConstrainedHandle> handles, int? handleIndex)
    {
        var idx = handleIndex ?? -1;
        if (idx < 0 || idx >= handles.Count)
            return (null, idx, CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"handleIndex must be between 0 and {handles.Count - 1} (use action=list_handles first)"));
        return (handles[idx], idx, null);
    }

    private static JObject DescribeHandle(RebarConstrainedHandle h, int index, RebarConstraintsManager mgr)
    {
        var dto = new JObject { ["handleIndex"] = index };
        try { dto["handleType"] = h.GetHandleType().ToString(); } catch { }
        try { dto["handleName"] = h.GetHandleName(); } catch { }
        try { dto["isEdgeHandle"] = h.IsEdgeHandle(); } catch { }
        try { dto["isCustomHandle"] = h.IsCustomHandle(); } catch { }
        try { if (h.IsEdgeHandle()) dto["edgeNumber"] = h.GetEdgeNumber(); } catch { }
        try
        {
            var pref = mgr.GetPreferredConstraintOnHandle(h);
            dto["hasPreferredConstraint"] = pref != null;
            if (pref != null) dto["preferredConstraintType"] = pref.GetConstraintType().ToString();
        }
        catch { }
        return dto;
    }

    private static JObject DescribeConstraint(RebarConstraint c, int index)
    {
        var dto = new JObject { ["candidateIndex"] = index };
        try { dto["constraintType"] = c.GetConstraintType().ToString(); } catch { }
        try { dto["numberOfTargets"] = c.NumberOfTargets; } catch { }
        try { dto["isToCover"] = c.IsToCover(); } catch { }
        try { dto["isToHostFaceOrCover"] = c.IsToHostFaceOrCover(); } catch { }
        try { dto["isToOtherRebar"] = c.IsToOtherRebar(); } catch { }
        // Describe the first target element id when available (a stable, serializable descriptor —
        // never the raw Reference object).
        try
        {
            var target = c.GetTargetElement();
            if (target != null) dto["targetElementId"] = ToolHelpers.GetElementIdValue(target);
        }
        catch { }
        return dto;
    }
}

/// <summary>
/// Propagation of rebar to similar hosts. The Revit API exposes NO rebar-propagation method on any
/// supported version (ReinforcementUtils is empty; the only Propagate* methods belong to DatumPlane).
/// This tool therefore returns a structured "unsupported" failure rather than faking a result.
/// </summary>
public class PropagateRebarTool : ICortexTool
{
    public string Name => "propagate_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Propagate rebar to similar hosts. NOTE: the Revit API exposes no rebar-propagation method on any supported version; this tool returns a structured 'unsupported' result. Use copy/paste + Revit's interactive rebar propagation UI instead.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        // Validate the rebar id so the caller still gets a precise diagnostic, then report the API gap.
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        _ = rebar;
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "Rebar propagation is not exposed by the Revit API on any supported version (2023-2027). " +
            "ReinforcementUtils has no propagation method and there is no Rebar.Propagate* member.",
            suggestion: "Use Revit's interactive 'Propagate' command in the rebar UI, or copy the rebar and re-host it onto similar elements.");
    }
}

/// <summary>
/// Unifies two compatible standalone bars into a single rebar via RebarSpliceUtils.UnifyRebarsIntoOne
/// (the only unify entry point in the API). Revit 2025+ only.
/// </summary>
public class UnifyRebarsTool : ICortexTool
{
    public string Name => "unify_rebars";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Unify compatible standalone bars into one (Revit 2025+). Provide rebarIds[] (>=2); they are unified pairwise into a single rebar. Returns the resulting rebar id. Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2025_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        if (!(input["rebarIds"] is JArray arr) || arr.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "rebarIds[] with at least 2 ids is required");

        var ids = new List<long>();
        foreach (var t in arr)
        {
            var v = t.Value<long?>();
            if (v == null || v <= 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Invalid rebar id '{t}' in rebarIds[]");
            var rb = doc!.GetElement(ToolHelpers.ToElementId(v.Value)) as Autodesk.Revit.DB.Structure.Rebar;
            if (rb == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No Rebar element with id {v}");
            ids.Add(v.Value);
        }

        if (!session.RequestConfirmation("unify rebars", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Unify Rebars");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            // UnifyRebarsIntoOne unifies a pair; fold the list left-to-right into the running result.
            var resultId = ToolHelpers.ToElementId(ids[0]);
            for (int i = 1; i < ids.Count; i++)
            {
                resultId = RebarSpliceUtils.UnifyRebarsIntoOne(doc!, resultId, ToolHelpers.ToElementId(ids[i]));
                if (resultId == null || resultId == ElementId.InvalidElementId)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Unify failed at rebar {ids[i]} (the bars may not be collinear/compatible).",
                        suggestion: "Unify only works on collinear, same-type bars that meet end to end.");
                }
            }
            var finalId = ToolHelpers.GetElementIdValue(resultId);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = $"Unified {ids.Count} bars into rebar {finalId}",
                inputRebarIds = ids,
                unifiedRebarId = finalId
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to unify rebars: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Unifying rebars", 2025);
#endif
    }
}

/// <summary>
/// Copies rebar tag/dimension annotations from one view to another by recreating MultiReferenceAnnotations
/// over the rebars present in the source view. Best-effort: per-element failures are surfaced in warnings[].
/// </summary>
public class TransferRebarAnnotationsTool : ICortexTool
{
    public string Name => "transfer_rebar_annotations";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Transfer rebar annotations between views by recreating MultiReferenceAnnotations over the rebars visible in the source view. Provide sourceViewId, targetViewId. Per-element issues are reported in warnings[].";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var srcId = input["sourceViewId"]?.Value<long?>();
        var tgtId = input["targetViewId"]?.Value<long?>();
        if (srcId == null || srcId <= 0) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sourceViewId is required");
        if (tgtId == null || tgtId <= 0) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "targetViewId is required");
        var srcView = doc!.GetElement(ToolHelpers.ToElementId(srcId.Value)) as View;
        var tgtView = doc.GetElement(ToolHelpers.ToElementId(tgtId.Value)) as View;
        if (srcView == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No View with id {srcId}");
        if (tgtView == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No View with id {tgtId}");

        var warnings = new List<string>();

        // Collect rebars visible in the source view.
        var rebars = new FilteredElementCollector(doc, srcView.Id)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar))
            .Cast<Autodesk.Revit.DB.Structure.Rebar>()
            .ToList();
        if (rebars.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"No rebar found in source view {srcId}; nothing to transfer.",
                suggestion: "Pick a source view that actually shows the bars you want annotated.");

        if (!session.RequestConfirmation("transfer rebar annotations", rebars.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Transfer Rebar Annotations");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            // A default MRA type works for rebar tag/dimension grouping across all versions.
            MultiReferenceAnnotationType mraType;
            try
            {
                mraType = MultiReferenceAnnotationType.CreateDefault(doc);
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Could not create a default MultiReferenceAnnotationType: {ex.Message}");
            }

            var created = new List<long>();
            foreach (var rebar in rebars)
            {
                try
                {
                    var options = new MultiReferenceAnnotationOptions(mraType);
                    options.SetElementsToDimension(new List<ElementId> { rebar.Id });
                    if (!MultiReferenceAnnotation.AreElementsValidForMultiReferenceAnnotation(doc, options))
                    {
                        warnings.Add($"Rebar {ToolHelpers.GetElementIdValue(rebar)} is not valid for an annotation in the target view; skipped.");
                        continue;
                    }
                    var mra = MultiReferenceAnnotation.Create(doc, tgtView.Id, options);
                    if (mra != null) created.Add(ToolHelpers.GetElementIdValue(mra));
                    else warnings.Add($"Annotation creation returned null for rebar {ToolHelpers.GetElementIdValue(rebar)}; skipped.");
                }
                catch (Exception exItem)
                {
                    warnings.Add($"Rebar {ToolHelpers.GetElementIdValue(rebar)}: {exItem.Message}");
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = $"Transferred annotations for {created.Count} of {rebars.Count} rebar(s) from view {srcId} to {tgtId}",
                sourceViewId = ToolHelpers.GetElementIdValue(srcView),
                targetViewId = ToolHelpers.GetElementIdValue(tgtView),
                rebarConsidered = rebars.Count,
                annotationsCreated = created.Count,
                createdAnnotationIds = created,
                warnings
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to transfer rebar annotations: {ex.Message}");
        }
    }
}

/// <summary>Reads coupler data: mark, quantity, and the linked reinforcement descriptors (read-only).</summary>
public class GetRebarCouplerDataTool : ICortexTool
{
    public string Name => "get_rebar_coupler_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a rebar coupler: couplerMark, quantity, type id/name, and each linked reinforcement descriptor {rebarId, end}. Provide couplerId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var couplerId = input["couplerId"]?.Value<long?>();
        if (couplerId == null || couplerId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "couplerId is required");
        var coupler = doc!.GetElement(ToolHelpers.ToElementId(couplerId.Value)) as RebarCoupler;
        if (coupler == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No RebarCoupler with id {couplerId}");

        try
        {
            var linked = new List<JObject>();
            foreach (var rd in coupler.GetCoupledReinforcementData())
            {
                // ReinforcementData is opaque; the concrete coupler data is RebarReinforcementData.
                if (rd is RebarReinforcementData rrd)
                {
                    linked.Add(new JObject
                    {
                        ["rebarId"] = ToolHelpers.GetElementIdValue(rrd.RebarId),
                        ["end"] = rrd.End
                    });
                }
            }

            var typeId = coupler.GetTypeId();
            var typeName = (doc.GetElement(typeId) as ElementType)?.Name;

            return CortexResult<object>.Ok(new
            {
                couplerId = ToolHelpers.GetElementIdValue(coupler),
                couplerMark = coupler.CouplerMark,
                quantity = coupler.GetCouplerQuantity(),
                linksTwoBars = coupler.CouplerLinkTwoBars(),
                typeId = ToolHelpers.GetElementIdValue(typeId),
                typeName,
                linkedReinforcement = linked
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read coupler: {ex.Message}");
        }
    }
}

/// <summary>Lists constraint candidates for a given rebar handle (read-only).</summary>
public class GetRebarConstraintCandidatesTool : ICortexTool
{
    public string Name => "get_rebar_constraint_candidates";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List the constraint candidates for one rebar handle. Provide rebarId and handleIndex (from manage_rebar_constraints action=list_handles). Read-only.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        RebarConstraintsManager mgr;
        try { mgr = rebar!.GetRebarConstraintsManager(); }
        catch (Exception ex) { return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Could not obtain constraints manager: {ex.Message}"); }
        if (mgr == null || !mgr.HasValidRebar())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "This rebar has no editable constraints manager.");

        var handles = mgr.GetAllHandles();
        var idx = input["handleIndex"]?.Value<int?>() ?? -1;
        if (idx < 0 || idx >= handles.Count)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"handleIndex must be between 0 and {handles.Count - 1} (use manage_rebar_constraints action=list_handles first)");

        try
        {
            var handle = handles[idx];
            // Bridged candidate query (1-arg overload on R23/R24/R25, (handle, ElementId) on R26+).
            var candidates = RebarConstraintCompat.GetCandidates(mgr, handle);
            var list = new List<JObject>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var dto = new JObject { ["candidateIndex"] = i };
                try { dto["constraintType"] = c.GetConstraintType().ToString(); } catch { }
                try { dto["numberOfTargets"] = c.NumberOfTargets; } catch { }
                try { dto["isToOtherRebar"] = c.IsToOtherRebar(); } catch { }
                try { dto["isToHostFaceOrCover"] = c.IsToHostFaceOrCover(); } catch { }
                try
                {
                    var target = c.GetTargetElement();
                    if (target != null) dto["targetElementId"] = ToolHelpers.GetElementIdValue(target);
                }
                catch { }
                list.Add(dto);
            }

            var handleDto = new JObject { ["handleIndex"] = idx };
            try { handleDto["handleType"] = handle.GetHandleType().ToString(); } catch { }
            try { handleDto["handleName"] = handle.GetHandleName(); } catch { }

            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                handle = handleDto,
                candidateCount = list.Count,
                candidates = list
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read constraint candidates: {ex.Message}");
        }
    }
}

// =====================================================================================================
// Splice tools — Revit 2025+ only (RebarSpliceUtils & Rebar splice instance methods are absent on R23/R24).
// =====================================================================================================

/// <summary>Splices a rebar by rules at a chosen position (Revit 2025+).</summary>
public class SpliceRebarTool : ICortexTool
{
    public string Name => "splice_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Splice a rebar by rules at a position (Revit 2025+). Provide rebarId, optional spliceTypeId, position (End1|Middle|End2). Returns the resulting rebar ids. Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2025_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        // Resolve a splice type. The proper API is RebarSpliceTypeUtils (NOT a FamilySymbol);
        // prefer an explicit id, then any existing splice type, else create a default one.
        ElementId spliceTypeId;
        var explicitId = input["spliceTypeId"]?.Value<long?>();
        if (explicitId.HasValue && explicitId > 0)
        {
            spliceTypeId = ToolHelpers.ToElementId(explicitId.Value);
        }
        else
        {
            var existing = RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc!);
            spliceTypeId = existing != null && existing.Count > 0 ? existing[0] : ElementId.InvalidElementId;
        }

        var positionStr = (input["position"]?.Value<string>() ?? "Middle").Trim();
        var position = RebarToolHelpers.ParseEnum<RebarSplicePosition>(positionStr, "position", out var perr);
        if (perr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, perr);

        if (!session.RequestConfirmation("splice rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Splice Rebar");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            // RebarSpliceOptions requires a valid splice type; create a default if the doc had none.
            if (spliceTypeId == ElementId.InvalidElementId)
            {
                var created = RebarSpliceTypeUtils.CreateRebarSpliceType(doc!, "RevitCortex Lap Splice");
                spliceTypeId = created?.Id ?? ElementId.InvalidElementId;
            }
            if (spliceTypeId == ElementId.InvalidElementId)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar splice type could be resolved or created.");
            }

            var options = new RebarSpliceOptions(doc!, spliceTypeId, position);
            var rules = RebarSpliceRules.Create(doc!);

            // Compute splice geometries by rules, then splice. This avoids requiring the caller to supply
            // a manual cut line/normal (the alternative SpliceRebar(line, ...) overloads).
            var byRules = RebarSpliceUtils.GetSpliceGeometries(doc!, rebar!.Id, options, rules);
            if (byRules == null || byRules.Error != RebarSpliceByRulesError.Success)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Could not compute splice geometry by rules (error: {byRules?.Error.ToString() ?? "null result"}).",
                    suggestion: "The bar may be shorter than the rule's maximum length, or the position is invalid for this bar.");
            }
            var geometries = byRules.GetSpliceGeometries();
            if (geometries == null || geometries.Count == 0)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Splice-by-rules produced no geometry (bar may not need splicing).");
            }

            var resultIds = RebarSpliceUtils.SpliceRebar(doc!, rebar.Id, options, geometries);
            var ids = (resultIds ?? new List<ElementId>()).Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = $"Spliced rebar into {ids.Count} segment(s) at position {position}",
                sourceRebarId = ToolHelpers.GetElementIdValue(rebar),
                position = position.ToString(),
                spliceTypeId = ToolHelpers.GetElementIdValue(spliceTypeId),
                resultRebarIds = ids
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to splice rebar: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

/// <summary>Removes the splice at a bar end (Revit 2025+).</summary>
public class RemoveRebarSpliceTool : ICortexTool
{
    public string Name => "remove_rebar_splice";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Remove a rebar splice at a bar end (Revit 2025+). Provide rebarId and optional barEnd (0 or 1; default 0). Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2025_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var barEnd = RebarToolHelpers.ParseBarEnd(input["barEnd"]);

        if (!session.RequestConfirmation("remove rebar splice", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Remove Rebar Splice");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            rebar!.RemoveSplice(barEnd);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(new
            {
                message = $"Removed splice at end {barEnd} of rebar {ToolHelpers.GetElementIdValue(rebar)}",
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                barEnd
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove splice: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

/// <summary>Reads splice data at each bar end: lap length, stagger, position, connected bar (Revit 2025+, read-only).</summary>
public class GetRebarSpliceDataTool : ICortexTool
{
    public string Name => "get_rebar_splice_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read rebar splice data (Revit 2025+): for each bar end, lap length (mm), stagger (mm), splice position, connected rebar id/end. Provide rebarId. Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2025_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        try
        {
            var ends = new List<JObject>();
            for (int end = 0; end <= 1; end++)
            {
                var dto = new JObject { ["barEnd"] = end };
                try { dto["lapLengthMm"] = RebarToolHelpers.ToMm(rebar!.GetLapLength(end)); } catch { }
                try { dto["staggerLengthMm"] = RebarToolHelpers.ToMm(rebar!.GetSpliceStaggerLength(end)); } catch { }
                RebarSplice? splice = null;
                try { splice = rebar!.GetRebarSplice(end); } catch { }
                dto["hasSplice"] = splice != null;
                if (splice != null)
                {
                    try { dto["splicePosition"] = splice.SplicePosition.ToString(); } catch { }
                    try { dto["spliceTypeId"] = ToolHelpers.GetElementIdValue(splice.SpliceTypeId); } catch { }
                    try { dto["connectedRebarId"] = ToolHelpers.GetElementIdValue(splice.ConnectedRebarId); } catch { }
                    try { dto["connectedRebarEnd"] = splice.ConnectedRebarEnd; } catch { }
                }
                ends.Add(dto);
            }

            // The full splice chain (set of bars joined by splices), if any.
            var chain = new List<long>();
            try { chain = RebarSpliceUtils.GetSpliceChain(rebar!).Select(i => ToolHelpers.GetElementIdValue(i)).ToList(); } catch { }

            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                ends,
                spliceChainRebarIds = chain
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read splice data: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

/// <summary>
/// Reports candidate splice geometries computed by rules for a rebar (Revit 2025+, read-only). Does NOT
/// modify the model; useful before calling splice_rebar.
/// </summary>
public class GetRebarSpliceCandidatesTool : ICortexTool
{
    public string Name => "get_rebar_splice_candidates";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report candidate splice geometries for a rebar by rules (Revit 2025+, read-only). Provide rebarId, optional spliceTypeId, position (End1|Middle|End2). Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
#if REVIT2025_OR_GREATER
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        ElementId spliceTypeId;
        var explicitId = input["spliceTypeId"]?.Value<long?>();
        if (explicitId.HasValue && explicitId > 0)
            spliceTypeId = ToolHelpers.ToElementId(explicitId.Value);
        else
        {
            var existing = RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc!);
            spliceTypeId = existing != null && existing.Count > 0 ? existing[0] : ElementId.InvalidElementId;
        }
        if (spliceTypeId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No rebar splice type exists in the document to evaluate candidates against.",
                suggestion: "Create a splice type (e.g. via splice_rebar once) or pass spliceTypeId.");

        var positionStr = (input["position"]?.Value<string>() ?? "Middle").Trim();
        var position = RebarToolHelpers.ParseEnum<RebarSplicePosition>(positionStr, "position", out var perr);
        if (perr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, perr);

        try
        {
            var options = new RebarSpliceOptions(doc!, spliceTypeId, position);
            var rules = RebarSpliceRules.Create(doc!);
            var byRules = RebarSpliceUtils.GetSpliceGeometries(doc!, rebar!.Id, options, rules);
            if (byRules == null)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown, "GetSpliceGeometries returned null.");

            var errorCode = byRules.Error.ToString();
            var geos = byRules.Error == RebarSpliceByRulesError.Success ? byRules.GetSpliceGeometries() : new List<RebarSpliceGeometry>();
            var geoDtos = geos.Select(g => new JObject
            {
                ["origin"] = RebarToolHelpers.XyzToDtoMm(g.SpliceOrigin),
                ["normal"] = RebarToolHelpers.XyzToDtoMm(g.SpliceNormal)
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                position = position.ToString(),
                spliceTypeId = ToolHelpers.GetElementIdValue(spliceTypeId),
                rulesError = errorCode,
                candidateCount = geoDtos.Count,
                candidateGeometries = geoDtos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read splice candidates: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

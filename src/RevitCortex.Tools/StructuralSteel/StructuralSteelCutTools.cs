using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

// =====================================================================================
// Module 5 — Solid & instance-void cuts (5 write + 3 read).
//
// API VERIFIED 2026-05-30 (reflection R25). These wrap the GENERIC Revit cut utilities
// SolidSolidCutUtils + InstanceVoidCutUtils, NOT a steel-specific API — every result says so.
// Both util classes (and CutFailureReason) exist on all targets R23–R27, so NO #if is needed.
//
// CRITICAL ARG ORDER (verified, not the plan draft which is reversed):
//   SolidSolidCutUtils.AddCutBetweenSolids(doc, solidToBeCut, cuttingSolid, splitFaces)
//       => (doc, TARGET, CUTTER, splitFaces)  — target is the cuttee.
//   SolidSolidCutUtils.CanElementCutElement(cuttingElement, cutElement, out reason)
//       => (CUTTER, TARGET, out reason).
//   SolidSolidCutUtils.RemoveCutBetweenSolids(doc, first, second)        — order-insensitive pair.
//   SolidSolidCutUtils.SplitFacesOfCuttingSolid(first, second, split)    — order-insensitive pair.
//   InstanceVoidCutUtils.AddInstanceVoidCut(doc, element, cuttingInstance)
//       => (doc, TARGET, VOID)  — target is the cuttee, void is the cutting instance.
//   InstanceVoidCutUtils.CanBeCutWithVoid(element)                       — arg is the CUTTEE.
//   InstanceVoidCutUtils.RemoveInstanceVoidCut(doc, element, cuttingInstance) => (doc, TARGET, VOID).
// =====================================================================================

/// <summary>
/// Adds a solid-solid cut so cutElement cuts targetElement (SolidSolidCutUtils.AddCutBetweenSolids).
/// Generic Revit geometry cut, not steel-specific.
/// </summary>
public class AddSteelSolidCutTool : ICortexTool
{
    public string Name => "add_steel_solid_cut";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add a solid cut so one element cuts another (SolidSolidCutUtils). Provide cutElementId (the cutter) and targetElementId (the element to be cut). Optional splitFaces (default false). Supports dryRun. Note: this is a generic Revit geometry cut, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;
        var splitFaces = input["splitFaces"]?.Value<bool?>() ?? false;

        var cutterId = ToolHelpers.GetElementIdValue(cutter!);
        var targetId = ToolHelpers.GetElementIdValue(target!);

        // Eligibility BEFORE tx: (cutter, target). Surface the CutFailureReason when ineligible.
        // `default(CutFailureReason)` avoids hard-coding a member name (enum members vary across targets).
        bool canCut;
        CutFailureReason reason = default;
        try { canCut = SolidSolidCutUtils.CanElementCutElement(cutter!, target!, out reason); }
        catch { canCut = false; }
        if (!canCut)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {cutterId} cannot cut {targetId}: {reason}",
                suggestion: "Use check_steel_cut_eligibility to test a pair first.");

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                eligible = true,
                cutElementId = cutterId,
                targetElementId = targetId
            });

        if (!session.RequestConfirmation("add solid cut", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Add Solid Cut");
        tx.Start();
        try
        {
            // ARG ORDER: (doc, solidToBeCut=target, cuttingSolid=cutter, splitFaces).
            SolidSolidCutUtils.AddCutBetweenSolids(doc!, target!, cutter!, splitFaces);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Solid cut added (generic Revit geometry cut, not steel-specific)",
                cutElementId = cutterId,
                targetElementId = targetId,
                splitFaces
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to add solid cut: {ex.Message}");
        }
    }
}

/// <summary>
/// Reports whether one element may cut another via a solid cut and/or an instance void cut, without mutating.
/// </summary>
public class CheckSteelCutEligibilityTool : ICortexTool
{
    public string Name => "check_steel_cut_eligibility";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Check whether one element can cut another via a solid cut and/or an instance void cut, without mutating. Provide cutElementId (the cutter) and targetElementId (the element to be cut). Returns solidCutEligible (+ solidCutFailureReason when false) and instanceVoidCutEligible.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;

        bool solidCutEligible = false;
        string? solidCutFailureReason = null;
        try
        {
            // (cutter, target, out reason): reason is meaningful only when the call returns false.
            solidCutEligible = SolidSolidCutUtils.CanElementCutElement(cutter!, target!, out var reason);
            if (!solidCutEligible) solidCutFailureReason = reason.ToString();
        }
        catch (Exception ex) { solidCutFailureReason = ex.Message; }

        bool instanceVoidCutEligible = false;
        try { instanceVoidCutEligible = InstanceVoidCutUtils.CanBeCutWithVoid(target!); }
        catch { instanceVoidCutEligible = false; }

        return CortexResult<object>.Ok(new
        {
            cutElementId = ToolHelpers.GetElementIdValue(cutter!),
            targetElementId = ToolHelpers.GetElementIdValue(target!),
            solidCutEligible,
            solidCutFailureReason,
            instanceVoidCutEligible
        });
    }
}

/// <summary>
/// Removes a solid-solid cut between two elements (SolidSolidCutUtils.RemoveCutBetweenSolids).
/// </summary>
public class RemoveSteelSolidCutTool : ICortexTool
{
    public string Name => "remove_steel_solid_cut";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Remove a solid cut between two elements (SolidSolidCutUtils). Provide cutElementId and targetElementId. Generic Revit geometry op, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;

        var cutterId = ToolHelpers.GetElementIdValue(cutter!);
        var targetId = ToolHelpers.GetElementIdValue(target!);

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                wouldRemoveSolidCut = new { cutElementId = cutterId, targetElementId = targetId }
            });

        if (!session.RequestConfirmation("remove solid cut", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Remove Solid Cut");
        tx.Start();
        try
        {
            SolidSolidCutUtils.RemoveCutBetweenSolids(doc!, cutter!, target!);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Solid cut removed (generic Revit geometry cut, not steel-specific)",
                cutElementId = cutterId,
                targetElementId = targetId
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove solid cut: {ex.Message}");
        }
    }
}

/// <summary>
/// Toggles whether the cutting solid's faces are split at an existing solid-solid cut
/// (SolidSolidCutUtils.SplitFacesOfCuttingSolid).
/// </summary>
public class SetSteelSolidCutFaceSplittingTool : ICortexTool
{
    public string Name => "set_steel_solid_cut_face_splitting";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set whether the cutting solid's faces are split at an existing solid cut (SolidSolidCutUtils.SplitFacesOfCuttingSolid). Provide cutElementId, targetElementId and split (bool, required). Generic Revit geometry op, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;

        var split = input["split"]?.Value<bool?>();
        if (split == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "'split' (bool) is required");

        var cutterId = ToolHelpers.GetElementIdValue(cutter!);
        var targetId = ToolHelpers.GetElementIdValue(target!);

        if (!session.RequestConfirmation("set solid cut face splitting", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Solid Cut Face Splitting");
        tx.Start();
        try
        {
            SolidSolidCutUtils.SplitFacesOfCuttingSolid(cutter!, target!, split.Value);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Solid cut face-splitting updated (generic Revit geometry cut, not steel-specific)",
                cutElementId = cutterId,
                targetElementId = targetId,
                split = split.Value
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set solid cut face splitting: {ex.Message}");
        }
    }
}

/// <summary>
/// Adds an instance void cut so a cutting void instance cuts targetElement
/// (InstanceVoidCutUtils.AddInstanceVoidCut). Generic Revit geometry op, not steel-specific.
/// </summary>
public class AddSteelInstanceVoidCutTool : ICortexTool
{
    public string Name => "add_steel_instance_void_cut";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add an instance void cut so a void family instance cuts another element (InstanceVoidCutUtils). Provide voidInstanceId (the cutting void instance) and targetElementId (the element to be cut). Supports dryRun. Note: this is a generic Revit geometry cut, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (voidInstance, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["voidInstanceId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;

        var voidId = ToolHelpers.GetElementIdValue(voidInstance!);
        var targetId = ToolHelpers.GetElementIdValue(target!);

        // Eligibility BEFORE tx: CanBeCutWithVoid takes the CUTTEE (target).
        bool canCut;
        try { canCut = InstanceVoidCutUtils.CanBeCutWithVoid(target!); }
        catch { canCut = false; }
        if (!canCut)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {targetId} cannot be cut with a void instance",
                suggestion: "Use check_steel_cut_eligibility to test a pair first.");

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                eligible = true,
                voidInstanceId = voidId,
                targetElementId = targetId
            });

        if (!session.RequestConfirmation("add instance void cut", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Add Instance Void Cut");
        tx.Start();
        try
        {
            // ARG ORDER: (doc, element=target, cuttingInstance=void).
            InstanceVoidCutUtils.AddInstanceVoidCut(doc!, target!, voidInstance!);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Instance void cut added (generic Revit geometry cut, not steel-specific)",
                voidInstanceId = voidId,
                targetElementId = targetId
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to add instance void cut: {ex.Message}");
        }
    }
}

/// <summary>
/// Removes an instance void cut between a cutting void instance and a target element
/// (InstanceVoidCutUtils.RemoveInstanceVoidCut).
/// </summary>
public class RemoveSteelInstanceVoidCutTool : ICortexTool
{
    public string Name => "remove_steel_instance_void_cut";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Remove an instance void cut between a void family instance and another element (InstanceVoidCutUtils). Provide voidInstanceId and targetElementId. Generic Revit geometry op, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (voidInstance, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["voidInstanceId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;

        var voidId = ToolHelpers.GetElementIdValue(voidInstance!);
        var targetId = ToolHelpers.GetElementIdValue(target!);

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                wouldRemoveInstanceVoidCut = new { voidInstanceId = voidId, targetElementId = targetId }
            });

        if (!session.RequestConfirmation("remove instance void cut", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Remove Instance Void Cut");
        tx.Start();
        try
        {
            // ARG ORDER: (doc, element=target, cuttingInstance=void).
            InstanceVoidCutUtils.RemoveInstanceVoidCut(doc!, target!, voidInstance!);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Instance void cut removed (generic Revit geometry cut, not steel-specific)",
                voidInstanceId = voidId,
                targetElementId = targetId
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove instance void cut: {ex.Message}");
        }
    }
}

/// <summary>
/// Reads the solid-solid cut relationships of an element: the solids that cut it
/// (GetCuttingSolids) and the solids it cuts (GetSolidsBeingCut).
/// </summary>
public class GetSolidCutRelationshipsTool : ICortexTool
{
    public string Name => "get_solid_cut_relationships";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the solid-solid cut relationships of an element (SolidSolidCutUtils): cuttingSolids (solids that cut this element) and solidsBeingCut (solids this element cuts). Counts are always returned; arrays are returned unless summaryOnly, truncated to maxResults (default 100). Generic Revit geometry, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;

        var maxResults = input["maxResults"]?.Value<int?>() ?? 100;
        if (maxResults < 1) maxResults = 1;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() == true;

        ICollection<ElementId> cuttingSolids;
        ICollection<ElementId> solidsBeingCut;
        try { cuttingSolids = SolidSolidCutUtils.GetCuttingSolids(elem!); }
        catch { cuttingSolids = new List<ElementId>(); }
        try { solidsBeingCut = SolidSolidCutUtils.GetSolidsBeingCut(elem!); }
        catch { solidsBeingCut = new List<ElementId>(); }

        var cuttingCount = cuttingSolids.Count;
        var beingCutCount = solidsBeingCut.Count;

        if (summaryOnly)
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                cuttingSolidsCount = cuttingCount,
                solidsBeingCutCount = beingCutCount,
                note = "Generic Revit solid-solid cut relationships, not steel-specific."
            });

        return CortexResult<object>.Ok(new
        {
            elementId = ToolHelpers.GetElementIdValue(elem!),
            cuttingSolidsCount = cuttingCount,
            solidsBeingCutCount = beingCutCount,
            cuttingSolids = cuttingSolids.Take(maxResults).Select(ToolHelpers.GetElementIdValue).ToList(),
            solidsBeingCut = solidsBeingCut.Take(maxResults).Select(ToolHelpers.GetElementIdValue).ToList(),
            truncated = cuttingCount > maxResults || beingCutCount > maxResults,
            note = "Generic Revit solid-solid cut relationships, not steel-specific."
        });
    }
}

/// <summary>
/// Reads the instance-void cut relationships of an element: the void instances that cut it
/// (GetCuttingVoidInstances) and the elements it (as a void instance) cuts (GetElementsBeingCut).
/// </summary>
public class GetInstanceVoidCutRelationshipsTool : ICortexTool
{
    public string Name => "get_instance_void_cut_relationships";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the instance-void cut relationships of an element (InstanceVoidCutUtils): cuttingVoidInstances (void instances that cut this element) and elementsBeingCut (elements this element cuts, when it is itself a cutting void instance). Counts are always returned; arrays are returned unless summaryOnly, truncated to maxResults (default 100). Generic Revit geometry, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;

        var maxResults = input["maxResults"]?.Value<int?>() ?? 100;
        if (maxResults < 1) maxResults = 1;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() == true;

        ICollection<ElementId> cuttingVoidInstances;
        ICollection<ElementId> elementsBeingCut;
        try { cuttingVoidInstances = InstanceVoidCutUtils.GetCuttingVoidInstances(elem!); }
        catch { cuttingVoidInstances = new List<ElementId>(); }
        try { elementsBeingCut = InstanceVoidCutUtils.GetElementsBeingCut(elem!); }
        catch { elementsBeingCut = new List<ElementId>(); }

        var cuttingCount = cuttingVoidInstances.Count;
        var beingCutCount = elementsBeingCut.Count;

        if (summaryOnly)
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                cuttingVoidInstancesCount = cuttingCount,
                elementsBeingCutCount = beingCutCount,
                note = "Generic Revit instance-void cut relationships, not steel-specific."
            });

        return CortexResult<object>.Ok(new
        {
            elementId = ToolHelpers.GetElementIdValue(elem!),
            cuttingVoidInstancesCount = cuttingCount,
            elementsBeingCutCount = beingCutCount,
            cuttingVoidInstances = cuttingVoidInstances.Take(maxResults).Select(ToolHelpers.GetElementIdValue).ToList(),
            elementsBeingCut = elementsBeingCut.Take(maxResults).Select(ToolHelpers.GetElementIdValue).ToList(),
            truncated = cuttingCount > maxResults || beingCutCount > maxResults,
            note = "Generic Revit instance-void cut relationships, not steel-specific."
        });
    }
}

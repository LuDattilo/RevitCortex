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

namespace RevitCortex.Tools.StructuralSteel;

// =====================================================================================
// Module 2 — Connection creation & input mutation (8 write tools).
//
// API VERIFIED 2026-05-30 (reflection over RevitAPI.dll R25/R26). Only the members below
// exist on Autodesk.Revit.DB.Structure.StructuralConnectionHandler:
//   STATIC  StructuralConnectionHandler CreateGenericConnection(Document, IList<ElementId>)
//   STATIC  StructuralConnectionHandler Create(Document, IList<ElementId> ids, ElementId typeId)
//   STATIC  StructuralConnectionHandler Create(Document, IList<ElementId> ids, String typeName)
//   STATIC  StructuralConnectionHandler Create(Document, IList<ElementId> ids, ElementId typeId,
//                                              IList<ConnectionInputPoint> additionalInputPoints)
//   INSTANCE void AddElementIds(IList<ElementId>); void RemoveElementIds(IList<ElementId>);
//            IList<ElementId> GetConnectedElementIds(); void AddReferences(Document, IList<Reference>);
//            void RemoveReferences(IList<Reference>); IList<ConnectionInputPoint> GetInputPoints();
//            void SetDefaultElementOrder(); bool IsCustom(); bool IsDetailed();
//   PROPS  ElementId ApprovalTypeId {get;set;}; StructuralConnectionCodeCheckingStatus CodeCheckingStatus {get;set;};
//          bool OverrideTypeParams {get;set;}; int SingleElementEndIndex {get;set;}.
//   ENUM   StructuralConnectionCodeCheckingStatus { NotCalculated, OkChecked, CheckingFailed }.
//   StructuralConnectionApprovalType STATIC: Create(Document, String); IsValidApprovalTypeName(Document, String);
//          GetAllStructuralConnectionApprovalTypes(Document, out ICollection<ElementId>).
// There is NO "disconnected" property and NO type-setter on the handler. set_steel_connection_type is
// therefore implemented as change-by-recreation (delete + Create). ConnectionInputPoint has no public
// constructor reachable from JSON, so create_steel_connection / set_steel_connection_type do not wire
// inputPoints (documented per-tool); modify_steel_connection_inputs cannot fabricate References from JSON
// either, so its *_references actions report an explicit unsupported error rather than guessing.
// =====================================================================================

/// <summary>
/// Creates a GENERIC structural connection between &gt;=2 elements (no provider required — the safe baseline).
/// Input: elementIds[] (&gt;=2), optional connectionName, dryRun.
/// </summary>
[ToolSafety(false, false)]
public class CreateGenericSteelConnectionTool : ICortexTool
{
    public string Name => "create_generic_steel_connection";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a generic structural connection between two or more elements (works without an installed connection provider). Provide elementIds (>=2); optional connectionName. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        if (input["elementIds"] is not JArray arr || arr.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (array of >=2 element ids) is required");

        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Fewer than 2 valid elements resolved for the connection",
                context: skipped.Count > 0 ? new Dictionary<string, object> { ["skipped"] = skipped } : null);

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new { dryRun = true, wouldConnect = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(), skipped });

        if (!session.RequestConfirmation("create generic steel connection", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Generic Steel Connection");
        tx.Start();
        try
        {
            var handler = StructuralConnectionHandler.CreateGenericConnection(doc!, ids);
            if (handler == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection handler");
            }
            var name = input["connectionName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                try { handler.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(name); } catch { /* naming is best-effort */ }
            }
            var id = ToolHelpers.GetElementIdValue(handler);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created generic steel connection {id} between {ids.Count} element(s)",
                connectionId = id,
                connectedElementIds = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                skipped
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create generic connection: {ex.Message}");
        }
    }
}

/// <summary>
/// Creates a TYPED structural connection from a connection handler type id/name (provider-gated).
/// inputPoints are accepted but NOT wired (ConnectionInputPoint has no public constructor reachable from JSON).
/// </summary>
[ToolSafety(false, false)]
public class CreateSteelConnectionTool : ICortexTool
{
    public string Name => "create_steel_connection";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a typed structural connection between two or more elements from a connection handler type (connectionHandlerTypeId or connectionHandlerTypeName). Requires an installed connection provider/type. Provide elementIds (>=2). Supports dryRun. inputPoints are not yet wired (Revit exposes no public ConnectionInputPoint constructor).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        if (input["elementIds"] is not JArray arr || arr.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (array of >=2 element ids) is required");

        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Fewer than 2 valid elements resolved for the connection",
                context: skipped.Count > 0 ? new Dictionary<string, object> { ["skipped"] = skipped } : null);

        var (typeId, typeError) = ResolveConnectionTypeId(doc!, input);
        if (typeError != null) return typeError;

        bool inputPointsIgnored = input["inputPoints"] is JArray ipArr && ipArr.Count > 0;

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                wouldConnect = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                connectionHandlerTypeId = ToolHelpers.GetElementIdValue(typeId),
                inputPointsIgnored,
                skipped
            });

        if (!session.RequestConfirmation("create steel connection", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Steel Connection");
        tx.Start();
        try
        {
            // 3-arg overload only: ConnectionInputPoint cannot be constructed from JSON via the public API,
            // so inputPoints are not forwarded. Documented in the response via inputPointsIgnored.
            var handler = StructuralConnectionHandler.Create(doc!, ids, typeId);
            if (handler == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection handler");
            }
            var id = ToolHelpers.GetElementIdValue(handler);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created steel connection {id} (type {ToolHelpers.GetElementIdValue(typeId)}) between {ids.Count} element(s)",
                connectionId = id,
                connectionHandlerTypeId = ToolHelpers.GetElementIdValue(typeId),
                connectedElementIds = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                inputPointsIgnored,
                skipped
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to create typed connection: {ex.Message}",
                suggestion: "Verify the connection handler type is valid for these elements, or use create_generic_steel_connection.");
        }
    }

    // Resolves connectionHandlerTypeId | connectionHandlerTypeName to a verified ElementId of a
    // StructuralConnectionHandlerType. Returns a provider-unavailable error when nothing resolves.
    internal static (ElementId typeId, CortexResult<object>? error) ResolveConnectionTypeId(Document doc, JObject input)
    {
        var idToken = input["connectionHandlerTypeId"]?.Value<long?>();
        if (idToken != null && idToken > 0)
        {
            var eid = ToolHelpers.ToElementId(idToken.Value);
            if (doc.GetElement(eid) is StructuralConnectionHandlerType)
                return (eid, null);
            return (ElementId.InvalidElementId, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No StructuralConnectionHandlerType with id {idToken}",
                suggestion: "Use list_steel_connection_handler_types to find a valid type id."));
        }

        var name = input["connectionHandlerTypeName"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = new FilteredElementCollector(doc).OfClass(typeof(StructuralConnectionHandlerType))
                .Cast<StructuralConnectionHandlerType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return (match.Id, null);
            return (ElementId.InvalidElementId, StructuralSteelToolHelpers.ProviderUnavailableError("Typed steel connections"));
        }

        return (ElementId.InvalidElementId, CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "connectionHandlerTypeId or connectionHandlerTypeName is required"));
    }
}

/// <summary>
/// Mutates the connected-element set of an existing connection handler (add/remove element ids).
/// Reference-based actions are not supported (References cannot be fabricated from JSON).
/// </summary>
[ToolSafety(false, false)]
public class ModifySteelConnectionInputsTool : ICortexTool
{
    public string Name => "modify_steel_connection_inputs";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add or remove connected elements on a structural connection handler. action = add_element_ids | remove_element_ids (provide elementIds[]). add_references / remove_references are not supported via this tool (Revit References cannot be built from JSON ids). Returns accepted/skipped counts.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        var action = StructuralSteelToolHelpers.ParseConnectionInputAction(input["action"]?.Value<string>(), out var actionError);
        if (actionError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, actionError);

        if (action == StructuralSteelToolHelpers.ConnectionInputAction.AddReferences ||
            action == StructuralSteelToolHelpers.ConnectionInputAction.RemoveReferences)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Reference-based input mutation is not supported via this tool: Revit Reference objects cannot be reconstructed from JSON element ids.",
                suggestion: "Use add_element_ids / remove_element_ids with whole-element ids instead.");
        }

        if (input["elementIds"] is not JArray arr || arr.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (non-empty array) is required for element-id actions");

        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No valid elements resolved",
                context: skipped.Count > 0 ? new Dictionary<string, object> { ["skipped"] = skipped } : null);

        bool isAdd = action == StructuralSteelToolHelpers.ConnectionInputAction.AddElementIds;

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                action = isAdd ? "add_element_ids" : "remove_element_ids",
                connectionId = ToolHelpers.GetElementIdValue(handler!),
                wouldModify = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                skipped
            });

        if (!session.RequestConfirmation(isAdd ? "add elements to steel connection" : "remove elements from steel connection", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Modify Steel Connection Inputs");
        tx.Start();
        try
        {
            if (isAdd) handler!.AddElementIds(ids);
            else handler!.RemoveElementIds(ids);
            var connected = handler!.GetConnectedElementIds()?.Select(i => ToolHelpers.GetElementIdValue(i)).ToList() ?? new List<long>();
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"{(isAdd ? "Added" : "Removed")} {ids.Count} element(s) {(isAdd ? "to" : "from")} connection {ToolHelpers.GetElementIdValue(handler)}",
                connectionId = ToolHelpers.GetElementIdValue(handler),
                action = isAdd ? "add_element_ids" : "remove_element_ids",
                acceptedCount = ids.Count,
                skippedCount = skipped.Count,
                skipped,
                connectedElementIds = connected
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to modify connection inputs: {ex.Message}");
        }
    }
}

/// <summary>
/// Changes a connection handler's type by recreation: read connected ids, delete the old handler,
/// and Create() a new one with the new type id (no in-place type setter exists). Provider-gated.
/// </summary>
[ToolSafety(false, true)]
public class SetSteelConnectionTypeTool : ICortexTool
{
    public string Name => "set_steel_connection_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Change a structural connection's type. Revit exposes no in-place type setter, so this recreates the connection: it reads the connected elements, deletes the old handler, and creates a new one with connectionHandlerTypeId|connectionHandlerTypeName. Writable state (approvalTypeId, codeCheckingStatus, overrideTypeParams, singleElementEndIndex) is best-effort restored and reported via restoredFields/lostFields. Requires an installed connection provider/type. Supports dryRun (returns willPreserve/willLose + a stateSnapshot). Existing input points/references are NOT preserved (no public ConnectionInputPoint/Reference constructor).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        IList<ElementId> ids;
        try { ids = handler!.GetConnectedElementIds() ?? new List<ElementId>(); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Could not read connected elements: {ex.Message}");
        }
        if (ids.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Connection has fewer than 2 connected elements; cannot recreate with a new type.");

        var (typeId, typeError) = CreateSteelConnectionTool.ResolveConnectionTypeId(doc!, input);
        if (typeError != null) return typeError;

        var oldId = ToolHelpers.GetElementIdValue(handler);
        var connectedValues = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList();

        // Snapshot every readable handler property BEFORE deletion so we can restore the simple writable
        // ones on the recreated handler. Each read is isolated: a single throwing getter must not abort
        // the whole snapshot (provider-dependent fields can throw on bare/generic handlers).
        ElementId oldApprovalTypeId = ElementId.InvalidElementId;
        try { oldApprovalTypeId = handler!.ApprovalTypeId ?? ElementId.InvalidElementId; } catch { oldApprovalTypeId = ElementId.InvalidElementId; }
        StructuralConnectionCodeCheckingStatus oldStatus = StructuralConnectionCodeCheckingStatus.NotCalculated;
        try { oldStatus = handler!.CodeCheckingStatus; } catch { oldStatus = StructuralConnectionCodeCheckingStatus.NotCalculated; }
        bool oldOverrideTypeParams = false;
        try { oldOverrideTypeParams = handler!.OverrideTypeParams; } catch { oldOverrideTypeParams = false; }
        int oldSingleElementEndIndex = 0;
        try { oldSingleElementEndIndex = handler!.SingleElementEndIndex; } catch { oldSingleElementEndIndex = 0; }
        int inputPointCount = 0;
        try { inputPointCount = handler!.GetInputPoints()?.Count ?? 0; } catch { inputPointCount = 0; }
        int inputReferenceCount = 0;
        try { inputReferenceCount = handler!.GetInputReferences()?.Count ?? 0; } catch { inputReferenceCount = 0; }

        // What the recreation will best-effort restore vs. inherently drop.
        var willPreserve = new List<string> { "connectedElementIds", "codeCheckingStatus", "overrideTypeParams", "singleElementEndIndex" };
        if (oldApprovalTypeId != ElementId.InvalidElementId) willPreserve.Insert(1, "approvalTypeId");
        var willLose = new List<string>();
        if (inputPointCount > 0) willLose.Add("inputPoints");
        if (inputReferenceCount > 0) willLose.Add("inputReferences");

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                connectionId = oldId,
                newConnectionHandlerTypeId = ToolHelpers.GetElementIdValue(typeId),
                connectedElementIds = connectedValues,
                willPreserve,
                willLose,
                stateSnapshot = new
                {
                    approvalTypeId = oldApprovalTypeId != ElementId.InvalidElementId
                        ? (long?)ToolHelpers.GetElementIdValue(oldApprovalTypeId)
                        : null,
                    codeCheckingStatus = oldStatus.ToString(),
                    overrideTypeParams = oldOverrideTypeParams,
                    singleElementEndIndex = oldSingleElementEndIndex,
                    inputPointCount,
                    inputReferenceCount
                },
                note = "Would delete the existing handler and recreate it with the new type, restoring writable state."
            });

        if (!session.RequestConfirmation("change steel connection type", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Change Steel Connection Type");
        tx.Start();
        try
        {
            doc!.Delete(handler!.Id);
            var created = StructuralConnectionHandler.Create(doc!, ids, typeId);
            if (created == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection handler after recreation; rolled back.");
            }

            // Best-effort restore of writable state. Each assignment is isolated so one rejected
            // property does not abort the rest (or the commit).
            var restoredFields = new List<string>();
            var lostFields = new List<string>();
            if (oldApprovalTypeId != ElementId.InvalidElementId)
            {
                try { created.ApprovalTypeId = oldApprovalTypeId; restoredFields.Add("approvalTypeId"); }
                catch { lostFields.Add("approvalTypeId"); }
            }
            try { created.CodeCheckingStatus = oldStatus; restoredFields.Add("codeCheckingStatus"); }
            catch { lostFields.Add("codeCheckingStatus"); }
            try { created.OverrideTypeParams = oldOverrideTypeParams; restoredFields.Add("overrideTypeParams"); }
            catch { lostFields.Add("overrideTypeParams"); }
            try { created.SingleElementEndIndex = oldSingleElementEndIndex; restoredFields.Add("singleElementEndIndex"); }
            catch { lostFields.Add("singleElementEndIndex"); }
            if (inputPointCount > 0) lostFields.Add("inputPoints");
            if (inputReferenceCount > 0) lostFields.Add("inputReferences");

            var newId = ToolHelpers.GetElementIdValue(created);
            tx.Commit();

            var warnings = new List<string>();
            if (inputPointCount > 0 || inputReferenceCount > 0)
                warnings.Add("input points/references are not recreated (Revit exposes no public ConnectionInputPoint/Reference constructor reachable from JSON).");

            return CortexResult<object>.Ok(new
            {
                message = $"Recreated connection {oldId} as {newId} with type {ToolHelpers.GetElementIdValue(typeId)}",
                previousConnectionId = oldId,
                connectionId = newId,
                connectionHandlerTypeId = ToolHelpers.GetElementIdValue(typeId),
                connectedElementIds = connectedValues,
                restoredFields,
                lostFields,
                warnings
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to change connection type: {ex.Message}",
                suggestion: "Verify the new connection handler type is valid for these elements.");
        }
    }
}

/// <summary>Sets a connection handler's approval type (ApprovalTypeId) from an approval type id/name.</summary>
[ToolSafety(false, false)]
public class SetSteelConnectionApprovalTool : ICortexTool
{
    public string Name => "set_steel_connection_approval";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the approval type of a structural connection handler. Provide connectionId and approvalTypeId or approvalTypeName (verified against the document's StructuralConnectionApprovalType definitions).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        var (approvalId, approvalError) = ResolveApprovalTypeId(doc!, input);
        if (approvalError != null) return approvalError;

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                connectionId = ToolHelpers.GetElementIdValue(handler!),
                wouldSetApprovalTypeId = ToolHelpers.GetElementIdValue(approvalId)
            });

        if (!session.RequestConfirmation("set steel connection approval", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Steel Connection Approval");
        tx.Start();
        try
        {
            handler!.ApprovalTypeId = approvalId;
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Set approval type {ToolHelpers.GetElementIdValue(approvalId)} on connection {ToolHelpers.GetElementIdValue(handler)}",
                connectionId = ToolHelpers.GetElementIdValue(handler),
                approvalTypeId = ToolHelpers.GetElementIdValue(approvalId)
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set approval type: {ex.Message}");
        }
    }

    // Resolves approvalTypeId | approvalTypeName. For a name, verifies via IsValidApprovalTypeName then
    // matches Element.Name across GetAllStructuralConnectionApprovalTypes (out ICollection<ElementId>).
    private static (ElementId approvalId, CortexResult<object>? error) ResolveApprovalTypeId(Document doc, JObject input)
    {
        var idToken = input["approvalTypeId"]?.Value<long?>();
        if (idToken != null && idToken > 0)
        {
            var eid = ToolHelpers.ToElementId(idToken.Value);
            if (doc.GetElement(eid) != null) return (eid, null);
            return (ElementId.InvalidElementId, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No approval type element with id {idToken}",
                suggestion: "Use list_steel_approval_types to find a valid approval type id."));
        }

        var name = input["approvalTypeName"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            bool valid;
            try { valid = StructuralConnectionApprovalType.IsValidApprovalTypeName(doc, name); }
            catch { valid = false; }
            if (!valid)
                return (ElementId.InvalidElementId, StructuralSteelToolHelpers.ProviderUnavailableError($"Approval type '{name}'"));

            ICollection<ElementId> approvalIds;
            try { StructuralConnectionApprovalType.GetAllStructuralConnectionApprovalTypes(doc, out approvalIds); }
            catch { approvalIds = new List<ElementId>(); }
            var match = approvalIds?.FirstOrDefault(id =>
                string.Equals(doc.GetElement(id)?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null && match != ElementId.InvalidElementId)
                return (match, null);
            return (ElementId.InvalidElementId, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Approval type name '{name}' is valid but no matching approval type element was found.",
                suggestion: "Create it first (administration tools) or pass approvalTypeId."));
        }

        return (ElementId.InvalidElementId, CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "approvalTypeId or approvalTypeName is required"));
    }
}

/// <summary>Sets a connection handler's code-checking status (NotCalculated | OkChecked | CheckingFailed).</summary>
[ToolSafety(false, false)]
public class SetSteelConnectionStatusTool : ICortexTool
{
    public string Name => "set_steel_connection_status";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the code-checking status of a structural connection handler. status = NotCalculated | OkChecked | CheckingFailed.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        var status = StructuralSteelToolHelpers.ParseEnum<StructuralConnectionCodeCheckingStatus>(
            input["status"]?.Value<string>(), "status", out var statusError);
        if (statusError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, statusError);

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                connectionId = ToolHelpers.GetElementIdValue(handler!),
                wouldSetCodeCheckingStatus = status.ToString()
            });

        if (!session.RequestConfirmation("set steel connection status", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Steel Connection Status");
        tx.Start();
        try
        {
            handler!.CodeCheckingStatus = status;
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Set code-checking status '{status}' on connection {ToolHelpers.GetElementIdValue(handler)}",
                connectionId = ToolHelpers.GetElementIdValue(handler),
                codeCheckingStatus = status.ToString()
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set code-checking status: {ex.Message}");
        }
    }
}

/// <summary>Restores the default element order on a connection handler (SetDefaultElementOrder).</summary>
[ToolSafety(false, false)]
public class SetSteelConnectionDefaultOrderTool : ICortexTool
{
    public string Name => "set_steel_connection_default_order";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reset a structural connection handler to its default element order (SetDefaultElementOrder). Provide connectionId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        if (!session.RequestConfirmation("set steel connection default order", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Steel Connection Default Order");
        tx.Start();
        try
        {
            handler!.SetDefaultElementOrder();
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Reset default element order on connection {ToolHelpers.GetElementIdValue(handler)}",
                connectionId = ToolHelpers.GetElementIdValue(handler)
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set default element order: {ex.Message}");
        }
    }
}

/// <summary>Deletes a structural connection handler (destructive). Supports dryRun.</summary>
[ToolSafety(false, true)]
public class DeleteSteelConnectionTool : ICortexTool
{
    public string Name => "delete_steel_connection";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Delete a structural connection handler by connectionId. Destructive — supports dryRun to preview. The connected elements themselves are not deleted.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        var id = ToolHelpers.GetElementIdValue(handler);

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new { dryRun = true, wouldDelete = id });

        if (!session.RequestConfirmation("delete steel connection", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Delete Steel Connection");
        tx.Start();
        try
        {
            doc!.Delete(handler!.Id);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Deleted steel connection {id}",
                deletedConnectionId = id
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to delete connection: {ex.Message}");
        }
    }
}

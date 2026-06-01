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
// Module 3 — Connection type & approval administration (6 write + 3 read).
//
// API VERIFIED 2026-05-30 (reflection over Nice3point ref RevitAPI.dll R26 AND R27 via
// MetadataLoadContext). Authoritative member surface used below:
//
//   StructuralConnectionType (STATIC unless noted):
//     static StructuralConnectionType Create(Document, StructuralConnectionApplyTo applyTo,
//                                            String name, ElementId familySymbolId)
//     static bool ValidFamilySymbolId(Document, StructuralConnectionApplyTo applyTo, ElementId)
//     static void GetAllStructuralConnectionTypeIds(Document, out ICollection<ElementId>)
//     instance: ElementId GetFamilySymbolId(); void SetFamilySymbolId(ElementId);
//     prop: StructuralConnectionApplyTo ApplyTo {get;}
//   enum StructuralConnectionApplyTo = BeamsAndBraces, ColumnTop, ColumnBase, Connection.
//
//   StructuralConnectionHandlerType (STATIC):
//     static StructuralConnectionHandlerType Create(Document, String name, Guid guid, String familyName)
//       (+ 2 more overloads with categoryId / inputPointsInfo)
//     static ElementId CreateDefaultStructuralConnectionHandlerType(Document)
//     static void UpdateCustomConnectionType(StructuralConnectionHandler, IList<Reference> add, IList<Reference> remove)
//     static void AddElementsToCustomConnection(StructuralConnectionHandler, IList<Reference>)            <-- R23-R26 ONLY (REMOVED in R27)
//     static void RemoveMainSubelementsFromCustomConnection(StructuralConnectionHandler, IList<Subelement>) <-- R23-R26 ONLY (REMOVED in R27)
//
//   StructuralConnectionApprovalType (STATIC):
//     static StructuralConnectionApprovalType Create(Document, String name)
//     static bool IsValidApprovalTypeName(Document, String name)
//     static void GetAllStructuralConnectionApprovalTypes(Document, out ICollection<ElementId>)
//     => NO rename, NO delete exist.
//
//   StructuralConnectionHandler INSTANCE: IList<ConnectionInputPoint> GetInputPoints();
//   ConnectionInputPoint props: Guid Id {get;set;}; XYZ Point {get;set;}.
//   ConnectionValidationInfo: NOT present in Autodesk.Revit.DB.Structure (confirmed absent in R26+R27),
//   and StructuralConnectionHandler exposes NO method that returns one. There is therefore NO public
//   producer of ConnectionValidationInfo for a placed handler -> get_steel_connection_validation reports
//   validationAvailable=false instead of fabricating.
//
//   There is NO public applicability predicate (StructuralConnectionType has no "applies-to-element"
//   query and StructuralConnectionHandler exposes no such test) -> get_steel_connection_applicability
//   reports the type's ApplyTo + family symbol + the supplied elements' categories, clearly labelled.
//
//   Custom-connection mutation needs interactively-picked Reference / Subelement objects that cannot be
//   fabricated from JSON, so manage_custom_steel_connection_type validates inputs and returns an honest
//   Fail for every action; the message documents the R27 removal of the legacy add/remove APIs via #if.
// =====================================================================================

/// <summary>Creates a StructuralConnectionType bound to a family symbol (typed create).</summary>
public class CreateSteelStructuralConnectionTypeTool : ICortexTool
{
    public string Name => "create_steel_structural_connection_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a StructuralConnectionType bound to a family symbol. Provide familySymbolId (a valid connection family symbol); applyTo = BeamsAndBraces | ColumnTop | ColumnBase | Connection (default Connection); optional name. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var symbolId = input["familySymbolId"]?.Value<long?>();
        if (symbolId == null || symbolId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "familySymbolId is required");
        var symId = ToolHelpers.ToElementId(symbolId.Value);
        if (doc!.GetElement(symId) is not FamilySymbol)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No family symbol with id {symbolId}",
                suggestion: "Pass the ElementId of a connection FamilySymbol (use get_available_family_types to find one).");

        var applyTo = StructuralSteelToolHelpers.ParseEnum<StructuralConnectionApplyTo>(
            input["applyTo"]?.Value<string>() ?? nameof(StructuralConnectionApplyTo.Connection), "applyTo", out var applyToError);
        if (applyToError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, applyToError);

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name)) name = "Steel Connection Type";

        bool valid;
        try { valid = StructuralConnectionType.ValidFamilySymbolId(doc, applyTo, symId); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not validate family symbol {symbolId}: {ex.Message}");
        }
        if (!valid)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Family symbol {symbolId} is not valid for a {applyTo} structural connection type",
                suggestion: "Pick a connection family symbol whose category matches the applyTo target.");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, familySymbolId = symbolId, applyTo = applyTo.ToString(), name });

        if (!session.RequestConfirmation("create steel connection type", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Steel Connection Type");
        tx.Start();
        try
        {
            var ct = StructuralConnectionType.Create(doc, applyTo, name, symId);
            if (ct == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection type");
            }
            var id = ToolHelpers.GetElementIdValue(ct);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created structural connection type {id}",
                connectionTypeId = id,
                familySymbolId = symbolId,
                applyTo = applyTo.ToString(),
                name = ct.Name
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create connection type: {ex.Message}");
        }
    }
}

/// <summary>Creates a StructuralConnectionHandlerType (name + guid + family name).</summary>
public class CreateSteelConnectionHandlerTypeTool : ICortexTool
{
    public string Name => "create_steel_connection_handler_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a StructuralConnectionHandlerType. Provide name; optional familyName (default empty); optional guid (a new GUID is generated when omitted). Supports dryRun. Returns the new type id and its connection GUID.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var familyName = input["familyName"]?.Value<string>() ?? string.Empty;

        Guid guid;
        var guidToken = input["guid"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(guidToken))
        {
            guid = StructuralSteelToolHelpers.ParseGuid(guidToken, "guid", out var guidError);
            if (guidError != null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, guidError);
        }
        else
        {
            // A handler type requires a GUID; generate one when the caller does not supply it.
            guid = Guid.NewGuid();
        }

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, name, familyName, guid = guid.ToString() });

        if (!session.RequestConfirmation("create steel connection handler type", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Steel Connection Handler Type");
        tx.Start();
        try
        {
            var handlerType = StructuralConnectionHandlerType.Create(doc!, name, guid, familyName);
            if (handlerType == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection handler type");
            }
            var id = ToolHelpers.GetElementIdValue(handlerType);
            string? connGuid = null;
            try { connGuid = handlerType.ConnectionGuid.ToString(); } catch { }
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created structural connection handler type {id}",
                connectionHandlerTypeId = id,
                name = handlerType.Name,
                familyName,
                guid = guid.ToString(),
                connectionGuid = connGuid
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create connection handler type: {ex.Message}");
        }
    }
}

/// <summary>Creates the document's default StructuralConnectionHandlerType.</summary>
public class CreateDefaultSteelConnectionHandlerTypeTool : ICortexTool
{
    public string Name => "create_default_steel_connection_handler_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create the default StructuralConnectionHandlerType for the document (CreateDefaultStructuralConnectionHandlerType). Returns the new type id. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, note = "Would create the default structural connection handler type." });

        if (!session.RequestConfirmation("create default steel connection handler type", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Default Steel Connection Handler Type");
        tx.Start();
        try
        {
            var typeId = StructuralConnectionHandlerType.CreateDefaultStructuralConnectionHandlerType(doc!);
            if (typeId == null || typeId == ElementId.InvalidElementId)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no default connection handler type id");
            }
            var id = ToolHelpers.GetElementIdValue(typeId);
            string? name = null;
            try { name = doc!.GetElement(typeId)?.Name; } catch { }
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created default structural connection handler type {id}",
                connectionHandlerTypeId = id,
                name
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create default connection handler type: {ex.Message}");
        }
    }
}

/// <summary>Re-binds a StructuralConnectionType to a different family symbol (SetFamilySymbolId).</summary>
public class SetSteelConnectionTypeFamilySymbolTool : ICortexTool
{
    public string Name => "set_steel_connection_type_family_symbol";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Re-bind a StructuralConnectionType to a different family symbol. Provide connectionTypeId and familySymbolId. The new symbol is validated against the type's existing ApplyTo. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var typeIdToken = input["connectionTypeId"]?.Value<long?>();
        if (typeIdToken == null || typeIdToken <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "connectionTypeId is required");
        var ct = doc!.GetElement(ToolHelpers.ToElementId(typeIdToken.Value)) as StructuralConnectionType;
        if (ct == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No StructuralConnectionType with id {typeIdToken}",
                suggestion: "Use list_steel_connection_types to find a valid connection type id.");

        var symbolId = input["familySymbolId"]?.Value<long?>();
        if (symbolId == null || symbolId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "familySymbolId is required");
        var symId = ToolHelpers.ToElementId(symbolId.Value);
        if (doc.GetElement(symId) is not FamilySymbol)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No family symbol with id {symbolId}");

        StructuralConnectionApplyTo applyTo;
        try { applyTo = ct.ApplyTo; }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Could not read the connection type's ApplyTo: {ex.Message}");
        }

        bool valid;
        try { valid = StructuralConnectionType.ValidFamilySymbolId(doc, applyTo, symId); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Could not validate family symbol {symbolId}: {ex.Message}");
        }
        if (!valid)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Family symbol {symbolId} is not valid for this {applyTo} connection type");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, connectionTypeId = typeIdToken, familySymbolId = symbolId, applyTo = applyTo.ToString() });

        if (!session.RequestConfirmation("set steel connection type family symbol", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Steel Connection Type Family Symbol");
        tx.Start();
        try
        {
            ct.SetFamilySymbolId(symId);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Set family symbol {symbolId} on connection type {ToolHelpers.GetElementIdValue(ct)}",
                connectionTypeId = ToolHelpers.GetElementIdValue(ct),
                familySymbolId = symbolId,
                applyTo = applyTo.ToString()
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set family symbol: {ex.Message}");
        }
    }
}

/// <summary>Action-based administration of StructuralConnectionApprovalType (create | list only).</summary>
public class ManageSteelApprovalTypeTool : ICortexTool
{
    public string Name => "manage_steel_approval_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Administer StructuralConnectionApprovalType definitions. action = create (requires name) | list. The Revit API exposes no rename/delete for approval types, so those actions return a structured error.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var action = (input["action"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        switch (action)
        {
            case "list":
                return ListApprovalTypes(doc!);
            case "create":
                return CreateApprovalType(doc!, input, session);
            case "":
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "action is required. Valid: create, list");
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "approval types support only create and list; the Revit API exposes no rename/delete");
        }
    }

    private static CortexResult<object> ListApprovalTypes(Document doc)
    {
        try
        {
            ICollection<ElementId> ids;
            StructuralConnectionApprovalType.GetAllStructuralConnectionApprovalTypes(doc, out ids);
            var allIds = ids != null ? ids.ToList() : new List<ElementId>();
            var items = allIds.Select(id => new JObject
            {
                ["id"] = ToolHelpers.GetElementIdValue(id),
                ["name"] = doc.GetElement(id)?.Name
            }).ToList();
            return CortexResult<object>.Ok(new { count = allIds.Count, approvalTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list approval types: {ex.Message}");
        }
    }

    private static CortexResult<object> CreateApprovalType(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required to create an approval type");

        bool valid;
        try { valid = StructuralConnectionApprovalType.IsValidApprovalTypeName(doc, name); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Could not validate approval type name '{name}': {ex.Message}");
        }
        if (!valid)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Approval type name '{name}' is not valid (it may already exist or contain illegal characters)");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, action = "create", name });

        if (!session.RequestConfirmation("create steel approval type", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Steel Approval Type");
        tx.Start();
        try
        {
            var approval = StructuralConnectionApprovalType.Create(doc, name);
            if (approval == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no approval type");
            }
            var id = ToolHelpers.GetElementIdValue(approval);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created approval type {id}",
                action = "create",
                approvalTypeId = id,
                name = approval.Name
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create approval type: {ex.Message}");
        }
    }
}

/// <summary>
/// Custom-connection mutation entry point. All actions return an honest Fail: Revit's custom-connection
/// mutation requires interactively-picked Reference / Subelement objects that cannot be reconstructed from
/// JSON ids. The message documents that the legacy add/remove APIs were removed in Revit 2027.
/// </summary>
public class ManageCustomSteelConnectionTypeTool : ICortexTool
{
    public string Name => "manage_custom_steel_connection_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Mutate a custom structural connection (handler). action = add_references | remove_references | add_elements | remove_subelements. NOTE: Revit's custom-connection mutation needs interactively-picked References/Subelements that cannot be built from JSON, so this tool validates inputs and returns a structured error rather than guessing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        // Validate the handler exists so the failure is about the unsupported operation, not a bad id.
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        var action = (input["action"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        switch (action)
        {
            case "add_references":
            case "remove_references":
            case "add_elements":
            case "remove_subelements":
                break;
            case "":
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "action is required. Valid: add_references, remove_references, add_elements, remove_subelements");
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Invalid action. Valid: add_references, remove_references, add_elements, remove_subelements");
        }

#if REVIT2027_OR_GREATER
        // In Revit 2027 the legacy AddElementsToCustomConnection / RemoveMainSubelementsFromCustomConnection
        // static mutators were REMOVED; only UpdateCustomConnectionType(handler, addRefs, removeRefs) survives,
        // and it takes Reference lists that cannot be fabricated from JSON element ids.
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "Custom steel connection mutation is not expressible as JSON: Revit 2027 only exposes "
                + "UpdateCustomConnectionType, which requires interactively-picked Reference objects (the "
                + "legacy AddElementsToCustomConnection / RemoveMainSubelementsFromCustomConnection were "
                + "removed in Revit 2027). Edit custom connections in the Revit UI instead.",
            suggestion: "Use the Revit UI to add/remove parts of a custom connection; the public API cannot build the required References/Subelements from JSON ids.");
#else
        // R23-R26 expose AddElementsToCustomConnection(handler, IList<Reference>),
        // RemoveMainSubelementsFromCustomConnection(handler, IList<Subelement>) and
        // UpdateCustomConnectionType(handler, IList<Reference>, IList<Reference>). All three take
        // Reference/Subelement arguments that cannot be reconstructed from JSON, so we still cannot call
        // them with caller-supplied data; we return an honest Fail rather than fabricating arguments.
        // (The references below keep the R26-only API names visible only inside this compiled-but-not-invoked
        //  branch, so they never appear on the R27 code path.)
        const string legacyApis = "AddElementsToCustomConnection / RemoveMainSubelementsFromCustomConnection / UpdateCustomConnectionType";
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "Custom steel connection mutation is not expressible as JSON: Revit's "
                + legacyApis + " all require interactively-picked Reference / Subelement objects, which cannot "
                + "be reconstructed from JSON element ids. Edit custom connections in the Revit UI instead. "
                + "(Note: AddElementsToCustomConnection and RemoveMainSubelementsFromCustomConnection are removed in Revit 2027.)",
            suggestion: "Use the Revit UI to add/remove parts of a custom connection; the public API cannot build the required References/Subelements from JSON ids.");
#endif
    }
}

/// <summary>Reads a connection handler's input points (id GUID + position in mm).</summary>
public class GetSteelConnectionInputPointsTool : ICortexTool
{
    public string Name => "get_steel_connection_input_points";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the input points of a structural connection handler: each point's id (GUID) and position (x,y,z in mm). Provide connectionId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        try
        {
            IList<ConnectionInputPoint> points;
            try { points = handler!.GetInputPoints() ?? new List<ConnectionInputPoint>(); }
            catch (Exception ex)
            {
                return CortexResult<object>.Ok(new
                {
                    connectionId = ToolHelpers.GetElementIdValue(handler!),
                    count = 0,
                    inputPoints = new object[0],
                    note = $"GetInputPoints is unavailable for this handler: {ex.Message}"
                });
            }

            var items = new List<JObject>();
            foreach (var p in points)
            {
                if (p == null) continue;
                string? id = null; JObject? pos = null;
                try { id = p.Id.ToString(); } catch { }
                try { pos = StructuralSteelToolHelpers.XyzToDtoMm(p.Point); } catch { }
                var dto = new JObject { ["id"] = id };
                if (pos != null) { dto["x"] = pos["x"]; dto["y"] = pos["y"]; dto["z"] = pos["z"]; }
                items.Add(dto);
            }

            return CortexResult<object>.Ok(new
            {
                connectionId = ToolHelpers.GetElementIdValue(handler!),
                count = items.Count,
                inputPoints = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read connection input points: {ex.Message}");
        }
    }
}

/// <summary>
/// Reports what is knowable about a connection type's applicability: there is no public applicability
/// predicate, so this returns the type's ApplyTo + family symbol and (optionally) the supplied elements'
/// categories so the caller can judge fit.
/// </summary>
public class GetSteelConnectionApplicabilityTool : ICortexTool
{
    public string Name => "get_steel_connection_applicability";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report a StructuralConnectionType's applicability hints. Revit exposes no public 'does this type apply to these elements' predicate, so this returns the type's ApplyTo + family symbol id and, for any supplied elementIds, their categories — clearly labelled as advisory.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var typeIdToken = input["connectionTypeId"]?.Value<long?>();
        if (typeIdToken == null || typeIdToken <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "connectionTypeId is required");
        var ct = doc!.GetElement(ToolHelpers.ToElementId(typeIdToken.Value)) as StructuralConnectionType;
        if (ct == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No StructuralConnectionType with id {typeIdToken}",
                suggestion: "Use list_steel_connection_types to find a valid connection type id.");

        string? applyTo = null; long familySymbolId = 0;
        try { applyTo = ct.ApplyTo.ToString(); } catch { }
        try { familySymbolId = ToolHelpers.GetElementIdValue(ct.GetFamilySymbolId()); } catch { }

        var elementInfos = new List<JObject>();
        if (input["elementIds"] is JArray arr)
        {
            foreach (var t in arr.Where(x => x.Type == JTokenType.Integer))
            {
                var raw = t.Value<long>();
                var e = doc.GetElement(ToolHelpers.ToElementId(raw));
                elementInfos.Add(new JObject
                {
                    ["elementId"] = raw,
                    ["exists"] = e != null,
                    ["category"] = e?.Category?.Name,
                    ["categoryBuiltIn"] = SafeBuiltInCategory(e)
                });
            }
        }

        return CortexResult<object>.Ok(new
        {
            connectionTypeId = ToolHelpers.GetElementIdValue(ct),
            name = ct.Name,
            applyTo,
            familySymbolId,
            elements = elementInfos,
            note = "No public applicability predicate exists in the Revit API; reporting the type's ApplyTo + family symbol and the supplied elements' categories so the caller can judge fit. ApplyTo values: BeamsAndBraces, ColumnTop, ColumnBase, Connection."
        });
    }

    private static string? SafeBuiltInCategory(Element? e)
    {
        try
        {
            if (e?.Category == null) return null;
#if REVIT2024_OR_GREATER
            return ((BuiltInCategory)e.Category.Id.Value).ToString();
#else
            return ((BuiltInCategory)e.Category.Id.IntegerValue).ToString();
#endif
        }
        catch { return null; }
    }
}

/// <summary>
/// Reports connection validation. No public API produces a ConnectionValidationInfo for a placed handler
/// (the type is absent from Autodesk.Revit.DB.Structure in R26+R27 and the handler exposes no producer),
/// so this returns a documented validationAvailable=false rather than fabricating warnings.
/// </summary>
public class GetSteelConnectionValidationTool : ICortexTool
{
    public string Name => "get_steel_connection_validation";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report validation warnings for a structural connection handler. The Revit API exposes no public producer of ConnectionValidationInfo for a placed handler, so this returns validationAvailable=false with a note. Use the general get_warnings tool for document-level failures.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (handler, handlerError) = StructuralSteelToolHelpers.RequireConnectionHandler(doc!, input["connectionId"]?.Value<long?>());
        if (handlerError != null) return handlerError;

        // ConnectionValidationInfo / ConnectionValidationWarning exist as types but the public SDK exposes
        // no method on StructuralConnectionHandler (or a util) that returns a ConnectionValidationInfo for a
        // placed handler (reflected R26 + R27). We therefore cannot produce validation data without
        // fabricating it. Report the readable code-checking status instead and flag validation unavailable.
        string? codeCheckingStatus = null;
        try { codeCheckingStatus = handler!.CodeCheckingStatus.ToString(); } catch { }

        return CortexResult<object>.Ok(new
        {
            connectionId = ToolHelpers.GetElementIdValue(handler!),
            validationAvailable = false,
            codeCheckingStatus,
            warnings = new object[0],
            note = "No public API produces ConnectionValidationInfo for a placed handler in this Revit version. The handler's CodeCheckingStatus is reported as the only readable validation-related state; use get_warnings for document-level failures."
        });
    }
}

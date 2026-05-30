using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class StructuralSteelTools
{
    [McpServerTool(Name = "get_structural_steel_api_capabilities"), Description("Report which structural steel features the running Revit version supports: SteelElementProperties, structural connections, cut utils, custom-connection mutation API (removed in R27), and whether any structural connection provider is detectable.")]
    public static async Task<string> GetStructuralSteelApiCapabilities(
        RevitConnectionManager revit,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_structural_steel_api_capabilities", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_steel_connection_handlers"), Description("List structural connection handlers in the document: id, type id/name, connected element count, custom/detailed flags. Use maxResults (default 100) and summaryOnly for counts-first browsing.")]
    public static async Task<string> ListSteelConnectionHandlers(
        RevitConnectionManager revit,
        [Description("Maximum handlers to return. Default 100")] int? maxResults = null,
        [Description("Return only the total count, no per-handler array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("list_steel_connection_handlers", p, ct)).ToString();
    }

    [McpServerTool(Name = "list_steel_connection_types"), Description("List StructuralConnectionType definitions in the document: id, name, family symbol id, applyTo. Use maxResults (default 100) and summaryOnly for counts-first browsing.")]
    public static async Task<string> ListSteelConnectionTypes(
        RevitConnectionManager revit,
        [Description("Maximum types to return. Default 100")] int? maxResults = null,
        [Description("Return only the total count, no per-type array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("list_steel_connection_types", p, ct)).ToString();
    }

    [McpServerTool(Name = "list_steel_connection_handler_types"), Description("List StructuralConnectionHandlerType definitions: id, name, connection GUID, generic/custom/detailed flags. Use maxResults (default 100) and summaryOnly for counts-first browsing.")]
    public static async Task<string> ListSteelConnectionHandlerTypes(
        RevitConnectionManager revit,
        [Description("Maximum handler types to return. Default 100")] int? maxResults = null,
        [Description("Return only the total count, no per-type array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("list_steel_connection_handler_types", p, ct)).ToString();
    }

    [McpServerTool(Name = "list_steel_approval_types"), Description("List StructuralConnectionApprovalType definitions: id, name. Use maxResults (default 100) and summaryOnly for counts-first browsing.")]
    public static async Task<string> ListSteelApprovalTypes(
        RevitConnectionManager revit,
        [Description("Maximum approval types to return. Default 100")] int? maxResults = null,
        [Description("Return only the total count, no per-type array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("list_steel_approval_types", p, ct)).ToString();
    }

    [McpServerTool(Name = "list_steel_connection_providers"), Description("List installed structural connection providers. The public Revit API exposes no queryable provider registry; this returns count 0 with an explanatory note.")]
    public static async Task<string> ListSteelConnectionProviders(
        RevitConnectionManager revit,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_steel_connection_providers", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_steel_connection_data"), Description("Read a structural connection handler by id: type id/name, connected element ids, origin, custom/detailed flags, approval type id, code-checking status, override-type-params flag.")]
    public static async Task<string> GetSteelConnectionData(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler")] long connectionId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId };
        return (await revit.ExecuteAsync("get_steel_connection_data", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_connection_type_data"), Description("Read a structural connection type by id. Returns StructuralConnectionType (family symbol id, applyTo) or StructuralConnectionHandlerType (connection GUID, generic/custom/detailed flags) data depending on the element kind.")]
    public static async Task<string> GetSteelConnectionTypeData(
        RevitConnectionManager revit,
        [Description("Element id of the connection type (StructuralConnectionType or StructuralConnectionHandlerType)")] long connectionTypeId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionTypeId"] = connectionTypeId };
        return (await revit.ExecuteAsync("get_steel_connection_type_data", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_connection_settings"), Description("Read the document-wide StructuralConnectionSettings (currently exposes the IncludeWarningControls flag).")]
    public static async Task<string> GetSteelConnectionSettings(
        RevitConnectionManager revit,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_steel_connection_settings", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_steel_element_properties"), Description("Read steel fabrication properties of an element: whether it carries SteelElementProperties and its fabrication unique id (GUID). External-id and material-link enumeration are not exposed by the Revit SDK. Use summaryOnly for flags only.")]
    public static async Task<string> GetSteelElementProperties(
        RevitConnectionManager revit,
        [Description("Revit element id")] long elementId,
        [Description("Return only presence flag without fabrication id detail. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementId"] = elementId };
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("get_steel_element_properties", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_external_id_map"), Description("Report the steel fabrication external-id map for an element. The Revit SDK does not expose per-element external-id enumeration; this returns the fabrication unique id (if any) and count 0 with a note.")]
    public static async Task<string> GetSteelExternalIdMap(
        RevitConnectionManager revit,
        [Description("Revit element id")] long elementId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementId"] = elementId };
        return (await revit.ExecuteAsync("get_steel_external_id_map", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_material_links"), Description("Report steel fabrication material links for an element. The Revit SDK does not expose linked-material enumeration on SteelElementProperties; this returns count 0 with a note.")]
    public static async Task<string> GetSteelMaterialLinks(
        RevitConnectionManager revit,
        [Description("Revit element id")] long elementId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementId"] = elementId };
        return (await revit.ExecuteAsync("get_steel_material_links", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_element_warnings"), Description("Report steel fabrication warnings for an element (or all elements if elementId is omitted). The Revit SDK exposes no steel-specific warning API; this returns count 0 with a note. Use the general get_warnings tool for document-level failures. summaryOnly returns counts only.")]
    public static async Task<string> GetSteelElementWarnings(
        RevitConnectionManager revit,
        [Description("Optional element id to scope the query; omit for a document-wide report")] long? elementId = null,
        [Description("Return only counts, no per-warning array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementId != null) p["elementId"] = elementId;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("get_steel_element_warnings", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_cut_data"), Description("Read cut relationships for an element: solid-solid cuts (cutting solids + solids being cut via SolidSolidCutUtils) and instance-void cuts (cutting void instances + elements being cut via InstanceVoidCutUtils).")]
    public static async Task<string> GetSteelCutData(
        RevitConnectionManager revit,
        [Description("Revit element id")] long elementId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementId"] = elementId };
        return (await revit.ExecuteAsync("get_steel_cut_data", p, ct)).ToString();
    }

    [McpServerTool(Name = "analyze_structural_steel_model"), Description("Document-wide structural steel summary: counts of connection handlers, connection types, connection handler types, approval types, and structural framing/column elements carrying SteelElementProperties. summaryOnly returns counts only; otherwise capped sample arrays via maxResults.")]
    public static async Task<string> AnalyzeStructuralSteelModel(
        RevitConnectionManager revit,
        [Description("Maximum items per sample array. Default 100")] int? maxResults = null,
        [Description("Return only counts, no sample arrays. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("analyze_structural_steel_model", p, ct)).ToString();
    }

    // ===== Module 2 — Connection creation & input mutation (8 write tools) =====

    [McpServerTool(Name = "create_generic_steel_connection"), Description("Create a generic structural connection between two or more elements (works without an installed connection provider — the safe baseline). Provide elementIds (JSON array of >=2 element ids); optional connectionName. Supports dryRun.")]
    public static async Task<string> CreateGenericSteelConnection(
        RevitConnectionManager revit,
        [Description("JSON array of >=2 element ids to connect, e.g. [123,456]")] string elementIds,
        [Description("Optional name applied to the connection (best-effort via the Comments parameter)")] string? connectionName = null,
        [Description("Preview without creating. Default false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = JArray.Parse(elementIds) };
        if (connectionName != null) p["connectionName"] = connectionName;
        if (dryRun != null) p["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("create_generic_steel_connection", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_steel_connection"), Description("Create a typed structural connection between two or more elements from a connection handler type (connectionHandlerTypeId or connectionHandlerTypeName). Requires an installed connection provider/type. Provide elementIds (JSON array of >=2 ids). Supports dryRun. inputPoints are accepted but not yet wired (Revit exposes no public ConnectionInputPoint constructor).")]
    public static async Task<string> CreateSteelConnection(
        RevitConnectionManager revit,
        [Description("JSON array of >=2 element ids to connect, e.g. [123,456]")] string elementIds,
        [Description("Element id of the StructuralConnectionHandlerType to apply")] long? connectionHandlerTypeId = null,
        [Description("Name of the connection handler type to apply (resolved against the document)")] string? connectionHandlerTypeName = null,
        [Description("Optional JSON array of input points [{x,y,z}] in mm. Currently ignored (no public ConnectionInputPoint constructor)")] string? inputPoints = null,
        [Description("Preview without creating. Default false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = JArray.Parse(elementIds) };
        if (connectionHandlerTypeId != null) p["connectionHandlerTypeId"] = connectionHandlerTypeId;
        if (connectionHandlerTypeName != null) p["connectionHandlerTypeName"] = connectionHandlerTypeName;
        if (inputPoints != null) p["inputPoints"] = JArray.Parse(inputPoints);
        if (dryRun != null) p["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("create_steel_connection", p, ct)).ToString();
    }

    [McpServerTool(Name = "modify_steel_connection_inputs"), Description("Add or remove connected elements on a structural connection handler. action = add_element_ids | remove_element_ids (provide elementIds[]). add_references / remove_references are not supported via this tool (Revit References cannot be built from JSON ids). Returns accepted/skipped counts.")]
    public static async Task<string> ModifySteelConnectionInputs(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler")] long connectionId,
        [Description("Action: add_element_ids | remove_element_ids")] string action,
        [Description("JSON array of element ids for the *_element_ids actions, e.g. [123,456]")] string elementIds,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["connectionId"] = connectionId,
            ["action"] = action,
            ["elementIds"] = JArray.Parse(elementIds)
        };
        return (await revit.ExecuteAsync("modify_steel_connection_inputs", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_steel_connection_type"), Description("Change a structural connection's type. Revit exposes no in-place type setter, so this recreates the connection: it reads the connected elements, deletes the old handler, and creates a new one with connectionHandlerTypeId|connectionHandlerTypeName. Requires an installed connection provider/type. Supports dryRun. Existing input points are not preserved.")]
    public static async Task<string> SetSteelConnectionType(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler to retype")] long connectionId,
        [Description("Element id of the new StructuralConnectionHandlerType")] long? connectionHandlerTypeId = null,
        [Description("Name of the new connection handler type (resolved against the document)")] string? connectionHandlerTypeName = null,
        [Description("Preview without recreating. Default false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId };
        if (connectionHandlerTypeId != null) p["connectionHandlerTypeId"] = connectionHandlerTypeId;
        if (connectionHandlerTypeName != null) p["connectionHandlerTypeName"] = connectionHandlerTypeName;
        if (dryRun != null) p["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("set_steel_connection_type", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_steel_connection_approval"), Description("Set the approval type of a structural connection handler. Provide connectionId and approvalTypeId or approvalTypeName (verified against the document's StructuralConnectionApprovalType definitions).")]
    public static async Task<string> SetSteelConnectionApproval(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler")] long connectionId,
        [Description("Element id of the approval type to apply")] long? approvalTypeId = null,
        [Description("Name of the approval type to apply (validated then matched by name)")] string? approvalTypeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId };
        if (approvalTypeId != null) p["approvalTypeId"] = approvalTypeId;
        if (approvalTypeName != null) p["approvalTypeName"] = approvalTypeName;
        return (await revit.ExecuteAsync("set_steel_connection_approval", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_steel_connection_status"), Description("Set the code-checking status of a structural connection handler. status = NotCalculated | OkChecked | CheckingFailed.")]
    public static async Task<string> SetSteelConnectionStatus(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler")] long connectionId,
        [Description("Code-checking status: NotCalculated | OkChecked | CheckingFailed")] string status,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId, ["status"] = status };
        return (await revit.ExecuteAsync("set_steel_connection_status", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_steel_connection_default_order"), Description("Reset a structural connection handler to its default element order (SetDefaultElementOrder). Provide connectionId.")]
    public static async Task<string> SetSteelConnectionDefaultOrder(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler")] long connectionId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId };
        return (await revit.ExecuteAsync("set_steel_connection_default_order", p, ct)).ToString();
    }

    [McpServerTool(Name = "delete_steel_connection"), Description("Delete a structural connection handler by connectionId. Destructive — supports dryRun to preview. The connected elements themselves are not deleted.")]
    public static async Task<string> DeleteSteelConnection(
        RevitConnectionManager revit,
        [Description("Element id of the StructuralConnectionHandler to delete")] long connectionId,
        [Description("Preview without deleting. Default false")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["connectionId"] = connectionId };
        if (dryRun != null) p["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("delete_steel_connection", p, ct)).ToString();
    }
}

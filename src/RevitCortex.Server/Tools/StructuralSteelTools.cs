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
}

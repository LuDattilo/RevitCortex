using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class LinkTools
{
    [McpServerTool(Name = "add_linked_file"), Description("Adds a new Revit linked file from a file path and optionally places an instance at the given position.")]
    public static async Task<string> AddLinkedFile(
        RevitConnectionManager revit,
        [Description("Path to the .rvt file to link")] string filePath,
        [Description("Initial X position in mm. Default: 0")] double? positionX = null,
        [Description("Initial Y position in mm. Default: 0")] double? positionY = null,
        [Description("Initial Z position in mm. Default: 0")] double? positionZ = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (positionX != null) p["positionX"] = positionX;
        if (positionY != null) p["positionY"] = positionY;
        if (positionZ != null) p["positionZ"] = positionZ;
        var result = await revit.ExecuteAsync("add_linked_file", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "align_link_to_host"), Description("Aligns a link instance to the host project's internal origin, shared coordinates, or project base point.")]
    public static async Task<string> AlignLinkToHost(
        RevitConnectionManager revit,
        [Description("Link instance element ID")] long instanceId,
        [Description("Alignment mode: origin | shared | base. Default: origin")] string? alignMode = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["instanceId"] = instanceId };
        if (alignMode != null) p["alignMode"] = alignMode;
        var result = await revit.ExecuteAsync("align_link_to_host", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_link_transform"), Description("Returns the full transform of a linked file instance.")]
    public static async Task<string> GetLinkTransform(
        RevitConnectionManager revit,
        [Description("Element ID of the link instance")] long linkInstanceId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["instanceId"] = linkInstanceId };
        var result = await revit.ExecuteAsync("get_link_transform", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_linked_file_instances"), Description("Lists all linked Revit files grouped by type, with transforms and load status.")]
    public static async Task<string> GetLinkedFileInstances(
        RevitConnectionManager revit,
        [Description("Strip transform matrix (origin/basisX/basisY) per instance. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("list_linked_file_instances", new JObject(), ct);
        return ToolResponseShaper.Shape("list_linked_file_instances", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "list_coordination_models"), Description("Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances.")]
    public static async Task<string> GetCoordinationModels(
        RevitConnectionManager revit,
        [Description("Optional case-insensitive filter applied to coordination model names.")] string? nameFilter = null,
        [Description("Include instance records. Default: true.")] bool includeInstances = true,
        [Description("Maximum instance records to include. Default: 100, cap: 250.")] int? maxInstances = null,
        [Description("Strip per-item verbose metadata (path, pathType, origin, instance name) while preserving counters and identifiers. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (nameFilter != null) p["nameFilter"] = nameFilter;
        p["includeInstances"] = includeInstances;
        if (maxInstances != null) p["maxInstances"] = maxInstances;
        if (compact) p["compact"] = compact;
        var result = await revit.ExecuteAsync("list_coordination_models", p, ct);
        return ToolResponseShaper.Shape("list_coordination_models", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "get_selected_linked_elements"), Description("Returns info about currently selected link instances.")]
    public static async Task<string> GetSelectedLinkedElements(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_selected_linked_elements", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "highlight_linked_element"), Description("Highlights an element inside a linked model with an optional section box.")]
    public static async Task<string> HighlightLinkedElement(
        RevitConnectionManager revit,
        [Description("Link instance element ID")] long instanceId,
        [Description("Linked element ID inside the linked model")] long linkedElementId,
        [Description("Create a section box around the element. Default: true")] bool createSectionBox = true,
        [Description("Section box padding in mm. Default: 1000")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["instanceId"] = instanceId,
            ["linkedElementId"] = linkedElementId,
        };
        p["createSectionBox"] = createSectionBox;
        if (offset != null) p["offset"] = offset;
        var result = await revit.ExecuteAsync("highlight_linked_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "show_cross_model_elements"), Description("Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc around each linked element's bounding box (synchronous, transactional, robust); (b) usePostCommandIsolate=true — use Revit's native IsolateElements via PostCommand after SetReferences (canonical Revit API pattern, but asynchronous: tool returns before isolate completes, and cannot be combined with section box / overrides in the same call).")]
    public static async Task<string> ShowCrossModelElements(
        RevitConnectionManager revit,
        [Description("Host document element IDs to include. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? hostElementIds = null,
        [Description("JSON array of linked targets: [{\"instanceId\":2409055,\"linkedElementId\":1413682}]")] System.Text.Json.JsonElement? linkedElements = null,
        [Description("Select host elements and linked-element references. Default: true")] bool select = true,
        [Description("Temporarily isolate host elements and link instances. Default: true")] bool isolate = true,
        [Description("Create a 3D section box around all targets. Default: true. Ignored when usePostCommandIsolate=true.")] bool createSectionBox = true,
        [Description("Create red DirectShape markers in the host doc around each linked element's bounding box. Default: true. Ignored when usePostCommandIsolate=true.")] bool createLinkedMarkers = true,
        [Description("Use Revit's native PostCommand(IsolateElement) instead of the marker strategy. Default: false. Asynchronous: tool returns before isolate completes; section box and markers are skipped.")] bool usePostCommandIsolate = false,
        [Description("Section box padding in mm. Default: 1200")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (hostElementIds != null)
        {
            if (!JsonArrayParam.TryParse(hostElementIds, out var hostElementIdsArray))
                return JsonArrayParam.InvalidArrayResult("show_cross_model_elements", "hostElementIds", hostElementIds);
            p["hostElementIds"] = hostElementIdsArray;
        }
        if (linkedElements != null)
        {
            if (!JsonArrayParam.TryParse(linkedElements, out var linkedElementsArray))
                return JsonArrayParam.InvalidArrayResult("show_cross_model_elements", "linkedElements", linkedElements);
            p["linkedElements"] = linkedElementsArray;
        }
        p["select"] = select;
        p["isolate"] = isolate;
        p["createSectionBox"] = createSectionBox;
        p["createLinkedMarkers"] = createLinkedMarkers;
        p["usePostCommandIsolate"] = usePostCommandIsolate;
        if (offset != null) p["offset"] = offset;

        var result = await revit.ExecuteAsync("show_cross_model_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_links"), Description("List, reload, reload-from-path, unload, or remove linked files. To add a NEW link use add_linked_file instead.")]
    public static async Task<string> ManageLinks(
        RevitConnectionManager revit,
        [Description("Action to perform: list | reload | reload_from | unload | remove")] string action = "list",
        [Description("Link element ID (required for reload/reload_from/unload/remove)")] long? linkId = null,
        [Description("New absolute path to reload the link from (required for reload_from)")] string? newPath = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (linkId != null) p["linkId"] = linkId;
        if (newPath != null) p["newPath"] = newPath;
        var result = await revit.ExecuteAsync("manage_links", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "move_link_instance"), Description("Moves a linked file instance. mode=delta applies (x,y,z) as an offset; mode=absolute places the origin at (x,y,z). Values are in mm.")]
    public static async Task<string> MoveLinkInstance(
        RevitConnectionManager revit,
        [Description("Link instance element ID")] long instanceId,
        [Description("X value in mm (delta or absolute depending on mode)")] double? x = null,
        [Description("Y value in mm")] double? y = null,
        [Description("Z value in mm")] double? z = null,
        [Description("Mode: delta | absolute. Default: delta")] string? mode = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["instanceId"] = instanceId };
        if (x != null) p["x"] = x;
        if (y != null) p["y"] = y;
        if (z != null) p["z"] = z;
        if (mode != null) p["mode"] = mode;
        var result = await revit.ExecuteAsync("move_link_instance", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pin_unpin_link_instance"), Description("Pins or unpins linked file instances.")]
    public static async Task<string> PinUnpinLinkInstance(
        RevitConnectionManager revit,
        [Description("Link instance element IDs")] long[] instanceIds,
        [Description("true to pin, false to unpin. Default: true")] bool pin = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["instanceIds"] = new JArray(instanceIds.Cast<object>().ToArray()) };
        p["pin"] = pin;
        var result = await revit.ExecuteAsync("pin_unpin_link_instance", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "clean_cad_links"), Description("Analyze and clean up imported/linked CAD files. action=list|delete.")]
    public static async Task<string> CadLinkCleanup(
        RevitConnectionManager revit,
        [Description("Action: list | delete. Default: list")] string? action = null,
        [Description("Delete imported CAD instances. Default: false")] bool deleteImports = false,
        [Description("Delete linked CAD instances. Default: false")] bool deleteLinks = false,
        [Description("Specific element IDs to target (optional). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        p["deleteImports"] = deleteImports;
        p["deleteLinks"] = deleteLinks;
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("clean_cad_links", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        var result = await revit.ExecuteAsync("clean_cad_links", p, ct);
        return result.ToString();
    }
}

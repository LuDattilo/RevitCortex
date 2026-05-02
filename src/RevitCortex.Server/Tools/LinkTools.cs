using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

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
        var p = new JObject { ["linkInstanceId"] = linkInstanceId };
        var result = await revit.ExecuteAsync("get_link_transform", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_linked_file_instances"), Description("Lists all linked Revit files grouped by type, with transforms and load status.")]
    public static async Task<string> GetLinkedFileInstances(
        RevitConnectionManager revit,
        [Description("Strip transform matrix (origin/basisX/basisY) per instance. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_linked_file_instances", new JObject(), ct);
        return ToolResponseShaper.Shape("get_linked_file_instances", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "get_coordination_models"), Description("Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances.")]
    public static async Task<string> GetCoordinationModels(
        RevitConnectionManager revit,
        [Description("Optional case-insensitive filter applied to coordination model names.")] string? nameFilter = null,
        [Description("Include instance records. Default: true.")] bool? includeInstances = null,
        [Description("Maximum instance records to include. Default: 100, cap: 250.")] int? maxInstances = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (nameFilter != null) p["nameFilter"] = nameFilter;
        if (includeInstances != null) p["includeInstances"] = includeInstances;
        if (maxInstances != null) p["maxInstances"] = maxInstances;
        var result = await revit.ExecuteAsync("get_coordination_models", p, ct);
        return result.ToString();
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
        [Description("Create a section box around the element. Default: true")] bool? createSectionBox = null,
        [Description("Section box padding in mm. Default: 1000")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["instanceId"] = instanceId,
            ["linkedElementId"] = linkedElementId,
        };
        if (createSectionBox != null) p["createSectionBox"] = createSectionBox;
        if (offset != null) p["offset"] = offset;
        var result = await revit.ExecuteAsync("highlight_linked_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "show_cross_model_elements"), Description("Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc around each linked element's bounding box (synchronous, transactional, robust); (b) usePostCommandIsolate=true — use Revit's native IsolateElements via PostCommand after SetReferences (canonical Revit API pattern, but asynchronous: tool returns before isolate completes, and cannot be combined with section box / overrides in the same call).")]
    public static async Task<string> ShowCrossModelElements(
        RevitConnectionManager revit,
        [Description("Host document element IDs to include")] long[]? hostElementIds = null,
        [Description("JSON array of linked targets: [{\"instanceId\":2409055,\"linkedElementId\":1413682}]")] string? linkedElements = null,
        [Description("Select host elements and linked-element references. Default: true")] bool? select = null,
        [Description("Temporarily isolate host elements and link instances. Default: true")] bool? isolate = null,
        [Description("Create a 3D section box around all targets. Default: true. Ignored when usePostCommandIsolate=true.")] bool? createSectionBox = null,
        [Description("Create red DirectShape markers in the host doc around each linked element's bounding box. Default: true. Ignored when usePostCommandIsolate=true.")] bool? createLinkedMarkers = null,
        [Description("Use Revit's native PostCommand(IsolateElement) instead of the marker strategy. Default: false. Asynchronous: tool returns before isolate completes; section box and markers are skipped.")] bool? usePostCommandIsolate = null,
        [Description("Section box padding in mm. Default: 1200")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (hostElementIds != null) p["hostElementIds"] = new JArray(hostElementIds.Cast<object>().ToArray());
        if (linkedElements != null) p["linkedElements"] = JArray.Parse(linkedElements);
        if (select != null) p["select"] = select;
        if (isolate != null) p["isolate"] = isolate;
        if (createSectionBox != null) p["createSectionBox"] = createSectionBox;
        if (createLinkedMarkers != null) p["createLinkedMarkers"] = createLinkedMarkers;
        if (usePostCommandIsolate != null) p["usePostCommandIsolate"] = usePostCommandIsolate;
        if (offset != null) p["offset"] = offset;

        var result = await revit.ExecuteAsync("show_cross_model_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_links"), Description("List, reload, or unload linked files.")]
    public static async Task<string> ManageLinks(
        RevitConnectionManager revit,
        [Description("Action to perform: list, reload, unload")] string action = "list",
        [Description("Link element ID (required for reload/unload)")] long? linkId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (linkId != null) p["linkId"] = linkId;
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
        [Description("true to pin, false to unpin. Default: true")] bool? pin = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["instanceIds"] = new JArray(instanceIds.Cast<object>().ToArray()) };
        if (pin != null) p["pin"] = pin;
        var result = await revit.ExecuteAsync("pin_unpin_link_instance", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "reload_linked_file_from"), Description("Reloads a linked Revit file from a different file path.")]
    public static async Task<string> ReloadLinkedFileFrom(
        RevitConnectionManager revit,
        [Description("Link TYPE element ID (not the instance ID)")] long linkTypeId,
        [Description("New file path to reload the link from")] string newPath,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["linkTypeId"] = linkTypeId,
            ["newPath"] = newPath,
        };
        var result = await revit.ExecuteAsync("reload_linked_file_from", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "cad_link_cleanup"), Description("Analyze and clean up imported/linked CAD files. action=list|delete.")]
    public static async Task<string> CadLinkCleanup(
        RevitConnectionManager revit,
        [Description("Action: list | delete. Default: list")] string? action = null,
        [Description("Delete imported CAD instances. Default: false")] bool? deleteImports = null,
        [Description("Delete linked CAD instances. Default: false")] bool? deleteLinks = null,
        [Description("Specific element IDs to target (optional)")] long[]? elementIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (deleteImports != null) p["deleteImports"] = deleteImports;
        if (deleteLinks != null) p["deleteLinks"] = deleteLinks;
        if (elementIds != null) p["elementIds"] = new JArray(elementIds.Cast<object>().ToArray());
        var result = await revit.ExecuteAsync("cad_link_cleanup", p, ct);
        return result.ToString();
    }
}

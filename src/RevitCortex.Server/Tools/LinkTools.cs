using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class LinkTools
{
    [McpServerTool(Name = "add_linked_file"), Description("Adds a new Revit linked file from a file path and optionally places an instance.")]
    public static async Task<string> AddLinkedFile(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"filePath\":\"C:\\\\path\\\\to\\\\file.rvt\", \"positionMode\":\"shared\"})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("add_linked_file", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "align_link_to_host"), Description("Aligns a link instance to the host project's internal origin or shared coordinates.")]
    public static async Task<string> AlignLinkToHost(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"linkInstanceId\":12345, \"coordinateSystem\":\"shared\"})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("align_link_to_host", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_link_transform"), Description("Returns the full transform of a linked file instance.")]
    public static async Task<string> GetLinkTransform(
        RevitConnectionManager revit,
        [Description("Element ID of the link instance")] int linkInstanceId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["linkInstanceId"] = linkInstanceId };
        var result = await revit.ExecuteAsync("get_link_transform", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_linked_file_instances"), Description("Lists all linked Revit files grouped by type, with transforms and load status.")]
    public static async Task<string> GetLinkedFileInstances(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_linked_file_instances", new JObject(), ct);
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

    [McpServerTool(Name = "highlight_linked_element"), Description("Highlights an element inside a linked model with section box.")]
    public static async Task<string> HighlightLinkedElement(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"linkInstanceId\":123, \"elementId\":456, \"zoomToFit\":true})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("highlight_linked_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_links"), Description("List, reload, or unload linked files.")]
    public static async Task<string> ManageLinks(
        RevitConnectionManager revit,
        [Description("Action to perform: list, reload, unload")] string action = "list",
        [Description("Link element ID (required for reload/unload)")] int? linkId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (linkId != null) p["linkId"] = linkId;
        var result = await revit.ExecuteAsync("manage_links", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "move_link_instance"), Description("Moves a linked file instance by delta offset or to absolute position.")]
    public static async Task<string> MoveLinkInstance(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"linkInstanceId\":123, \"deltaX\":10.0, \"deltaY\":0, \"deltaZ\":0})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("move_link_instance", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pin_unpin_link_instance"), Description("Pins or unpins linked file instances.")]
    public static async Task<string> PinUnpinLinkInstance(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"linkInstanceIds\":[123,456], \"pin\":true})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("pin_unpin_link_instance", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "reload_linked_file_from"), Description("Reloads a linked Revit file from a different file path.")]
    public static async Task<string> ReloadLinkedFileFrom(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"linkId\":123, \"newFilePath\":\"C:\\\\new\\\\path.rvt\"})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("reload_linked_file_from", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "cad_link_cleanup"), Description("Analyze and clean up imported/linked CAD files.")]
    public static async Task<string> CadLinkCleanup(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"action\":\"analyze\", \"deleteUnused\":false})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("cad_link_cleanup", p, ct);
        return result.ToString();
    }
}

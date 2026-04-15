using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ElementTools
{
    [McpServerTool(Name = "get_element_parameters"), Description("Get all parameters of specific elements by their Revit element IDs.")]
    public static async Task<string> GetElementParameters(
        RevitConnectionManager revit,
        [Description("Array of Revit element IDs to query")] int[] elementIds,
        [Description("Include type-level parameters. Default: true")] bool includeTypeParameters = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray()),
            ["includeTypeParameters"] = includeTypeParameters,
        };
        var result = await revit.ExecuteAsync("get_element_parameters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ai_element_filter"), Description("Query elements by category, parameter filters, and conditions. Supports type and instance filtering.")]
    public static async Task<string> AIElementFilter(
        RevitConnectionManager revit,
        [Description("BuiltInCategory code, e.g. OST_Walls, OST_Doors")] string? filterCategory = null,
        [Description("Include type elements")] bool includeTypes = false,
        [Description("Include instance elements")] bool includeInstances = true,
        [Description("Max elements to return")] int maxElements = 100,
        CancellationToken ct = default)
    {
        var data = new JObject();
        if (filterCategory != null) data["filterCategory"] = filterCategory;
        data["includeTypes"] = includeTypes;
        data["includeInstances"] = includeInstances;
        data["maxElements"] = maxElements;

        var p = new JObject { ["data"] = data };
        var result = await revit.ExecuteAsync("ai_element_filter", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_selected_elements"), Description("Get currently selected elements in Revit.")]
    public static async Task<string> GetSelectedElements(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_selected_elements", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "operate_element"), Description("Select, highlight, isolate, hide, or zoom to elements. Actions: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, resetisolate, delete.")]
    public static async Task<string> OperateElement(
        RevitConnectionManager revit,
        [Description("Element IDs to operate on")] int[] elementIds,
        [Description("Action to perform")] string action,
        CancellationToken ct = default)
    {
        var data = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray()),
            ["action"] = action,
        };
        var p = new JObject { ["data"] = data };
        var result = await revit.ExecuteAsync("operate_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "copy_elements"), Description("Copy elements with optional offset")]
    public static async Task<string> CopyElements(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("copy_elements", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_selection"), Description("Delete a saved selection filter")]
    public static async Task<string> DeleteSelection(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("delete_selection", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "find_undimensioned_elements"), Description("Find elements not referenced by dimensions")]
    public static async Task<string> FindUndimensionedElements(
        RevitConnectionManager revit,
        [Description("Category to filter (e.g. Walls, Doors)")] string? category = null,
        [Description("View element ID to search in")] int? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        if (viewId != null) p["viewId"] = viewId;
        var result = await revit.ExecuteAsync("find_undimensioned_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "find_untagged_elements"), Description("Find elements without tags in a view")]
    public static async Task<string> FindUntaggedElements(
        RevitConnectionManager revit,
        [Description("Category to filter (e.g. Walls, Doors)")] string? category = null,
        [Description("View element ID to search in")] int? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        if (viewId != null) p["viewId"] = viewId;
        var result = await revit.ExecuteAsync("find_untagged_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "match_element_properties"), Description("Copy parameter values from source to target elements")]
    public static async Task<string> MatchElementProperties(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("match_element_properties", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "measure_between_elements"), Description("Measure distance between elements or points in mm")]
    public static async Task<string> MeasureBetweenElements(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("measure_between_elements", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "renumber_elements"), Description("Renumber rooms/doors/windows by location or name")]
    public static async Task<string> RenumberElements(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("renumber_elements", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "save_selection"), Description("Save element selection as named filter")]
    public static async Task<string> SaveSelection(
        RevitConnectionManager revit,
        [Description("Name for the saved selection")] string name,
        CancellationToken ct = default)
    {
        var p = new JObject { ["name"] = name };
        var result = await revit.ExecuteAsync("save_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "load_selection"), Description("List or load saved selections")]
    public static async Task<string> LoadSelection(
        RevitConnectionManager revit,
        [Description("Action to perform (list or load). Default: list")] string action = "list",
        [Description("Name of the selection to load")] string? name = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name != null) p["name"] = name;
        var result = await revit.ExecuteAsync("load_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "section_box_from_selection"), Description("Create a 3D section box from selected elements")]
    public static async Task<string> SectionBoxFromSelection(
        RevitConnectionManager revit,
        [Description("Element IDs to create section box from")] int[]? elementIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null) p["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray());
        var result = await revit.ExecuteAsync("section_box_from_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_phase"), Description("Assign created/demolished phase to elements")]
    public static async Task<string> SetElementPhase(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("set_element_phase", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_workset"), Description("Move elements to a different workset")]
    public static async Task<string> SetElementWorkset(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("set_element_workset", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_elements_in_spatial_volume"), Description("Find elements within a 3D bounding box or room volume")]
    public static async Task<string> GetElementsInSpatialVolume(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_elements_in_spatial_volume", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_linked_elements"), Description("Query elements from linked Revit models with optional filtering")]
    public static async Task<string> GetLinkedElements(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_linked_elements", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_room_openings"), Description("Get doors/windows adjacent to rooms with dimensions")]
    public static async Task<string> GetRoomOpenings(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_room_openings", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "modify_element"), Description("Move, rotate, mirror, or copy elements")]
    public static async Task<string> ModifyElement(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("modify_element", JObject.Parse(data), ct);
        return result.ToString();
    }
}

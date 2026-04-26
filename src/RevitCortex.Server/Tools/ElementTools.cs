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
        [Description("Array of Revit element IDs to query")] long[] elementIds,
        [Description("Include type-level parameters. Default: true")] bool includeTypeParameters = true,
        [Description("Return compact parameter rows (name+value only) and skip empty params. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["includeTypeParameters"] = includeTypeParameters,
        };
        var result = await revit.ExecuteAsync("get_element_parameters", p, ct);
        return ToolResponseShaper.Shape("get_element_parameters", result, compact, summaryOnly: false).ToString();
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
        [Description("Element IDs to operate on")] long[] elementIds,
        [Description("Action to perform")] string action,
        CancellationToken ct = default)
    {
        var data = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["action"] = action,
        };
        var p = new JObject { ["data"] = data };
        var result = await revit.ExecuteAsync("operate_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "copy_elements"), Description("Copy elements with optional offset. Can target a different view.")]
    public static async Task<string> CopyElements(
        RevitConnectionManager revit,
        [Description("Element IDs to copy")] long[] elementIds,
        [Description("Source view ID (optional)")] long? sourceViewId = null,
        [Description("Target view ID (optional)")] long? targetViewId = null,
        [Description("Offset X in project units. Default: 0")] double? offsetX = null,
        [Description("Offset Y in project units. Default: 0")] double? offsetY = null,
        [Description("Offset Z in project units. Default: 0")] double? offsetZ = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
        };
        if (sourceViewId != null) p["sourceViewId"] = sourceViewId;
        if (targetViewId != null) p["targetViewId"] = targetViewId;
        if (offsetX != null) p["offsetX"] = offsetX;
        if (offsetY != null) p["offsetY"] = offsetY;
        if (offsetZ != null) p["offsetZ"] = offsetZ;
        var result = await revit.ExecuteAsync("copy_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_selection"), Description("Delete a saved selection filter by name")]
    public static async Task<string> DeleteSelection(
        RevitConnectionManager revit,
        [Description("Name of the saved selection to delete")] string name,
        CancellationToken ct = default)
    {
        var p = new JObject { ["name"] = name };
        var result = await revit.ExecuteAsync("delete_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "find_undimensioned_elements"), Description("Find elements not referenced by dimensions")]
    public static async Task<string> FindUndimensionedElements(
        RevitConnectionManager revit,
        [Description("Category to filter (e.g. Walls, Doors)")] string? category = null,
        [Description("View element ID to search in")] long? viewId = null,
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
        [Description("View element ID to search in")] long? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        if (viewId != null) p["viewId"] = viewId;
        var result = await revit.ExecuteAsync("find_untagged_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "match_element_properties"), Description("Copy parameter values from one source element to one or more target elements.")]
    public static async Task<string> MatchElementProperties(
        RevitConnectionManager revit,
        [Description("Source element ID")] long sourceElementId,
        [Description("Target element IDs")] long[] targetElementIds,
        [Description("Parameter names to copy; if omitted, copies all writable parameters")] string[]? parameterNames = null,
        [Description("Also copy type-level parameters. Default: false")] bool? includeTypeParameters = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sourceElementId"] = sourceElementId,
            ["targetElementIds"] = new JArray(targetElementIds.Cast<object>().ToArray()),
        };
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (includeTypeParameters != null) p["includeTypeParameters"] = includeTypeParameters;
        var result = await revit.ExecuteAsync("match_element_properties", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "measure_between_elements"), Description("Measure distance between two elements or two points in mm. Provide either elementId1/elementId2, or point1/point2 (as JSON arrays [x,y,z]).")]
    public static async Task<string> MeasureBetweenElements(
        RevitConnectionManager revit,
        [Description("First element ID (optional; use point1 as alternative)")] long? elementId1 = null,
        [Description("Second element ID (optional; use point2 as alternative)")] long? elementId2 = null,
        [Description("First point as JSON array [x,y,z] (optional)")] string? point1 = null,
        [Description("Second point as JSON array [x,y,z] (optional)")] string? point2 = null,
        [Description("Measurement mode: center_to_center | closest_face. Default: center_to_center")] string? measureType = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementId1 != null) p["elementId1"] = elementId1;
        if (elementId2 != null) p["elementId2"] = elementId2;
        if (point1 != null) p["point1"] = JArray.Parse(point1);
        if (point2 != null) p["point2"] = JArray.Parse(point2);
        if (measureType != null) p["measureType"] = measureType;
        var result = await revit.ExecuteAsync("measure_between_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "renumber_elements"), Description("Renumber rooms/doors/windows by location or name. Writes into the specified parameter; supports prefix/suffix and start/increment.")]
    public static async Task<string> RenumberElements(
        RevitConnectionManager revit,
        [Description("Element IDs to renumber (optional; omit to use targetCategory)")] long[]? elementIds = null,
        [Description("Category to renumber when elementIds is empty (e.g. Rooms, Doors, Windows)")] string? targetCategory = null,
        [Description("Parameter name to write into (e.g. Number, Mark)")] string? parameterName = null,
        [Description("Starting number. Default: 1")] int? startNumber = null,
        [Description("Increment between values. Default: 1")] int? increment = null,
        [Description("Prefix string")] string? prefix = null,
        [Description("Suffix string")] string? suffix = null,
        [Description("Sort strategy: location | name. Default: location")] string? sortBy = null,
        [Description("Preview without writing. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null) p["elementIds"] = new JArray(elementIds.Cast<object>().ToArray());
        if (targetCategory != null) p["targetCategory"] = targetCategory;
        if (parameterName != null) p["parameterName"] = parameterName;
        if (startNumber != null) p["startNumber"] = startNumber;
        if (increment != null) p["increment"] = increment;
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (sortBy != null) p["sortBy"] = sortBy;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("renumber_elements", p, ct);
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
        [Description("Element IDs to create section box from")] long[]? elementIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null) p["elementIds"] = new JArray(elementIds.Cast<object>().ToArray());
        var result = await revit.ExecuteAsync("section_box_from_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_phase"), Description("Assign created/demolished phase to elements. Pass a JSON array of requests: [{elementId, phaseCreatedId?, phaseDemolishedId?}].")]
    public static async Task<string> SetElementPhase(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{elementId, phaseCreatedId?, phaseDemolishedId?}]")] string requests,
        CancellationToken ct = default)
    {
        var p = new JObject { ["requests"] = JArray.Parse(requests) };
        var result = await revit.ExecuteAsync("set_element_phase", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_workset"), Description("Move elements to a different workset. Pass a JSON array of requests: [{elementId, worksetId|worksetName}].")]
    public static async Task<string> SetElementWorkset(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{elementId, worksetId?, worksetName?}]")] string requests,
        CancellationToken ct = default)
    {
        var p = new JObject { ["requests"] = JArray.Parse(requests) };
        var result = await revit.ExecuteAsync("set_element_workset", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_elements_in_spatial_volume"), Description("Find elements within a 3D bounding box or room volume. volumeType=room uses volumeIds; volumeType=custom uses customMinX..customMaxZ.")]
    public static async Task<string> GetElementsInSpatialVolume(
        RevitConnectionManager revit,
        [Description("Volume type: room | custom. Default: room")] string? volumeType = null,
        [Description("Room element IDs (when volumeType=room)")] long[]? volumeIds = null,
        [Description("Category filter list (e.g. OST_Doors, OST_Walls)")] string[]? categoryFilter = null,
        [Description("Max elements returned per volume. Default: 100")] int? maxElementsPerVolume = null,
        [Description("Custom box min X (when volumeType=custom)")] double? customMinX = null,
        [Description("Custom box min Y (when volumeType=custom)")] double? customMinY = null,
        [Description("Custom box min Z (when volumeType=custom)")] double? customMinZ = null,
        [Description("Custom box max X (when volumeType=custom)")] double? customMaxX = null,
        [Description("Custom box max Y (when volumeType=custom)")] double? customMaxY = null,
        [Description("Custom box max Z (when volumeType=custom)")] double? customMaxZ = null,
        [Description("Strip per-element extras. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (volumeType != null) p["volumeType"] = volumeType;
        if (volumeIds != null) p["volumeIds"] = new JArray(volumeIds.Cast<object>().ToArray());
        if (categoryFilter != null) p["categoryFilter"] = new JArray(categoryFilter);
        if (maxElementsPerVolume != null) p["maxElementsPerVolume"] = maxElementsPerVolume;
        if (customMinX != null) p["customMinX"] = customMinX;
        if (customMinY != null) p["customMinY"] = customMinY;
        if (customMinZ != null) p["customMinZ"] = customMinZ;
        if (customMaxX != null) p["customMaxX"] = customMaxX;
        if (customMaxY != null) p["customMaxY"] = customMaxY;
        if (customMaxZ != null) p["customMaxZ"] = customMaxZ;
        var result = await revit.ExecuteAsync("get_elements_in_spatial_volume", p, ct);
        return ToolResponseShaper.Shape("get_elements_in_spatial_volume", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "get_linked_elements"), Description("Query elements from linked Revit models with optional filtering. parameterNames is additive — without it only basic fields are returned.")]
    public static async Task<string> GetLinkedElements(
        RevitConnectionManager revit,
        [Description("Name of the linked file (optional; omit to search all links)")] string? linkName = null,
        [Description("Categories to include (OST_* codes or display names)")] string[]? categories = null,
        [Description("Parameter names to extract; additive — without this only basic fields are returned")] string[]? parameterNames = null,
        [Description("Max elements returned. Default: 5000")] int? maxElements = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (linkName != null) p["linkName"] = linkName;
        if (categories != null) p["categories"] = new JArray(categories);
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("get_linked_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_room_openings"), Description("Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName.")]
    public static async Task<string> GetRoomOpenings(
        RevitConnectionManager revit,
        [Description("Room element IDs to query")] long[]? roomIds = null,
        [Description("Room numbers to query")] string[]? roomNumbers = null,
        [Description("Level name filter")] string? levelName = null,
        [Description("Element type: doors | windows | both. Default: both")] string? elementType = null,
        [Description("Include room parameters in response. Default: false")] bool? includeRoomParams = null,
        [Description("Include opening element parameters in response. Default: false")] bool? includeElementParams = null,
        [Description("Specific parameter names to extract")] string[]? parameterNames = null,
        [Description("Max elements per room. Default: 100")] int? maxElementsPerRoom = null,
        [Description("Return a compact payload. Default: false")] bool compact = false,
        [Description("Return counts without nested opening arrays. Default: false")] bool summaryOnly = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (roomIds != null) p["roomIds"] = new JArray(roomIds.Cast<object>().ToArray());
        if (roomNumbers != null) p["roomNumbers"] = new JArray(roomNumbers);
        if (levelName != null) p["levelName"] = levelName;
        if (elementType != null) p["elementType"] = elementType;
        if (includeRoomParams != null) p["includeRoomParams"] = includeRoomParams;
        if (includeElementParams != null) p["includeElementParams"] = includeElementParams;
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (maxElementsPerRoom != null) p["maxElementsPerRoom"] = maxElementsPerRoom;
        var result = await revit.ExecuteAsync("get_room_openings", p, ct);
        return ToolResponseShaper.Shape("get_room_openings", result, compact, summaryOnly).ToString();
    }

    [McpServerTool(Name = "modify_element"), Description("Move, rotate, mirror, or copy elements. Action-specific params: move needs 'translation' [x,y,z]; rotate needs 'rotationCenter' [x,y,z] and 'rotationAngle' (radians); mirror needs 'mirrorPlaneOrigin' and 'mirrorPlaneNormal'; copy needs 'copyOffset' [x,y,z].")]
    public static async Task<string> ModifyElement(
        RevitConnectionManager revit,
        [Description("Element IDs to modify")] long[] elementIds,
        [Description("Action: move | rotate | mirror | copy")] string action,
        [Description("Translation vector [x,y,z] for move action (JSON array)")] string? translation = null,
        [Description("Rotation center [x,y,z] for rotate action (JSON array)")] string? rotationCenter = null,
        [Description("Rotation angle in radians for rotate action")] double? rotationAngle = null,
        [Description("Mirror plane origin [x,y,z] (JSON array)")] string? mirrorPlaneOrigin = null,
        [Description("Mirror plane normal [x,y,z] (JSON array)")] string? mirrorPlaneNormal = null,
        [Description("Copy offset [x,y,z] for copy action (JSON array)")] string? copyOffset = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["action"] = action,
        };
        if (translation != null) p["translation"] = JArray.Parse(translation);
        if (rotationCenter != null) p["rotationCenter"] = JArray.Parse(rotationCenter);
        if (rotationAngle != null) p["rotationAngle"] = rotationAngle;
        if (mirrorPlaneOrigin != null) p["mirrorPlaneOrigin"] = JArray.Parse(mirrorPlaneOrigin);
        if (mirrorPlaneNormal != null) p["mirrorPlaneNormal"] = JArray.Parse(mirrorPlaneNormal);
        if (copyOffset != null) p["copyOffset"] = JArray.Parse(copyOffset);
        var result = await revit.ExecuteAsync("modify_element", p, ct);
        return result.ToString();
    }
}

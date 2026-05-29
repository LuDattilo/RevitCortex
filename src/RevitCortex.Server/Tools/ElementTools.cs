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

    [McpServerTool(Name = "ai_element_filter"), Description("Query elements by category, element class, family symbol, bounding box, or level. Filters combine with AND (default) or OR, and the whole set can be inverted (NOT). Supports type and instance filtering. For parameter-VALUE filtering use filter_by_parameter_value.")]
    public static async Task<string> AIElementFilter(
        RevitConnectionManager revit,
        [Description("BuiltInCategory code, e.g. OST_Walls, OST_Doors")] string? filterCategory = null,
        [Description("Include type elements")] bool includeTypes = false,
        [Description("Include instance elements")] bool includeInstances = true,
        [Description("Max elements to return")] int maxElements = 100,
        [Description("Combine the filters with: and | or. Default: and")] string? combineWith = null,
        [Description("Invert the combined filter (NOT) — return elements that do NOT match. Default: false")] bool? invert = null,
        [Description("Restrict instances to a level: JSON {\"levelId\":123} or {\"levelName\":\"L1\"}")] string? levelFilter = null,
        CancellationToken ct = default)
    {
        var data = new JObject();
        if (filterCategory != null) data["filterCategory"] = filterCategory;
        data["includeTypes"] = includeTypes;
        data["includeInstances"] = includeInstances;
        data["maxElements"] = maxElements;
        if (combineWith != null) data["combineWith"] = combineWith;
        if (invert != null) data["invert"] = invert;
        if (levelFilter != null) data["levelFilter"] = JObject.Parse(levelFilter);

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

    [McpServerTool(Name = "get_elements_by_unique_id"), Description("Resolve Revit UniqueId strings to ElementId records for cross-app workflows.")]
    public static async Task<string> ResolveElementsByUniqueId(
        RevitConnectionManager revit,
        [Description("Array of Revit UniqueId strings to resolve")] string[] uniqueIds,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["uniqueIds"] = new JArray(uniqueIds)
        };
        var result = await revit.ExecuteAsync("get_elements_by_unique_id", p, ct);
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

    [McpServerTool(Name = "copy_elements"), Description("Copy elements with optional mm offset. Can target a different view (sourceViewId+targetViewId) or another OPEN document (targetDocumentTitle).")]
    public static async Task<string> CopyElements(
        RevitConnectionManager revit,
        [Description("Element IDs to copy")] long[] elementIds,
        [Description("Source view ID (optional; required with targetViewId)")] long? sourceViewId = null,
        [Description("Target view ID (optional; required with sourceViewId)")] long? targetViewId = null,
        [Description("Title of another open document to copy into (without .rvt). Omit for same-document copy")] string? targetDocumentTitle = null,
        [Description("Offset X in mm. Default: 0")] double? offsetX = null,
        [Description("Offset Y in mm. Default: 0")] double? offsetY = null,
        [Description("Offset Z in mm. Default: 0")] double? offsetZ = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
        };
        if (sourceViewId != null) p["sourceViewId"] = sourceViewId;
        if (targetViewId != null) p["targetViewId"] = targetViewId;
        if (targetDocumentTitle != null) p["targetDocumentTitle"] = targetDocumentTitle;
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
        [Description("Measurement mode: center_to_center | closest_points | bounding_box. closest_points needs two elementIds (uses their bounding-box closest points). Default: center_to_center")] string? measureType = null,
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
        [Description("For volumeType=room, confirm containment against the real room solid (ClosedShell) instead of the room bounding box. Default: true.")] bool? useRoomSolid = null,
        [Description("Strip per-element extras. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (volumeType != null) p["volumeType"] = volumeType;
        if (volumeIds != null) p["volumeIds"] = new JArray(volumeIds.Cast<object>().ToArray());
        if (categoryFilter != null) p["categoryFilter"] = new JArray(categoryFilter);
        if (maxElementsPerVolume != null) p["maxElementsPerVolume"] = maxElementsPerVolume;
        if (useRoomSolid != null) p["useRoomSolid"] = useRoomSolid;
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

    [McpServerTool(Name = "modify_element"), Description("Move, rotate, mirror, or copy elements. Vectors are {\"x\":mm,\"y\":mm,\"z\":mm} JSON objects. move needs translation; rotate needs rotationCenter + rotationAngle (degrees) and optionally rotationAxis (default Z); mirror needs mirrorPlaneOrigin + mirrorPlaneNormal; copy needs copyOffset.")]
    public static async Task<string> ModifyElement(
        RevitConnectionManager revit,
        [Description("Element IDs to modify")] long[] elementIds,
        [Description("Action: move | rotate | mirror | copy")] string action,
        [Description("Translation vector {x,y,z} in mm for move (JSON object)")] string? translation = null,
        [Description("Rotation center {x,y,z} in mm for rotate (JSON object)")] string? rotationCenter = null,
        [Description("Rotation angle in DEGREES for rotate")] double? rotationAngle = null,
        [Description("Rotation axis direction {x,y,z} for rotate (JSON object). Default: Z axis")] string? rotationAxis = null,
        [Description("Mirror plane origin {x,y,z} in mm (JSON object)")] string? mirrorPlaneOrigin = null,
        [Description("Mirror plane normal {x,y,z} unit vector (JSON object)")] string? mirrorPlaneNormal = null,
        [Description("Copy offset {x,y,z} in mm for copy (JSON object)")] string? copyOffset = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["action"] = action,
        };
        if (translation != null) p["translation"] = JToken.Parse(translation);
        if (rotationCenter != null) p["rotationCenter"] = JToken.Parse(rotationCenter);
        if (rotationAngle != null) p["rotationAngle"] = rotationAngle;
        if (rotationAxis != null) p["rotationAxis"] = JToken.Parse(rotationAxis);
        if (mirrorPlaneOrigin != null) p["mirrorPlaneOrigin"] = JToken.Parse(mirrorPlaneOrigin);
        if (mirrorPlaneNormal != null) p["mirrorPlaneNormal"] = JToken.Parse(mirrorPlaneNormal);
        if (copyOffset != null) p["copyOffset"] = JToken.Parse(copyOffset);
        var result = await revit.ExecuteAsync("modify_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "push_to_powerbi"), Description("Export element parameters or existing schedules to CSV files in a local/OneDrive folder for Power BI refresh. Two modes: (1) categories + parameterNames -> one elements.csv; (2) scheduleIds -> one schedule_<Name>.csv per schedule. Scope can be limited to active view or current selection. Defaults to OneDrive GPA Ingegneria Srl\\RevitCortex\\<DocumentName>\\.")]
    public static async Task<string> PushToPowerBi(
        RevitConnectionManager revit,
        [Description("Categories to export (OST_* codes or display names). Omit for all. Used in category mode.")] string[]? categories = null,
        [Description("Parameter names to include as columns. Omit to auto-discover from first 100 elements. Used in category mode.")] string[]? parameterNames = null,
        [Description("Existing schedule (ViewSchedule) IDs to export. When set, switches to schedule mode (one CSV per schedule).")] long[]? scheduleIds = null,
        [Description("Also include type-level parameters. Default: false. Category mode only.")] bool includeTypeParameters = false,
        [Description("Max elements to export. Default: 10000. Category mode only.")] int maxElements = 10000,
        [Description("Scope: WholeModel (default), ActiveView, Selection.")] string? scopeMode = null,
        [Description("Element IDs for Selection scope.")] long[]? selectionIds = null,
        [Description("View ID for ActiveView scope (defaults to the document's active view).")] long? activeViewId = null,
        [Description("Output folder path. Defaults to OneDrive GPA Ingegneria Srl\\RevitCortex\\<DocumentName>.")] string? outputFolder = null,
        [Description("CSV file name (without path). Defaults to elements_<timestamp>.csv. Category mode only.")] string? fileName = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["includeTypeParameters"] = includeTypeParameters,
            ["maxElements"] = maxElements,
        };
        if (categories != null) p["categories"] = new JArray(categories);
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (scheduleIds != null) p["scheduleIds"] = new JArray(scheduleIds.Cast<object>().ToArray());
        if (scopeMode != null) p["scopeMode"] = scopeMode;
        if (selectionIds != null) p["selectionIds"] = new JArray(selectionIds.Cast<object>().ToArray());
        if (activeViewId != null) p["activeViewId"] = activeViewId;
        if (outputFolder != null) p["outputFolder"] = outputFolder;
        if (fileName != null) p["fileName"] = fileName;
        var result = await revit.ExecuteAsync("push_to_powerbi", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "select_from_powerbi"), Description("Selects/zooms/isolates elements in the active Revit view from a Power BI drillthrough. Action: select | highlight | isolate.")]
    public static async Task<string> SelectFromPowerBi(
        RevitConnectionManager revit,
        [Description("Element IDs to focus")] long[] elementIds,
        [Description("Action: select | highlight | isolate. Default: select")] string action = "select",
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["action"] = action,
        };
        var result = await revit.ExecuteAsync("select_from_powerbi", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "push_table_to_powerbi"), Description("Writes an arbitrary table (headers + rows) to a CSV in the RevitCortex/OneDrive folder for Power BI. Use when the data was computed by you (LLM analysis, multi-doc aggregation, etc.) and is NOT a direct dump of Revit elements. Pass headers as a JSON array of strings; pass rows as a JSON array where each item is either an array (positional) or an object keyed by header. The file lives next to push_to_powerbi outputs and can be picked up by the same Power BI folder source.")]
    public static async Task<string> PushTableToPowerBi(
        RevitConnectionManager revit,
        [Description("JSON array of column header strings, e.g. [\"Level\",\"Volume\",\"Count\"].")] string headers,
        [Description("JSON array of rows. Each row is either an array of values matching headers' order, or an object keyed by header name.")] string rows,
        [Description("Output folder. Defaults to OneDrive\\RevitCortex\\<DocumentName-or-ChatTables>.")] string? outputFolder = null,
        [Description("Optional subfolder name appended to outputFolder for grouping (e.g. 'Analyses').")] string? subfolder = null,
        [Description("CSV file name. Defaults to table_<timestamp>.csv. Use a stable name for repeated overwrites.")] string? fileName = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["headers"] = JArray.Parse(headers),
            ["rows"] = JArray.Parse(rows),
        };
        if (outputFolder != null) p["outputFolder"] = outputFolder;
        if (subfolder != null) p["subfolder"] = subfolder;
        if (fileName != null) p["fileName"] = fileName;
        var result = await revit.ExecuteAsync("push_table_to_powerbi", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_check_auth"), Description("Reports the user's Power BI sign-in state. With signIn=true, starts an MSAL device-code flow inside Revit (a TaskDialog will show the URL + code; the user finishes login in their browser). Token is cached DPAPI-encrypted in %LOCALAPPDATA%\\.revitcortex\\msal_cache.bin.")]
    public static async Task<string> PbiCheckAuth(
        RevitConnectionManager revit,
        [Description("If true and not already signed in, kick off device-code flow and wait until the user signs in.")] bool signIn = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["signIn"] = signIn };
        var result = await revit.ExecuteAsync("pbi_check_auth", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_list_workspaces"), Description("Lists Power BI workspaces (groups) accessible to the signed-in user. Read-only.")]
    public static async Task<string> PbiListWorkspaces(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("pbi_list_workspaces", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_list_datasets"), Description("Lists push datasets in a Power BI workspace. Useful for finding an existing RevitCortex dataset before publishing. Read-only.")]
    public static async Task<string> PbiListDatasets(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID — obtained from pbi_list_workspaces.")] string workspaceId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["workspaceId"] = workspaceId };
        var result = await revit.ExecuteAsync("pbi_list_datasets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_create_dataset"), Description("Creates a RevitCortex push dataset in a Power BI workspace. Idempotent: if a dataset with the same name already exists, returns its id without creating a duplicate. Default tables: Metadata, Elements, Selection.")]
    public static async Task<string> PbiCreateDataset(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID.")] string workspaceId,
        [Description("Dataset name. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
        [Description("Tables to include. Allowed: Metadata, Elements, Schedules, ElementParameters, Selection. Default: [Metadata, Elements, Selection].")] string[]? tables = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["workspaceId"] = workspaceId };
        if (datasetName != null) p["datasetName"] = datasetName;
        if (tables != null) p["tables"] = new JArray(tables);
        var result = await revit.ExecuteAsync("pbi_create_dataset", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_publish_elements"), Description("Publishes Revit model elements to a Power BI push dataset (Elements table). workspaceId and datasetId can be omitted if a ProjectBinding was saved by a previous successful publish for this document. Supports replace (full snapshot, clears old rows), append (keeps old rows), and create (auto-create dataset if missing then replace). Snapshot runs on the Revit main thread; HTTP publish runs in the background.")]
    public static async Task<string> PbiPublishElements(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding was saved by a previous publish for this document.")] string? workspaceId = null,
        [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName (and created if mode allows).")] string? datasetId = null,
        [Description("Dataset name used for lookup/create. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
        [Description("Publish mode: 'replace' (default, delete rows then post snapshot), 'append' (add rows, keep existing), 'create' (auto-create if missing then replace).")] string mode = "replace",
        [Description("OST category codes to filter elements, e.g. [\"OST_Walls\",\"OST_Doors\"]. Omit to export all model elements.")] string[]? categoryFilter = null,
        [Description("Maximum number of elements to export. Default 10000.")] int maxElements = 10000,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["mode"] = mode,
            ["maxElements"] = maxElements
        };
        if (workspaceId != null) p["workspaceId"] = workspaceId;
        if (datasetId != null) p["datasetId"] = datasetId;
        if (datasetName != null) p["datasetName"] = datasetName;
        if (categoryFilter != null) p["categoryFilter"] = new JArray(categoryFilter);
        var result = await revit.ExecuteAsync("pbi_publish_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_publish_schedules"), Description("Publishes Revit schedules to the Power BI Schedules table in long-form (one row per cell). workspaceId and datasetId can be omitted if a ProjectBinding was saved by a previous successful publish for this document. Supports replace and append modes. Snapshot runs on the Revit main thread; HTTP publish runs in the background.")]
    public static async Task<string> PbiPublishSchedules(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding was saved by a previous publish for this document.")] string? workspaceId = null,
        [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName.")] string? datasetId = null,
        [Description("Dataset name used for lookup. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
        [Description("Specific schedule element ids to export. Omit to export all non-template schedules.")] long[]? scheduleIds = null,
        [Description("Publish mode: 'replace' (default, clears existing rows) or 'append'.")] string mode = "replace",
        [Description("Maximum number of source elements to visit per schedule. Default 5000.")] int maxElementsPerSchedule = 5000,
        [Description("Maximum number of long-form rows (cells) to emit per schedule. 0 = no cell cap (only the element cap applies). Useful for bounding rows = elements * fields.")] int maxCellsPerSchedule = 0,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["mode"] = mode,
            ["maxElementsPerSchedule"] = maxElementsPerSchedule,
            ["maxCellsPerSchedule"] = maxCellsPerSchedule
        };
        if (workspaceId != null) p["workspaceId"] = workspaceId;
        if (datasetId != null) p["datasetId"] = datasetId;
        if (datasetName != null) p["datasetName"] = datasetName;
        if (scheduleIds != null) p["scheduleIds"] = new JArray(scheduleIds);
        var result = await revit.ExecuteAsync("pbi_publish_schedules", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_sign_out"), Description("Signs out of Power BI by revoking all cached MSAL tokens. After this call pbi_check_auth(signIn=false) returns signedIn=false. The previously signed-in account is reported in the response. Use pbi_check_auth(signIn=true) to re-authenticate.")]
    public static async Task<string> PbiSignOut(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("pbi_sign_out", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_get_binding"), Description("Returns the Power BI ProjectBinding stored for the active Revit document (workspaceId, datasetId, datasetName, docKey, updatedAt). Returns bound=false with a tip if no binding exists yet. Read-only — call this to verify which workspace/dataset a document is linked to before publishing.")]
    public static async Task<string> PbiGetBinding(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("pbi_get_binding", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_publish_selection"), Description("Publishes the current Revit selection to the Power BI Selection table (one row per selected element). Each call replaces the previous snapshot (DELETE then POST). workspaceId and datasetId can be omitted when a ProjectBinding was saved by a previous publish for this document. Returns rowCount=0 with a warning if nothing is selected and clearIfEmpty is false.")]
    public static async Task<string> PbiPublishSelection(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding exists for this document.")] string? workspaceId = null,
        [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName.")] string? datasetId = null,
        [Description("Dataset name used for lookup. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
        [Description("If true, DELETE the Selection table rows even when nothing is selected in Revit. Default: false.")] bool clearIfEmpty = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["clearIfEmpty"] = clearIfEmpty };
        if (workspaceId != null) p["workspaceId"] = workspaceId;
        if (datasetId != null)   p["datasetId"]   = datasetId;
        if (datasetName != null) p["datasetName"] = datasetName;
        var result = await revit.ExecuteAsync("pbi_publish_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "pbi_query"), Description("Executes a DAX query against the bound Power BI dataset and selects matching elements in Revit. Use template params (category, level, parameterName+parameterValue, exportRunId) for common filters, or supply a raw daxQuery for advanced queries. action='isolate' temporarily isolates the elements instead of selecting. workspaceId and datasetId can be omitted when a ProjectBinding exists. Returns elementCount=0 with a warning when no elements match.")]
    public static async Task<string> PbiQuery(
        RevitConnectionManager revit,
        [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding exists.")] string? workspaceId = null,
        [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName.")] string? datasetId = null,
        [Description("Dataset name for lookup. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
        [Description("OST category code to filter by, e.g. 'OST_Walls'. Applied as Elements[Category] = value.")] string? category = null,
        [Description("Level name to filter by, e.g. 'Level 1'. Applied as Elements[Level] = value.")] string? level = null,
        [Description("Column name in the Elements table to filter by, e.g. 'Mark'. Requires parameterValue.")] string? parameterName = null,
        [Description("Value to match for parameterName. String equality match. Requires parameterName.")] string? parameterValue = null,
        [Description("ExportRunId GUID from a previous pbi_publish_elements run, to reselect that exact snapshot.")] string? exportRunId = null,
        [Description("Raw DAX query starting with EVALUATE. Overrides all template params. Example: EVALUATE SELECTCOLUMNS(FILTER(Elements, Elements[Area] > 50), \"ElementId\", Elements[ElementId])")] string? daxQuery = null,
        [Description("'select' (default) or 'isolate' — isolate temporarily hides all other elements in the active view.")] string? action = null,
        [Description("Maximum number of ElementIds to select. Default: 5000.")] int maxElements = 5000,
        CancellationToken ct = default)
    {
        var p = new JObject { ["maxElements"] = maxElements };
        if (workspaceId    != null) p["workspaceId"]    = workspaceId;
        if (datasetId      != null) p["datasetId"]      = datasetId;
        if (datasetName    != null) p["datasetName"]    = datasetName;
        if (category       != null) p["category"]       = category;
        if (level          != null) p["level"]          = level;
        if (parameterName  != null) p["parameterName"]  = parameterName;
        if (parameterValue != null) p["parameterValue"] = parameterValue;
        if (exportRunId    != null) p["exportRunId"]    = exportRunId;
        if (daxQuery       != null) p["daxQuery"]       = daxQuery;
        if (action         != null) p["action"]         = action;
        var result = await revit.ExecuteAsync("pbi_query", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_from_powerbi"), Description("Reads a previously-exported (or hand-edited) Power BI CSV and writes parameter values back to Revit elements. Identifies elements via the ElementId column. Built-in fields and read-only parameters are skipped. Defaults to dryRun=true so callers can preview first.")]
    public static async Task<string> ImportFromPowerBi(
        RevitConnectionManager revit,
        [Description("Path to the CSV file (typically the one written by push_to_powerbi).")] string filePath,
        [Description("Preview mode: report what would change without committing. Default: true.")] bool dryRun = true,
        [Description("Name of the column holding the Revit ElementId. Default: 'ElementId'.")] string idColumn = "ElementId",
        [Description("Optional whitelist of column headers to write (others ignored).")] string[]? columns = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["filePath"] = filePath,
            ["dryRun"] = dryRun,
            ["idColumn"] = idColumn,
        };
        if (columns != null) p["columns"] = new JArray(columns);
        var result = await revit.ExecuteAsync("import_from_powerbi", p, ct);
        return result.ToString();
    }
}

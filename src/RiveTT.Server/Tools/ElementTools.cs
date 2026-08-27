using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class ElementTools
{
    [McpServerTool(Name = "get_element_parameters"), Description("Get parameters of elements by Revit element ID. Numeric values come back in PROJECT display units with an explicit unit plus the Revit internal value (internalValue, in ft/ft2/ft3). IDs that no longer exist are listed in notFoundIds with found=false, never as an element with an empty parameter list.")]
    public static async Task<string> GetElementParameters(
        RevitConnectionManager revit,
        [Description("Array of Revit element IDs to query")] long[] elementIds,
        [Description("Include type-level parameters. Default: true")] bool includeTypeParameters = true,
        [Description("Only these parameters, resolved in English or in the document language. Unresolved names are reported in unresolvedParameterNames. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Return compact parameter rows (name+value only) and skip empty params. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
            ["includeTypeParameters"] = includeTypeParameters,
        };
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("get_element_parameters", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        var result = await revit.ExecuteAsync("get_element_parameters", p, ct);
        return ToolResponseShaper.Shape("get_element_parameters", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "get_element_solid_geometry"), Description("Get an element's REAL solid geometry (bounding box, centroid, volume m3, face/edge counts AND inferred cross-section shape: circular/rectangular/complex) in mm and model coordinates. Unlike get_BoundingBox this reflects the actual solid AFTER joins and cuts, and reports the section SHAPE — a 613x613 bbox can be a Ø610 circular column, and a T-beam's bbox includes empty space above the web. Always use this, not the bounding box, when precise placement relative to the real solid matters.")]
    public static async Task<string> GetElementSolidGeometry(
        RevitConnectionManager revit,
        [Description("Revit element ID to inspect")] long elementId,
        [Description("Max solids to detail individually. Default: 20")] int maxSolids = 20,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementId"] = elementId,
            ["maxSolids"] = maxSolids,
        };
        var result = await revit.ExecuteAsync("get_element_solid_geometry", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "filter_elements"), Description("Paginated element query by category, class, family symbol, bounding box, or level. Returns totalCount, returnedCount, appliedLimit and nextCursor. responseMode=summary (default), idsOnly, or details.")]
    public static async Task<string> AIElementFilter(
        RevitConnectionManager revit,
        [Description("BuiltInCategory code, e.g. OST_Walls, OST_Doors")] string? filterCategory = null,
        [Description("Include type elements")] bool includeTypes = false,
        [Description("Include instance elements")] bool includeInstances = true,
        [Description("Maximum elements in this page, 1-500. Default: 100")] int pageSize = 100,
        [Description("Opaque cursor returned by the previous page")] string? cursor = null,
        [Description("Response mode: summary | idsOnly | details. Default: summary")] string? responseMode = "summary",
        [Description("Combine the filters with: and | or. Default: and")] string? combineWith = null,
        [Description("Invert the combined filter (NOT) — return elements that do NOT match. Default: false")] bool invert = false,
        [Description("Restrict instances to a level: JSON {\"levelId\":123} or {\"levelName\":\"L1\"}")] string? levelFilter = null,
        [Description("Optional group filter: grouped | ungrouped")] string? groupStatus = null,
        [Description("Optional wall constraint filter: level_constrained | unconnected | attached | unattached")] string? wallConstraintStatus = null,
        CancellationToken ct = default)
    {
        var data = new JObject();
        if (filterCategory != null) data["filterCategory"] = filterCategory;
        data["includeTypes"] = includeTypes;
        data["includeInstances"] = includeInstances;
        data["pageSize"] = pageSize;
        if (cursor != null) data["cursor"] = cursor;
        if (responseMode != null) data["responseMode"] = responseMode;
        if (combineWith != null) data["combineWith"] = combineWith;
        data["invert"] = invert;
        if (levelFilter != null) data["levelFilter"] = JObject.Parse(levelFilter);
        if (groupStatus != null) data["groupStatus"] = groupStatus;
        if (wallConstraintStatus != null) data["wallConstraintStatus"] = wallConstraintStatus;

        var p = new JObject { ["data"] = data };
        var result = await revit.ExecuteAsync("filter_elements", p, ct);
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

    [McpServerTool(Name = "capture_selection"), Description("Capture explicit element IDs or the current Revit selection as a reusable temporary token. Tokens expire and are scoped to the active document session.")]
    public static async Task<string> CaptureSelection(
        RevitConnectionManager revit,
        [Description("Optional explicit element IDs; omit to capture the current Revit selection. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        [Description("Token lifetime in minutes, 1-120. Default: 15")] int? ttlMinutes = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("capture_selection", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        if (ttlMinutes != null) p["ttlMinutes"] = ttlMinutes;
        return (await revit.ExecuteAsync("capture_selection", p, ct)).ToString();
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

    [McpServerTool(Name = "manage_view_display"), Description("Select, highlight, isolate, hide, or zoom to elements in the active view. Actions: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, resetisolate. To delete elements use delete_element.")]
    public static async Task<string> ManageViewDisplay(
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
        var result = await revit.ExecuteAsync("manage_view_display", p, ct);
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

    [McpServerTool(Name = "manage_selection"), Description("CRUD on named saved selections (SelectionFilterElement). action=save|load|list|delete. name is required for save/load/delete (ignored for list). save: elementIds (absent = current UI selection) and overwrite (default false) apply only here. load: selectInView (default true) applies only here. delete: dryRun (default true) applies only here. Use capture_selection instead for a temporary session-scoped token — this tool always persists into the document.")]
    public static async Task<string> ManageSelection(
        RevitConnectionManager revit,
        [Description("Action: save | load | list | delete")] string action,
        [Description("Selection name (required for save/load/delete, ignored for list)")] string? name = null,
        [Description("Element IDs to save (action=save only). Omit to save the current Revit selection. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        [Description("Replace an existing selection of the same name (action=save only). Default: false")] bool overwrite = false,
        [Description("Select the elements in the active view (action=load only). Default: true")] bool selectInView = true,
        [Description("Preview without deleting (action=delete only). Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name != null) p["name"] = name;
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("manage_selection", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        p["overwrite"] = overwrite;
        p["selectInView"] = selectInView;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("manage_selection", p, ct);
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
        // Plugin reads "categories" as an array; keep "category" too for direct-bridge compat.
        if (category != null) { p["categories"] = new JArray(category); p["category"] = category; }
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
        // Plugin reads "categories" as an array; keep "category" too for direct-bridge compat.
        if (category != null) { p["categories"] = new JArray(category); p["category"] = category; }
        if (viewId != null) p["viewId"] = viewId;
        var result = await revit.ExecuteAsync("find_untagged_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "match_element_properties"), Description("Copy parameter values from one source element to one or more target elements.")]
    public static async Task<string> MatchElementProperties(
        RevitConnectionManager revit,
        [Description("Source element ID")] long sourceElementId,
        [Description("Target element IDs")] long[] targetElementIds,
        [Description("Parameter names to copy; if omitted, copies all writable parameters. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Also copy type-level parameters. Default: false")] bool includeTypeParameters = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sourceElementId"] = sourceElementId,
            ["targetElementIds"] = new JArray(targetElementIds.Cast<object>().ToArray()),
        };
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("match_element_properties", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        p["includeTypeParameters"] = includeTypeParameters;
        var result = await revit.ExecuteAsync("match_element_properties", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "measure_between_elements"), Description("Measure distance between two elements or two points in mm. Provide either elementId1/elementId2, or point1/point2 (as JSON arrays [x,y,z]).")]
    public static async Task<string> MeasureBetweenElements(
        RevitConnectionManager revit,
        [Description("First element ID (optional; use point1 as alternative)")] long? elementId1 = null,
        [Description("Second element ID (optional; use point2 as alternative)")] long? elementId2 = null,
        [Description("First point as JSON array [x,y,z] (optional)")] System.Text.Json.JsonElement? point1 = null,
        [Description("Second point as JSON array [x,y,z] (optional)")] System.Text.Json.JsonElement? point2 = null,
        [Description("Measurement mode: center_to_center | closest_points | bounding_box. closest_points needs two elementIds (uses their bounding-box closest points). Default: center_to_center")] string? measureType = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementId1 != null) p["elementId1"] = elementId1;
        if (elementId2 != null) p["elementId2"] = elementId2;
        if (point1 != null)
        {
            if (!JsonArrayParam.TryParse(point1, out var point1Array))
                return JsonArrayParam.InvalidArrayResult("measure_between_elements", "point1", point1);
            p["point1"] = point1Array;
        }
        if (point2 != null)
        {
            if (!JsonArrayParam.TryParse(point2, out var point2Array))
                return JsonArrayParam.InvalidArrayResult("measure_between_elements", "point2", point2);
            p["point2"] = point2Array;
        }
        if (measureType != null) p["measureType"] = measureType;
        var result = await revit.ExecuteAsync("measure_between_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "renumber_elements"), Description("Renumber rooms/doors/windows by location or name. Writes into the specified parameter; supports prefix/suffix and start/increment.")]
    public static async Task<string> RenumberElements(
        RevitConnectionManager revit,
        [Description("Element IDs to renumber (optional; omit to use targetCategory). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        [Description("Category to renumber when elementIds is empty (e.g. Rooms, Doors, Windows)")] string? targetCategory = null,
        [Description("Parameter name to write into (e.g. Number, Mark)")] string? parameterName = null,
        [Description("Starting number. Default: 1")] int? startNumber = null,
        [Description("Increment between values. Default: 1")] int? increment = null,
        [Description("Prefix string")] string? prefix = null,
        [Description("Suffix string")] string? suffix = null,
        [Description("Sort strategy: location | name. Default: location")] string? sortBy = null,
        [Description("Preview without writing. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("renumber_elements", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        if (targetCategory != null) p["targetCategory"] = targetCategory;
        if (parameterName != null) p["parameterName"] = parameterName;
        if (startNumber != null) p["startNumber"] = startNumber;
        if (increment != null) p["increment"] = increment;
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (sortBy != null) p["sortBy"] = sortBy;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("renumber_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_section_box_from_selection"), Description("Create a 3D section box from selected elements")]
    public static async Task<string> SectionBoxFromSelection(
        RevitConnectionManager revit,
        [Description("Element IDs to create section box from. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("create_section_box_from_selection", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        var result = await revit.ExecuteAsync("create_section_box_from_selection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_phase"), Description("Assign created/demolished phase to elements. Pass a JSON array of requests: [{elementId, createdPhaseId?, demolishedPhaseId?}]. The older names phaseCreatedId / phaseDemolishedId are still accepted.")]
    public static async Task<string> SetElementPhase(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{elementId, createdPhaseId?, demolishedPhaseId?}]")] string requests,
        CancellationToken ct = default)
    {
        var p = new JObject { ["requests"] = JArray.Parse(requests) };
        var result = await revit.ExecuteAsync("set_element_phase", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_element_workset"), Description("Move elements to a different workset. Pass a JSON array of requests: [{elementId, worksetName}]. Worksets are resolved by name only.")]
    public static async Task<string> SetElementWorkset(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{elementId, worksetName}]")] string requests,
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
        [Description("Room element IDs (when volumeType=room). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? volumeIds = null,
        [Description("Category filter list (e.g. OST_Doors, OST_Walls). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categoryFilter = null,
        [Description("Max elements returned per volume. Default: 100")] int? maxElementsPerVolume = null,
        [Description("Custom box min X (when volumeType=custom)")] double? customMinX = null,
        [Description("Custom box min Y (when volumeType=custom)")] double? customMinY = null,
        [Description("Custom box min Z (when volumeType=custom)")] double? customMinZ = null,
        [Description("Custom box max X (when volumeType=custom)")] double? customMaxX = null,
        [Description("Custom box max Y (when volumeType=custom)")] double? customMaxY = null,
        [Description("Custom box max Z (when volumeType=custom)")] double? customMaxZ = null,
        [Description("For volumeType=room, confirm containment against the real room solid (ClosedShell) instead of the room bounding box. Default: true.")] bool useRoomSolid = true,
        [Description("inside (default) = elements contained in the volume; boundary = elements that BOUND the room (walls, columns, separation lines), from Revit boundary segments")] string? containment = null,
        [Description("Strip per-element extras. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (volumeType != null) p["volumeType"] = volumeType;
        if (volumeIds != null)
        {
            if (!JsonArrayParam.TryParse(volumeIds, out var volumeIdsArray))
                return JsonArrayParam.InvalidArrayResult("get_elements_in_spatial_volume", "volumeIds", volumeIds);
            p["volumeIds"] = volumeIdsArray;
        }
        if (categoryFilter != null)
        {
            if (!JsonArrayParam.TryParse(categoryFilter, out var categoryFilterArray))
                return JsonArrayParam.InvalidArrayResult("get_elements_in_spatial_volume", "categoryFilter", categoryFilter);
            p["categoryFilter"] = categoryFilterArray;
        }
        if (maxElementsPerVolume != null) p["maxElementsPerVolume"] = maxElementsPerVolume;
        p["useRoomSolid"] = useRoomSolid;
        if (containment != null) p["containment"] = containment;
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
        [Description("Categories to include (OST_* codes or display names). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Parameter names to extract; additive — without this only basic fields are returned. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Max elements returned. Default: 5000")] int? maxElements = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (linkName != null) p["linkName"] = linkName;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("get_linked_elements", "categories", categories);
            p["categories"] = categoriesArray;
        }
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("get_linked_elements", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("get_linked_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_room_openings"), Description("Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName.")]
    public static async Task<string> GetRoomOpenings(
        RevitConnectionManager revit,
        [Description("Room element IDs to query. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? roomIds = null,
        [Description("Room numbers to query. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? roomNumbers = null,
        [Description("Level name filter")] string? levelName = null,
        [Description("Element type: doors | windows | both. Default: both")] string? elementType = null,
        [Description("Include room parameters in response. Default: false")] bool includeRoomParams = false,
        [Description("Include opening element parameters in response. Default: false")] bool includeElementParams = false,
        [Description("Specific parameter names to extract. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Max elements per room. Default: 100")] int? maxElementsPerRoom = null,
        [Description("Return a compact payload. Default: false")] bool compact = false,
        [Description("Return counts without nested opening arrays. Default: false")] bool summaryOnly = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (roomIds != null)
        {
            if (!JsonArrayParam.TryParse(roomIds, out var roomIdsArray))
                return JsonArrayParam.InvalidArrayResult("get_room_openings", "roomIds", roomIds);
            p["roomIds"] = roomIdsArray;
        }
        if (roomNumbers != null)
        {
            if (!JsonArrayParam.TryParse(roomNumbers, out var roomNumbersArray))
                return JsonArrayParam.InvalidArrayResult("get_room_openings", "roomNumbers", roomNumbers);
            p["roomNumbers"] = roomNumbersArray;
        }
        if (levelName != null) p["levelName"] = levelName;
        if (elementType != null) p["elementType"] = elementType;
        p["includeRoomParams"] = includeRoomParams;
        p["includeElementParams"] = includeElementParams;
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("get_room_openings", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
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

}

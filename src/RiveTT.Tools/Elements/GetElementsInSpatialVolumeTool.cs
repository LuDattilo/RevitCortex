using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Returns elements contained within a spatial volume: room bounding box,
/// area bounding box, or a custom axis-aligned bounding box defined in mm.
/// Mirrors the fork's GetElementsInSpatialVolumeEventHandler logic.
/// </summary>
[ToolSafety(true, false)]
public class GetElementsInSpatialVolumeTool : ICortexTool
{
    public string Name => "get_elements_in_spatial_volume";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns elements contained within a spatial volume: a room, an area, or a custom axis-aligned bounding box (mm). For rooms, true solid containment (Room ClosedShell) is used by default to avoid the over-reporting of an L-shaped room's bounding box; set useRoomSolid=false for the faster bbox approximation. Set containment=\"boundary\" to get the elements that BOUND the room (walls, columns, separation lines) instead of those inside it: solid containment excludes them by design, and the bounding box pulls in unrelated neighbours. Each volume reports the containment mode actually used.";
    // 1 foot = MmPerFoot mm — used for MM<->feet conversions

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Parse inputs ───────────────────────────────────────────────────
        var volumeType          = input["volumeType"]?.ToString() ?? "room";
        var volumeIds           = input["volumeIds"]?.ToObject<List<long>>() ?? new List<long>();
        var categoryFilter      = input["categoryFilter"]?.ToObject<List<string>>() ?? new List<string>();
        var maxElementsPerVolume = input["maxElementsPerVolume"]?.Value<int>() ?? 100;
        // For room volumes, confirm bbox candidates against the room's real solid.
        var useRoomSolid        = input["useRoomSolid"]?.Value<bool>() ?? true;
        // "inside" (default) keeps the historical behavior; "boundary" answers the
        // question the bounding box only approximated — which elements delimit this
        // room. Asking for "the walls of the cafeteria" returned 0 with the solid
        // filter (a bounding wall is not inside the room) and 12 unrelated walls
        // with the bounding box once the room geometry changed.
        var containment         = (input["containment"]?.Value<string>() ?? "inside")
                                    .Trim().ToLowerInvariant();
        if (containment is not ("inside" or "boundary"))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"containment '{containment}' is not recognized. Use \"inside\" (default) or \"boundary\".");

        // Custom bounding box coordinates in mm
        var customMinX = input["customMinX"]?.Value<double>() ?? 0;
        var customMinY = input["customMinY"]?.Value<double>() ?? 0;
        var customMinZ = input["customMinZ"]?.Value<double>() ?? 0;
        var customMaxX = input["customMaxX"]?.Value<double>() ?? 0;
        var customMaxY = input["customMaxY"]?.Value<double>() ?? 0;
        var customMaxZ = input["customMaxZ"]?.Value<double>() ?? 0;

        // Validate volumeType
        var normalizedVolumeType = volumeType.ToLowerInvariant();
        if (normalizedVolumeType != "room" && normalizedVolumeType != "area" && normalizedVolumeType != "custom")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Invalid volumeType '{volumeType}'. Must be 'room', 'area', or 'custom'.");

        try
        {
            var volumeResults = new List<object>();
            int totalElements = 0;

            if (normalizedVolumeType == "custom")
            {
                // Convert mm to feet for Revit internal units
                double minXFt = customMinX / MmPerFoot;
                double minYFt = customMinY / MmPerFoot;
                double minZFt = customMinZ / MmPerFoot;
                double maxXFt = customMaxX / MmPerFoot;
                double maxYFt = customMaxY / MmPerFoot;
                double maxZFt = customMaxZ / MmPerFoot;

                var outline  = new Outline(new XYZ(minXFt, minYFt, minZFt), new XYZ(maxXFt, maxYFt, maxZFt));
                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType();

                var elements = FilterByCategories(doc, collector, categoryFilter);

                int totalInVolume = elements.Count;
                bool truncated = elements.Count > maxElementsPerVolume;
                if (truncated)
                    elements = elements.Take(maxElementsPerVolume).ToList();

                totalElements += totalInVolume;
                volumeResults.Add(new
                {
                    volumeType        = "custom",
                    volumeId          = (long)0,
                    volumeName        = "Custom Bounding Box",
                    elementCount      = elements.Count,
                    totalElementCount = totalInVolume,
                    truncated,
                    elements          = elements.Select(FormatElement).ToList()
                });
            }
            else
            {
                // Room or Area
                var bic = normalizedVolumeType == "area"
                    ? BuiltInCategory.OST_Areas
                    : BuiltInCategory.OST_Rooms;

                List<Element> spatialElements;

                if (volumeIds.Count > 0)
                {
                    spatialElements = new List<Element>();
                    foreach (var id in volumeIds)
                    {
                        var elem = doc.GetElement(new ElementId(id));
                        if (elem != null)
                            spatialElements.Add(elem);
                    }
                }
                else
                {
                    spatialElements = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToList();
                }

                foreach (var spatial in spatialElements)
                {
                    var bb = spatial.get_BoundingBox(null);
                    if (bb == null) continue;

                    // Skip rooms/areas with zero or negative area
                    if (spatial is Room room && room.Area <= 0) continue;

                    if (containment == "boundary")
                    {
                        var boundaryResult = BuildBoundaryResult(
                            doc, spatial, normalizedVolumeType, categoryFilter, maxElementsPerVolume,
                            out var boundaryCount);
                        if (boundaryResult != null)
                        {
                            totalElements += boundaryCount;
                            volumeResults.Add(boundaryResult);
                        }
                        continue;
                    }

                    var outline  = new Outline(bb.Min, bb.Max);
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);
                    var collector = new FilteredElementCollector(doc)
                        .WherePasses(bbFilter)
                        .WhereElementIsNotElementType();

                    var elements = FilterByCategories(doc, collector, categoryFilter);

                    // Refine room results against the real room solid (ClosedShell) so an
                    // L-shaped room doesn't pull in elements that only its bbox overlaps.
                    if (useRoomSolid && spatial is Room roomForSolid)
                    {
                        var roomSolid = GetRoomSolid(roomForSolid);
                        if (roomSolid != null)
                        {
                            var candidateIds = elements.Select(e => e.Id).ToList();
                            if (candidateIds.Count > 0)
                            {
                                try
                                {
                                    var inside = new FilteredElementCollector(doc, candidateIds)
                                        .WherePasses(new ElementIntersectsSolidFilter(roomSolid))
                                        .ToElementIds();
                                    var insideSet = new HashSet<ElementId>(inside);
                                    elements = elements.Where(e => insideSet.Contains(e.Id)).ToList();
                                }
                                catch { /* keep bbox candidates on solid-filter failure */ }
                            }
                        }
                    }

                    // Exclude the spatial element itself from results
                    long spatialIdVal = spatial.Id.Value;
                    elements = elements.Where(e => e.Id.Value != spatialIdVal).ToList();

                    // Build a human-readable volume name
                    string volumeName;
                    if (spatial is Room r)
                    {
                        var roomName   = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name;
                        var roomNumber = r.Number;
                        volumeName = string.IsNullOrWhiteSpace(roomNumber)
                            ? roomName
                            : $"{roomNumber} - {roomName}";
                    }
                    else
                    {
                        volumeName = spatial.Name;
                    }

                    int totalInVolume = elements.Count;
                    bool truncated = elements.Count > maxElementsPerVolume;
                    if (truncated)
                        elements = elements.Take(maxElementsPerVolume).ToList();

                    totalElements += totalInVolume;
                    volumeResults.Add(new
                    {
                        volumeType        = normalizedVolumeType,
                        volumeId          = spatial.Id.Value,
                        volumeName,
                        // State the geometry actually used: "solid" excludes bounding
                        // elements, "boundingBox" over-reports, and the difference
                        // explains most surprising result sets.
                        containment       = "inside",
                        geometryUsed      = useRoomSolid && spatial is Room ? "roomSolid" : "boundingBox",
                        elementCount      = elements.Count,
                        totalElementCount = totalInVolume,
                        truncated,
                        elements          = elements.Select(FormatElement).ToList()
                    });
                }
            }

            return CortexResult<object>.Ok(new
            {
                message        = $"Found {totalElements} element(s) across {volumeResults.Count} volume(s) " +
                                 $"(containment={containment})",
                totalElements,
                volumeCount    = volumeResults.Count,
                containment,
                categoryFilter,
                volumes        = volumeResults
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to retrieve elements in spatial volume: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the room's enclosing solid via its geometry (the closed shell Revit
    /// builds from the room's boundaries up to its height). Returns null when no solid
    /// can be obtained (unbounded/unplaced room).
    /// </summary>
    private static Solid? GetRoomSolid(Room room)
    {
        try
        {
            var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
            var geom = room.get_Geometry(opts);
            if (geom == null) return null;
            foreach (var obj in geom)
            {
                if (obj is Solid s && s.Volume > 1e-6)
                    return s;
            }
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>
    /// Filters a collector's results to only those elements whose category
    /// matches one of the provided OST_* category codes. When the list is
    /// empty, all elements are returned unfiltered.
    /// </summary>
    private static List<Element> FilterByCategories(
        Document doc,
        FilteredElementCollector collector,
        List<string> categories)
    {
        var elements = collector.ToList();

        if (categories == null || categories.Count == 0)
            return elements;

        // Resolve via CategoryResolver — accepts OST_* codes, English friendly names, and localized display names.
        var resolvedIds = new HashSet<ElementId>();
        foreach (var catCode in categories)
        {
            var catId = CategoryResolver.ResolveToId(doc, catCode);
            if (catId != null && catId != ElementId.InvalidElementId)
                resolvedIds.Add(catId);
        }

        if (resolvedIds.Count == 0)
            return elements; // no valid codes — return all

        return elements
            .Where(e => e.Category != null && resolvedIds.Contains(e.Category.Id))
            .ToList();
    }

    private static object FormatElement(Element e)
    {
        return new
        {
            elementId  = e.Id.Value,
            name       = e.Name,
            category   = e.Category?.Name ?? "Unknown",
            familyName = (e as FamilyInstance)?.Symbol?.FamilyName ?? "",
            typeName   = (e as FamilyInstance)?.Symbol?.Name ?? ""
        };
    }

    /// <summary>
    /// The elements that BOUND a room, from Revit's own boundary segments
    /// (Room.GetBoundarySegments), not from a geometric guess. Walls, columns,
    /// room separation lines and curtain panels come back with the boundary
    /// length they contribute.
    /// </summary>
    private static object? BuildBoundaryResult(
        Document doc,
        Element spatial,
        string volumeType,
        List<string> categoryFilter,
        int maxElements,
        out int totalCount)
    {
        totalCount = 0;
        if (spatial is not SpatialElement spatialElement) return null;

        var contributions = new Dictionary<ElementId, double>();
        try
        {
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var loops = spatialElement.GetBoundarySegments(options);
            if (loops == null) return null;

            foreach (var loop in loops)
            {
                foreach (var segment in loop)
                {
                    var elementId = segment.ElementId;
                    if (elementId == ElementId.InvalidElementId) continue;

                    var length = 0.0;
                    try { length = segment.GetCurve()?.Length ?? 0; } catch { }

                    contributions[elementId] = contributions.TryGetValue(elementId, out var current)
                        ? current + length
                        : length;
                }
            }
        }
        catch
        {
            return null;
        }

        var resolved = contributions
            .Select(entry => new { element = doc.GetElement(entry.Key), lengthFt = entry.Value })
            .Where(entry => entry.element != null)
            .ToList();

        if (categoryFilter.Count > 0)
        {
            var wanted = categoryFilter
                .Select(name => CategoryResolver.ResolveToId(doc, name))
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .ToHashSet();

            if (wanted.Count > 0)
                resolved = resolved
                    .Where(entry => entry.element!.Category != null && wanted.Contains(entry.element!.Category.Id))
                    .ToList();
        }

        totalCount = resolved.Count;
        var truncated = resolved.Count > maxElements;
        var page = resolved
            .OrderByDescending(entry => entry.lengthFt)
            .Take(maxElements)
            .Select(entry => new
            {
                elementId = ToolHelpers.GetElementIdValue(entry.element!.Id),
                name = entry.element!.Name,
                category = entry.element.Category?.Name,
                categoryBic = CategoryResolver.DescribeBuiltInCategory(entry.element.Category),
                boundaryLengthMm = Math.Round(entry.lengthFt * MmPerFoot, 1)
            })
            .ToList();

        var roomLabel = spatial is Room room
            ? (string.IsNullOrWhiteSpace(room.Number)
                ? room.Name
                : $"{room.Number} - {room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name}")
            : spatial.Name;

        return new
        {
            volumeType,
            volumeId = ToolHelpers.GetElementIdValue(spatial.Id),
            volumeName = roomLabel,
            containment = "boundary",
            geometryUsed = "roomBoundarySegments",
            elementCount = page.Count,
            totalElementCount = totalCount,
            truncated,
            elements = page
        };
    }
}

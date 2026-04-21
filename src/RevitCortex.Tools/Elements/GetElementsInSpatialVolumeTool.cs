using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Returns elements contained within a spatial volume: room bounding box,
/// area bounding box, or a custom axis-aligned bounding box defined in mm.
/// Mirrors the fork's GetElementsInSpatialVolumeEventHandler logic.
/// </summary>
public class GetElementsInSpatialVolumeTool : ICortexTool
{
    public string Name => "get_elements_in_spatial_volume";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns elements contained within a spatial volume: room bounding box, area bounding box, or a custom axis-aligned bounding box defined in mm. Mirrors the fork's GetElementsInSpatialVolumeEventHandler logic.";
    // 1 foot = 304.8 mm — used for MM<->feet conversions
    private const double MmPerFoot = 304.8;

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
#if REVIT2024_OR_GREATER
                        var elem = doc.GetElement(new ElementId(id));
#else
                        var elem = doc.GetElement(new ElementId((int)id));
#endif
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

                    var outline  = new Outline(bb.Min, bb.Max);
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);
                    var collector = new FilteredElementCollector(doc)
                        .WherePasses(bbFilter)
                        .WhereElementIsNotElementType();

                    var elements = FilterByCategories(doc, collector, categoryFilter);

                    // Exclude the spatial element itself from results
#if REVIT2024_OR_GREATER
                    long spatialIdVal = spatial.Id.Value;
                    elements = elements.Where(e => e.Id.Value != spatialIdVal).ToList();
#else
                    int spatialIdVal = spatial.Id.IntegerValue;
                    elements = elements.Where(e => e.Id.IntegerValue != spatialIdVal).ToList();
#endif

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
#if REVIT2024_OR_GREATER
                        volumeId          = spatial.Id.Value,
#else
                        volumeId          = (long)spatial.Id.IntegerValue,
#endif
                        volumeName,
                        elementCount      = elements.Count,
                        totalElementCount = totalInVolume,
                        truncated,
                        elements          = elements.Select(FormatElement).ToList()
                    });
                }
            }

            return CortexResult<object>.Ok(new
            {
                message        = $"Found {totalElements} element(s) across {volumeResults.Count} volume(s)",
                totalElements,
                volumeCount    = volumeResults.Count,
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
#if REVIT2024_OR_GREATER
            elementId  = e.Id.Value,
#else
            elementId  = (long)e.Id.IntegerValue,
#endif
            name       = e.Name,
            category   = e.Category?.Name ?? "Unknown",
            familyName = (e as FamilyInstance)?.Symbol?.FamilyName ?? "",
            typeName   = (e as FamilyInstance)?.Symbol?.Name ?? ""
        };
    }
}

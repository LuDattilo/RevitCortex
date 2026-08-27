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
/// Smart element query tool — supports category, element-class, family-symbol,
/// view-visibility and bounding-box filters, all combinable via logical AND.
/// Mirrors the fork's AIElementFilterEventHandler filtering logic.
/// </summary>
[ToolSafety(true, false)]
public class AIElementFilterTool : ICortexTool
{
    public string Name => "filter_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Smart element query tool — supports category, element-class, family-symbol, view-visibility and bounding-box filters, all combinable via logical AND. Mirrors the fork's AIElementFilterEventHandler filtering logic.";
    // ── Revit internal-unit conversion factor: 1 foot = MmPerFoot mm ──────────

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // The fork wraps parameters in a "data" object — support both layouts
        var data = input["data"] as JObject ?? input;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Parse inputs ───────────────────────────────────────────────────
        var filterCategory     = data["filterCategory"]?.ToString();
        var filterElementType  = data["filterElementType"]?.ToString();
        var filterFamilySymId  = data["filterFamilySymbolId"]?.Value<int>() ?? -1;
        var includeTypes       = data["includeTypes"]?.Value<bool>() ?? false;
        var includeInstances   = data["includeInstances"]?.Value<bool>() ?? true;
        var filterVisibleInView = data["filterVisibleInCurrentView"]?.Value<bool>() ?? false;
        var pageSize           = Math.Clamp(data["pageSize"]?.Value<int>()
                                             ?? data["maxElements"]?.Value<int>() ?? 100, 1, 500);
        var responseMode       = (data["responseMode"]?.Value<string>() ?? "summary").ToLowerInvariant();
        if (responseMode is not ("summary" or "idsonly" or "details"))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "responseMode must be summary, idsOnly, or details");
        if (!TryDecodeCursor(data["cursor"]?.Value<string>(), session.DocumentVersion,
                out var offset, out var cursorError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cursorError!,
                suggestion: "Restart the search without a cursor because the document changed.");

        // Logical combination of the individual filters: "and" (default) or "or".
        var combineWith        = (data["combineWith"]?.Value<string>() ?? "and").ToLowerInvariant();
        // Invert the whole combined filter (NOT) — e.g. "everything that is NOT a wall".
        var invert             = data["invert"]?.Value<bool>() ?? false;
        // Optional level filter: {levelId} or {levelName} — instances on that level only.
        var levelFilterToken   = data["levelFilter"];
        var groupStatus        = data["groupStatus"]?.Value<string>();
        var wallConstraintStatus = data["wallConstraintStatus"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(groupStatus) &&
            !new[] { "grouped", "ungrouped" }.Contains(groupStatus, StringComparer.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "groupStatus must be grouped or ungrouped");
        if (!string.IsNullOrWhiteSpace(wallConstraintStatus) &&
            !new[] { "level_constrained", "unconnected", "attached", "unattached" }
                .Contains(wallConstraintStatus, StringComparer.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "wallConstraintStatus must be level_constrained, unconnected, attached, or unattached");

        // Bounding box (coordinates in mm, matching the fork's convention)
        var bbMinToken = data["boundingBoxMin"];
        var bbMaxToken = data["boundingBoxMax"];
        XYZ? bbMin = ParseXYZ(bbMinToken);
        XYZ? bbMax = ParseXYZ(bbMaxToken);

        // ── Validate ───────────────────────────────────────────────────────
        if (!includeTypes && !includeInstances)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "At least one of includeTypes or includeInstances must be true");

        if (string.IsNullOrWhiteSpace(filterCategory) &&
            string.IsNullOrWhiteSpace(filterElementType) &&
            filterFamilySymId <= 0 &&
            levelFilterToken == null &&
            string.IsNullOrWhiteSpace(groupStatus) &&
            string.IsNullOrWhiteSpace(wallConstraintStatus) &&
            bbMin == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Specify at least one filter: filterCategory, filterElementType, filterFamilySymbolId, levelFilter, groupStatus, wallConstraintStatus, or boundingBox",
                suggestion: "Use OST_* codes for filterCategory, e.g. OST_Walls, OST_Doors");

        if ((bbMin == null) != (bbMax == null))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Both boundingBoxMin and boundingBoxMax must be provided together");

        if (bbMin != null && bbMax != null)
        {
            if (bbMin.X > bbMax.X || bbMin.Y > bbMax.Y || bbMin.Z > bbMax.Z)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "boundingBoxMin coordinates must be less than or equal to boundingBoxMax");
        }

        if (includeTypes && !includeInstances && filterFamilySymId > 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filterFamilySymbolId cannot be combined with includeTypes-only mode");

        if (includeTypes && !includeInstances && filterVisibleInView)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filterVisibleInCurrentView cannot be combined with includeTypes-only mode");

        try
        {
            // ── Collect elements ───────────────────────────────────────────
            // Perf: CollectByKind applies early-exit when maxElements is reached,
            // and returns (totalMatching, takenElements) so we avoid
            // materializing the full list just to count + slice. On large models
            // (1k+ elements per category) this turns an O(N) allocation into O(k)
            // where k = maxElements.
            var elements = new List<Element>();
            int totalCount = 0;

            if (includeInstances && includeTypes)
            {
                var (cntInst, instList) = CollectByKind(doc, isElementType: false,
                    filterCategory, filterElementType, filterFamilySymId,
                    filterVisibleInView, bbMin, bbMax, pageSize, offset,
                    combineWith, invert, levelFilterToken, groupStatus, wallConstraintStatus);
                elements.AddRange(instList);
                totalCount += cntInst;

                // For types we only need the remaining slots (if any); if caller
                // wanted a hard cap we still report true totalCount below.
                int remaining = Math.Max(0, pageSize - elements.Count);
                int typeOffset = Math.Max(0, offset - cntInst);
                var (cntType, typeList) = CollectByKind(doc, isElementType: true,
                    filterCategory, filterElementType, filterFamilySymId: -1,
                    filterVisibleInView: false, bbMin, bbMax,
                    remaining, typeOffset,
                    combineWith, invert, levelFilter: null, groupStatus: groupStatus,
                    wallConstraintStatus: wallConstraintStatus);
                elements.AddRange(typeList);
                totalCount += cntType;
            }
            else if (includeInstances)
            {
                var (cnt, list) = CollectByKind(doc, isElementType: false,
                    filterCategory, filterElementType, filterFamilySymId,
                    filterVisibleInView, bbMin, bbMax, pageSize, offset,
                    combineWith, invert, levelFilterToken, groupStatus, wallConstraintStatus);
                elements.AddRange(list);
                totalCount = cnt;
            }
            else // includeTypes only
            {
                var (cnt, list) = CollectByKind(doc, isElementType: true,
                    filterCategory, filterElementType, filterFamilySymId: -1,
                    filterVisibleInView: false, bbMin, bbMax, pageSize, offset,
                    combineWith, invert, levelFilter: null, groupStatus: groupStatus,
                    wallConstraintStatus: wallConstraintStatus);
                elements.AddRange(list);
                totalCount = cnt;
            }

            // Cache result IDs for follow-up operations
            var ids = elements.Select(GetElementIdLong).ToArray();
            session.Store.Set("lastFilterResults", ids);

            var nextOffset = offset + ids.Length;
            var nextCursor = nextOffset < totalCount
                ? EncodeCursor(session.DocumentVersion, nextOffset)
                : null;
            var results = responseMode == "details"
                ? BuildElementInfoList(doc, elements)
                : null;

            return CortexResult<object>.Ok(new
            {
                message = $"Found {totalCount} element(s), returning {ids.Length} from offset {offset}",
                totalCount,
                returnedCount = ids.Length,
                appliedLimit = pageSize,
                responseMode,
                nextCursor,
                elementIds = responseMode == "idsonly" ? ids : null,
                elements = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Filter failed: {ex.Message}");
        }
    }

    // ── Collection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build and execute a FilteredElementCollector.
    /// Returns (totalMatching, taken) where 'taken' contains at most maxElements
    /// entries. A zero limit returns only the count, which lets a combined
    /// instance/type query avoid materializing a second collection when its page
    /// is already full. Avoids materializing the full result when a cap is set.
    /// </summary>
    private static (int total, List<Element> taken) CollectByKind(
        Document doc,
        bool isElementType,
        string? filterCategory,
        string? filterElementType,
        int filterFamilySymId,
        bool filterVisibleInView,
        XYZ? bbMin,
        XYZ? bbMax,
        int maxElements = 0,
        int offset = 0,
        string combineWith = "and",
        bool invert = false,
        JToken? levelFilter = null,
        string? groupStatus = null,
        string? wallConstraintStatus = null)
    {
        // Choose base collector (view-constrained or whole-model)
        FilteredElementCollector collector =
            (!isElementType && filterVisibleInView && doc.ActiveView != null)
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

        collector = isElementType
            ? collector.WhereElementIsElementType()
            : collector.WhereElementIsNotElementType();

        var filters = new List<ElementFilter>();

        // 1. Category filter
        if (!string.IsNullOrWhiteSpace(filterCategory))
        {
            var catId = CategoryResolver.ResolveToId(doc, filterCategory!);
            if (catId == null || catId == ElementId.InvalidElementId)
                throw new ArgumentException(
                    $"'{filterCategory}' is not a recognized category. Use OST_* codes (e.g. OST_Walls), English friendly names (Walls, Foundations), or the localized display name.");
            filters.Add(new ElementCategoryFilter(catId));
        }

        // 2. Element-class filter
        if (!string.IsNullOrWhiteSpace(filterElementType))
        {
            var resolvedType = ResolveRevitType(filterElementType!)
                ?? throw new ArgumentException(
                    $"Cannot resolve Revit API type '{filterElementType}'.");
            filters.Add(new ElementClassFilter(resolvedType));
        }

        // 3. Family-symbol filter (instances only)
        if (!isElementType && filterFamilySymId > 0)
        {
            var symId = new ElementId((long)filterFamilySymId);
            var symElem = doc.GetElement(symId);
            if (symElem is FamilySymbol symbol)
            {
                filters.Add(new FamilyInstanceFilter(doc, symId));
            }
            // If not a valid FamilySymbol, skip silently (matches fork behaviour)
        }

        // 4. Bounding-box spatial filter
        if (bbMin != null && bbMax != null)
        {
            var outline = new Outline(bbMin, bbMax);
            filters.Add(new BoundingBoxIntersectsFilter(outline));
        }

        // 5. Level filter (instances on a specific level, by id or name)
        if (!isElementType && levelFilter != null)
        {
            var lf = BuildLevelFilter(doc, levelFilter);
            if (lf != null) filters.Add(lf);
        }

        // Combine the individual filters with AND (default) or OR, then optionally
        // invert the whole thing (NOT) via ElementFilter's inverted constructor.
        ElementFilter? combined = null;
        if (filters.Count == 1)
            combined = filters[0];
        else if (filters.Count > 1)
            combined = combineWith == "or"
                ? new LogicalOrFilter(filters)
                : (ElementFilter)new LogicalAndFilter(filters);

        if (combined != null)
        {
            if (invert)
            {
                // LogicalAndFilter/LogicalOrFilter and the rule filters all support
                // an inverted form; wrap by negating each leaf is complex, so we use
                // the collector's WherePasses with an inverting ElementFilter where
                // available. ElementCategoryFilter etc. accept an 'inverted' arg, but
                // a combined filter does not — emulate NOT by excluding the matched ids.
                var matchedIds = new FilteredElementCollector(doc)
                    .WherePasses(combined);
                matchedIds = isElementType
                    ? matchedIds.WhereElementIsElementType()
                    : matchedIds.WhereElementIsNotElementType();
                var excludeIds = matchedIds.ToElementIds();
                if (excludeIds.Count > 0)
                    collector = collector.Excluding(excludeIds);
            }
            else
            {
                collector = collector.WherePasses(combined);
            }
        }

        if (!string.IsNullOrWhiteSpace(groupStatus) || !string.IsNullOrWhiteSpace(wallConstraintStatus))
        {
            var matching = collector.Cast<Element>()
                .Where(element => MatchesPostFilters(element, groupStatus, wallConstraintStatus))
                .ToList();
            return (matching.Count, matching.Skip(offset).Take(maxElements).ToList());
        }

        // GetElementCount walks the filtered set without wrapping each entry in
        // a managed Element — much cheaper than ToElements() when we only need
        // the count + a small prefix.
        int total = collector.GetElementCount();

        if (maxElements <= 0) return (total, new List<Element>());
        if (offset >= total) return (total, new List<Element>());
        var taken = new List<Element>(capacity: maxElements);
        var skipped = 0;
        foreach (var e in collector)
        {
            if (skipped++ < offset) continue;
            taken.Add(e);
            if (taken.Count >= maxElements) break;
        }
        return (total, taken);
    }

    private static bool MatchesPostFilters(Element element, string? groupStatus,
        string? wallConstraintStatus)
    {
        if (!string.IsNullOrWhiteSpace(groupStatus))
        {
            var grouped = element.GroupId != ElementId.InvalidElementId || element is Group;
            if (groupStatus.Equals("grouped", StringComparison.OrdinalIgnoreCase) && !grouped) return false;
            if (groupStatus.Equals("ungrouped", StringComparison.OrdinalIgnoreCase) && grouped) return false;
        }

        if (string.IsNullOrWhiteSpace(wallConstraintStatus)) return true;
        if (element is not Wall wall) return false;
        var topLevel = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();
        var levelConstrained = topLevel != null && topLevel != ElementId.InvalidElementId;
        var attached = wall.GetAttachmentIds(AttachmentLocation.Top).Count > 0 ||
                       wall.GetAttachmentIds(AttachmentLocation.Base).Count > 0;
        return wallConstraintStatus.ToLowerInvariant() switch
        {
            "level_constrained" => levelConstrained,
            "unconnected" => !levelConstrained,
            "attached" => attached,
            "unattached" => !attached,
            _ => true
        };
    }

    private static string EncodeCursor(long documentVersion, int offset)
        => PageCursor.Encode(documentVersion, offset);

    private static bool TryDecodeCursor(string? cursor, long documentVersion,
        out int offset, out string? error)
        => PageCursor.TryDecode(cursor, documentVersion, out offset, out error);

    // ── Element info builders ──────────────────────────────────────────────

    private static List<object> BuildElementInfoList(Document doc, IList<Element> elements)
    {
        var list = new List<object>();
        foreach (var elem in elements)
        {
            if (elem == null) continue;
            object? info = elem switch
            {
                ElementType et                                              => BuildTypeInfo(doc, et),
                Level or Grid                                              => BuildPositioningInfo(doc, elem),
                SpatialElement                                             => BuildSpatialInfo(doc, elem),
                View                                                       => BuildViewInfo(doc, elem),
                TextNote or Dimension or IndependentTag
                    or AnnotationSymbol or SpotDimension                   => BuildAnnotationInfo(doc, elem),
                Group or RevitLinkInstance                                 => BuildGroupOrLinkInfo(doc, elem),
                _ when elem.Category?.HasMaterialQuantities == true        => BuildInstanceInfo(doc, elem),
                _                                                          => BuildBasicInfo(doc, elem)
            };
            if (info != null) list.Add(info);
        }
        return list;
    }

    private static object? BuildInstanceInfo(Document doc, Element elem)
    {
        try
        {
            var typeElem = doc.GetElement(elem.GetTypeId());
            var bb = GetBoundingBox(elem);
            var roomId = elem is FamilyInstance fi ? GetElementIdLong(fi.Room) : (long?)-1;

            return new
            {
                elementId     = GetElementIdLong(elem),
                uniqueId      = elem.UniqueId,
                name          = elem.Name,
                familyName    = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                category      = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                typeId        = GetElementIdLong(typeElem),
                typeName      = typeElem?.Name,
                level         = GetLevelInfo(doc, elem),
                roomId,
                boundingBox   = bb,
                elementKind   = "instance"
            };
        }
        catch { return null; }
    }

    private static object? BuildTypeInfo(Document doc, ElementType et)
    {
        try
        {
            return new
            {
                elementId       = GetElementIdLong(et),
                uniqueId        = et.UniqueId,
                name            = et.Name,
                familyName      = et.FamilyName,
                category        = et.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(et),
                dimensionParams = GetDimensionParameters(et),
                elementKind     = "type"
            };
        }
        catch { return null; }
    }

    private static object? BuildPositioningInfo(Document doc, Element elem)
    {
        try
        {
            double? elevation = null;
            object? gridLine = null;

            if (elem is Level lvl)
                elevation = lvl.Elevation * MmPerFoot;
            else if (elem is Grid grid && grid.Curve != null)
            {
                var s = grid.Curve.GetEndPoint(0);
                var e = grid.Curve.GetEndPoint(1);
                gridLine = new
                {
                    start = new { x = s.X * MmPerFoot, y = s.Y * MmPerFoot, z = s.Z * MmPerFoot },
                    end   = new { x = e.X * MmPerFoot, y = e.Y * MmPerFoot, z = e.Z * MmPerFoot }
                };
            }

            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                elementClass    = elem.GetType().Name,
                elevation,
                gridLine,
                level           = GetLevelInfo(doc, elem),
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "positioning"
            };
        }
        catch { return null; }
    }

    private static object? BuildSpatialInfo(Document doc, Element elem)
    {
        try
        {
            string? number = null;
            double? volume = null;
            double? area   = null;
            double? perimeter = null;

            if (elem is Room room)
            {
                number = room.Number;
                volume = room.Volume * Math.Pow(MmPerFoot, 3);
            }
            else if (elem is Area ar)
                number = ar.Number;

            var areaParam = elem.get_Parameter(BuiltInParameter.ROOM_AREA);
            if (areaParam?.HasValue == true)
                area = areaParam.AsDouble() * Math.Pow(MmPerFoot, 2);

            var perimParam = elem.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            if (perimParam?.HasValue == true)
                perimeter = perimParam.AsDouble() * MmPerFoot;

            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                elementClass    = elem.GetType().Name,
                number,
                areaMm2         = area,
                perimeterMm     = perimeter,
                volumeMm3       = volume,
                level           = GetLevelInfo(doc, elem),
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "spatial"
            };
        }
        catch { return null; }
    }

    private static object? BuildViewInfo(Document doc, Element elem)
    {
        try
        {
            var view = (View)elem;

            object? assocLevel = null;
            if (view is ViewPlan vp && vp.GenLevel is Level level)
                assocLevel = new { elementId = GetElementIdLong(level), level.Name, elevationMm = level.Elevation * MmPerFoot };

            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                viewType        = view.ViewType.ToString(),
                scale           = view.Scale,
                isTemplate      = view.IsTemplate,
                detailLevel     = view.DetailLevel.ToString(),
                associatedLevel = assocLevel,
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "view"
            };
        }
        catch { return null; }
    }

    private static object? BuildAnnotationInfo(Document doc, Element elem)
    {
        try
        {
            string? ownerView = null;
            if (elem.OwnerViewId != ElementId.InvalidElementId)
                ownerView = (doc.GetElement(elem.OwnerViewId) as View)?.Name;

            object? position = null;
            string? textContent = null;
            string? dimensionValue = null;

            if (elem is TextNote tn)
            {
                textContent = tn.Text;
                position = XyzToMm(tn.Coord);
            }
            else if (elem is Dimension dim)
            {
                dimensionValue = dim.Value?.ToString();
                position = XyzToMm(dim.Origin);
            }
            else if (elem is AnnotationSymbol annSym && annSym.Location is LocationPoint lp)
                position = XyzToMm(lp.Point);

            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                elementClass    = elem.GetType().Name,
                ownerView,
                position,
                textContent,
                dimensionValue,
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "annotation"
            };
        }
        catch { return null; }
    }

    private static object? BuildGroupOrLinkInfo(Document doc, Element elem)
    {
        try
        {
            int? memberCount = null;
            string? groupType = null;
            string? linkPath = null;
            string? linkStatus = null;
            object? position = null;

            if (elem is Group grp)
            {
                memberCount = grp.GetMemberIds()?.Count;
                groupType   = grp.GroupType?.Name;
            }
            else if (elem is RevitLinkInstance link)
            {
                var lt = doc.GetElement(link.GetTypeId()) as RevitLinkType;
                if (lt != null)
                {
                    linkPath   = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                                     lt.GetExternalFileReference().GetAbsolutePath());
                    linkStatus = lt.GetLinkedFileStatus().ToString();
                }
                else
                    linkStatus = LinkedFileStatus.Invalid.ToString();

                if (link.Location is LocationPoint llp)
                    position = XyzToMm(llp.Point);
            }

            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                elementClass    = elem.GetType().Name,
                memberCount,
                groupType,
                linkPath,
                linkStatus,
                position,
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "groupOrLink"
            };
        }
        catch { return null; }
    }

    private static object? BuildBasicInfo(Document doc, Element elem)
    {
        try
        {
            return new
            {
                elementId       = GetElementIdLong(elem),
                uniqueId        = elem.UniqueId,
                name            = elem.Name,
                category        = elem.Category?.Name,
                builtInCategory = GetBuiltInCategoryName(elem),
                boundingBox     = GetBoundingBox(elem),
                elementKind     = "basic"
            };
        }
        catch { return null; }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static long GetElementIdLong(Element? elem)
    {
        if (elem == null) return -1;
        return elem.Id.Value;
    }

    private static string? GetBuiltInCategoryName(Element elem)
    {
        if (elem.Category == null) return null;
        return Enum.GetName(typeof(BuiltInCategory), (BuiltInCategory)(int)elem.Category.Id.Value);
    }

    private static object? GetLevelInfo(Document doc, Element elem)
    {
        var levelIdParam = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                        ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                        ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

        if (levelIdParam?.HasValue != true) return null;

        var levelId = levelIdParam.AsElementId();
        if (levelId == ElementId.InvalidElementId) return null;

        var level = doc.GetElement(levelId) as Level;
        if (level == null) return null;

        return new { elementId = GetElementIdLong(level), level.Name, elevationMm = level.Elevation * MmPerFoot };
    }

    private static object? GetBoundingBox(Element elem)
    {
        // Perf: get_BoundingBox(null) forces a global geometry regeneration,
        // which on large models costs 150-500ms per element. Passing the
        // active view reuses cached view geometry when possible. If the
        // active view yields no bbox (element not visible in it), we fall
        // back to the global bbox — correct at the cost of the old path.
        try
        {
            BoundingBoxXYZ? bb = null;
            try
            {
                var activeView = elem.Document?.ActiveView;
                if (activeView != null)
                    bb = elem.get_BoundingBox(activeView);
            }
            catch
            {
                // ActiveView may not be queryable in some contexts (e.g.
                // browser views, sheets) — fall through to null lookup.
                bb = null;
            }

            if (bb == null)
                bb = elem.get_BoundingBox(null);

            if (bb == null) return null;
            return new
            {
                min = new { x = bb.Min.X * MmPerFoot, y = bb.Min.Y * MmPerFoot, z = bb.Min.Z * MmPerFoot },
                max = new { x = bb.Max.X * MmPerFoot, y = bb.Max.Y * MmPerFoot, z = bb.Max.Z * MmPerFoot }
            };
        }
        catch { return null; }
    }

    private static object XyzToMm(XYZ p)
        => new { x = p.X * MmPerFoot, y = p.Y * MmPerFoot, z = p.Z * MmPerFoot };

    /// <summary>
    /// Returns dimensional parameters (doubles only) for type elements, matching
    /// the fork's GetDimensionParameters helper.
    /// </summary>
    private static List<object> GetDimensionParameters(Element elem)
    {
        var list = new List<object>();
        foreach (Parameter p in elem.Parameters)
        {
            if (p.StorageType != StorageType.Double || !p.HasValue) continue;
            // H30: a flat *MmPerFoot multiplier is only correct for LENGTH parameters.
            // Area params are stored in ft^2 (×MmPerFoot^2) and volume in ft^3 (×MmPerFoot^3),
            // so the old `valueMm` was 304x too small for area and ~92903x for volume.
            // AsValueString() applies the parameter's own unit/format, which is correct
            // for every dimension type; we also expose the raw internal value (feet-based)
            // for callers that want to convert themselves.
            list.Add(new
            {
                name  = p.Definition?.Name ?? "Unknown",
                value = p.AsValueString(),
                valueInternal = p.AsDouble(),
                isReadOnly = p.IsReadOnly
            });
        }
        return list;
    }

    /// <summary>
    /// Parses a JSON token representing a {x, y, z} point in mm into Revit's
    /// internal foot-based XYZ. Returns null if the token is null/missing.
    /// </summary>
    private static XYZ? ParseXYZ(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }

    /// <summary>
    /// Attempts to resolve a short or fully-qualified Revit API type name.
    /// Mirrors the fork's multi-attempt approach.
    /// </summary>
    private static Type? ResolveRevitType(string typeName)
    {
        string[] candidates =
        [
            typeName,
            $"Autodesk.Revit.DB.{typeName}, RevitAPI",
            $"{typeName}, RevitAPI"
        ];
        foreach (var candidate in candidates)
        {
            var t = Type.GetType(candidate);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>
    /// Builds an ElementLevelFilter from {levelId}, {levelName}, a raw id, or a raw
    /// level name. Returns null when the level cannot be resolved.
    /// </summary>
    private static ElementFilter? BuildLevelFilter(Document doc, JToken levelFilter)
    {
        Level? level = null;

        if (levelFilter is JObject levelObject)
        {
            var levelIdLong = levelObject["levelId"]?.Value<long?>();
            if (levelIdLong.HasValue && levelIdLong.Value > 0)
                level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong.Value)) as Level;

            var levelName = levelObject["levelName"]?.Value<string>();
            if (level == null && !string.IsNullOrEmpty(levelName))
                level = ResolveLevelByName(doc, levelName!);
        }
        else if (levelFilter.Type == JTokenType.Integer)
        {
            var levelIdLong = levelFilter.Value<long>();
            if (levelIdLong > 0)
                level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong)) as Level;
        }
        else
        {
            var rawLevel = levelFilter.Value<string>();
            if (long.TryParse(rawLevel, out var levelIdLong) && levelIdLong > 0)
                level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong)) as Level;
            if (level == null && !string.IsNullOrWhiteSpace(rawLevel))
                level = ResolveLevelByName(doc, rawLevel!);
        }

        return level != null ? new ElementLevelFilter(level.Id) : null;
    }

    private static Level? ResolveLevelByName(Document doc, string levelName)
    {
        return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
    }
}

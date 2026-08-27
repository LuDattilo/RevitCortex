using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Returns elements visible in the current active view, with optional category
/// and field filtering. Mirrors the fork's GetCurrentViewElementsEventHandler logic.
/// </summary>
[ToolSafety(true, false)]
public class GetCurrentViewElementsTool : ICortexTool
{
    public string Name => "get_current_view_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns elements visible in the current active view, with optional category and field filtering. Pages via pageSize/cursor: nextCursor in the response, passed back as cursor, reaches elements beyond the first page. totalElementsInView counts the whole view regardless of category filter; filteredElementCount is what the requested categories matched.";

    // Default model categories when none specified
    private static readonly IReadOnlyList<string> DefaultModelCategories = new[]
    {
        "OST_Walls",
        "OST_Doors",
        "OST_Windows",
        "OST_Furniture",
        "OST_Columns",
        "OST_Floors",
        "OST_Roofs",
        "OST_Stairs",
        "OST_StructuralFraming",
        "OST_Ceilings",
        "OST_MEPSpaces",
        "OST_Rooms"
    };

    // Default annotation categories when none specified
    private static readonly IReadOnlyList<string> DefaultAnnotationCategories = new[]
    {
        "OST_Dimensions",
        "OST_TextNotes",
        "OST_GenericAnnotation",
        "OST_WallTags",
        "OST_DoorTags",
        "OST_WindowTags",
        "OST_RoomTags",
        "OST_AreaTags",
        "OST_SpaceTags",
        "OST_ViewportLabels",
        "OST_TitleBlocks"
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var activeView = doc.ActiveView;
        if (activeView == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active view in the document");

        // ── Parse inputs ───────────────────────────────────────────────────
        var modelCategoryTokens      = input["modelCategoryList"]?.ToObject<string[]>();
        var annotationCategoryTokens = input["annotationCategoryList"]?.ToObject<string[]>();
        // categoryFilter is a single-category shortcut published by the MCP schema;
        // it used to be dropped on the floor here.
        var categoryFilterToken      = input["categoryFilter"]?.Value<string>();
        var includeHidden            = input["includeHidden"]?.Value<bool>() ?? false;
        // pageSize is the name every other paging tool (filter_elements) uses;
        // limit is kept as an alias so existing callers keep working.
        var pageSize                 = Math.Clamp(input["pageSize"]?.Value<int>()
                                                    ?? input["limit"]?.Value<int>() ?? 200, 1, 2000);
        var fields                   = input["fields"]?.ToObject<string[]>();

        if (!PageCursor.TryDecode(input["cursor"]?.Value<string>(), session.DocumentVersion,
                out var offset, out var cursorError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cursorError!,
                suggestion: "Restart the read without a cursor because the document changed.");

        // Categories the caller actually asked for — distinct from the built-in
        // defaults used when none are given. A default that happens not to exist
        // in this document (e.g. OST_SpaceTags on a template without spaces) is
        // not something the caller requested and must not show up in
        // unresolvedCategories — see P2.2 in PLAN_CORRECTION.md.
        var categoriesWereRequested = modelCategoryTokens != null || annotationCategoryTokens != null ||
                                       !string.IsNullOrWhiteSpace(categoryFilterToken);
        IEnumerable<string> allCategories = categoriesWereRequested
            ? (modelCategoryTokens ?? Array.Empty<string>())
                .Concat(annotationCategoryTokens ?? Array.Empty<string>())
                .Concat(string.IsNullOrWhiteSpace(categoryFilterToken)
                    ? Array.Empty<string>()
                    : new[] { categoryFilterToken! })
            : DefaultModelCategories.Concat(DefaultAnnotationCategories);

        try
        {
            // ── Build collector scoped to current view ─────────────────────
            var collector = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType();

            // Resolve category tokens the same way as every other tool: OST_ code,
            // English friendly name, or localized display name. Enum.TryParse alone
            // silently dropped "Walls" and "Murs", widening the query to everything.
            var categoryIds = new List<ElementId>();
            var unresolvedCategories = new List<string>();
            foreach (var catName in allCategories)
            {
                var categoryId = CategoryResolver.ResolveToId(doc, catName);
                if (categoryId != null && categoryId != ElementId.InvalidElementId)
                    categoryIds.Add(categoryId);
                else if (categoriesWereRequested)
                    unresolvedCategories.Add(catName);
            }

            if (categoriesWereRequested && unresolvedCategories.Count > 0 && categoryIds.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"No requested category could be resolved: {string.Join(", ", unresolvedCategories)}",
                    suggestion: "Use OST_* codes (OST_Walls), English names (Walls) or the exact localized label.");

            IList<Element> elements;
            int totalElementsInView;
            if (categoryIds.Count > 0)
            {
                var categoryFilter = new ElementMulticategoryFilter(categoryIds);
                elements = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(categoryFilter)
                    .ToElements();
                totalElementsInView = new FilteredElementCollector(doc, activeView.Id)
                    .GetElementCount();
            }
            else
            {
                // Without a category filter the collector result IS the view total —
                // don't run a second view-scoped collector just to count it.
                elements = collector.ToElements();
                totalElementsInView = elements.Count;
            }

            // Note: view-scoped collectors already exclude elements hidden in the view
            // (per-element hide, category visibility, filters), so the previous
            // per-element IsHidden() pass — one Revit interop call per element — was
            // redundant. includeHidden is still accepted for schema compatibility.

            int filteredCount = elements.Count;

            // Page through the filtered result with the same opaque-cursor
            // contract as filter_elements, instead of a limit/truncated pair
            // with no way to reach what came after — see P2.2 in
            // PLAN_CORRECTION.md.
            var page = offset < elements.Count
                ? elements.Skip(offset).Take(pageSize).ToList()
                : new List<Element>();
            var nextOffset = offset + page.Count;
            var nextCursor = nextOffset < filteredCount
                ? PageCursor.Encode(session.DocumentVersion, nextOffset)
                : null;

            // ── Build result list ──────────────────────────────────────────
            var fieldSet = (fields != null && fields.Length > 0)
                ? new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase)
                : null;

            var elementInfos = page.Select(e => BuildElementInfo(doc, e, activeView, fieldSet)).ToList();

            return CortexResult<object>.Ok(new
            {
                viewId               = activeView.Id.Value,
                viewName             = activeView.Name,
                // Everything in the view, ignoring category filtering — NOT the
                // count this call's category filter matched. Use
                // filteredElementCount for that.
                totalElementsInView,
                // How many elements matched the requested category filter
                // (before paging). This is the count "elements" was drawn from.
                filteredElementCount = filteredCount,
                returnedCount = page.Count,
                pageSize,
                nextCursor,
                truncated = nextCursor != null,
                unresolvedCategories,
                elements             = elementInfos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get current view elements: {ex.Message}");
        }
    }

    // ── Element info builder ───────────────────────────────────────────────

    private static object BuildElementInfo(
        Document doc,
        Element element,
        View activeView,
        HashSet<string>? fieldSet)
    {
        var typeElem = doc.GetElement(element.GetTypeId());

        return new
        {
            id           = element.Id.Value,
            uniqueId     = element.UniqueId,
            name         = element.Name,
            category     = element.Category?.Name ?? "unknown",
            typeName     = typeElem?.Name,
            familyName   = GetFamilyName(element, typeElem),
            location     = GetLocation(element),
            properties   = GetProperties(element, fieldSet)
        };
    }

    private static string? GetFamilyName(Element element, Element? typeElem)
    {
        // Try built-in family param first, fall back to FamilySymbol.FamilyName
        var param = element.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
        if (param?.HasValue == true)
            return param.AsValueString();

        if (typeElem is FamilySymbol fs)
            return fs.FamilyName;

        return null;
    }

    private static object? GetLocation(Element element)
    {
        if (element.Location is LocationPoint lp)
        {
            var pt = lp.Point;
            return new
            {
                type = "point",
                x    = pt.X * MmPerFoot,
                y    = pt.Y * MmPerFoot,
                z    = pt.Z * MmPerFoot
            };
        }

        if (element.Location is LocationCurve lc)
        {
            var curve = lc.Curve;
            var start = curve.GetEndPoint(0);
            var end   = curve.GetEndPoint(1);
            return new
            {
                type   = "curve",
                startX = start.X * MmPerFoot,
                startY = start.Y * MmPerFoot,
                startZ = start.Z * MmPerFoot,
                endX   = end.X * MmPerFoot,
                endY   = end.Y * MmPerFoot,
                endZ   = end.Z * MmPerFoot,
                lengthMm = curve.Length * MmPerFoot
            };
        }

        return null;
    }

    private static Dictionary<string, string> GetProperties(Element element, HashSet<string>? fieldSet)
    {
        var props    = new Dictionary<string, string>();
        bool filtered = fieldSet != null;

        // ElementId
        if (!filtered || fieldSet!.Contains("ElementId"))
        {
            props["ElementId"] = element.Id.Value.ToString();
        }

        // Location coordinates (LocationPoint)
        if (element.Location is LocationPoint lp2)
        {
            var pt = lp2.Point;
            if (!filtered || fieldSet!.Contains("LocationX")) props["LocationX"] = (pt.X * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("LocationY")) props["LocationY"] = (pt.Y * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("LocationZ")) props["LocationZ"] = (pt.Z * MmPerFoot).ToString("F2");
        }
        else if (element.Location is LocationCurve lc2)
        {
            var curve  = lc2.Curve;
            var start2 = curve.GetEndPoint(0);
            var end2   = curve.GetEndPoint(1);
            if (!filtered || fieldSet!.Contains("StartX")) props["StartX"] = (start2.X * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("StartY")) props["StartY"] = (start2.Y * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("StartZ")) props["StartZ"] = (start2.Z * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("EndX"))   props["EndX"]   = (end2.X * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("EndY"))   props["EndY"]   = (end2.Y * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("EndZ"))   props["EndZ"]   = (end2.Z * MmPerFoot).ToString("F2");
            if (!filtered || fieldSet!.Contains("Length")) props["Length"]  = (curve.Length * MmPerFoot).ToString("F2");
        }

        // Common named parameters
        var commonParams = new[] { "Comments", "Mark", "Level", "Family", "Type" };
        foreach (var paramName in commonParams)
        {
            if (filtered && !fieldSet!.Contains(paramName))
                continue;

            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) continue;

            string? val = param.StorageType switch
            {
                StorageType.String    => param.AsString() ?? string.Empty,
                StorageType.Double    => param.AsDouble().ToString("F2"),
                StorageType.Integer   => param.AsInteger().ToString(),
                StorageType.ElementId => param.AsElementId().Value.ToString(),
                _                     => null
            };

            if (val != null)
                props[paramName] = val;
        }

        return props;
    }
}

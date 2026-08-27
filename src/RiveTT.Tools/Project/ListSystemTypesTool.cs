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

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists the system types of a category — wall, floor, ceiling, roof, railing,
/// stair, ramp, viewport, text, dimension, sheet…
///
/// Why this exists: system types are not loadable families, so an agent had no way
/// to discover them. Finding a railing type required reading a parameter off an
/// existing railing instance, and if none existed the type was simply unreachable:
/// a balcony ended up with no guardrail because create_railing needs a
/// railingTypeId nobody could enumerate.
/// </summary>
[ToolSafety(true, false)]
public sealed class ListSystemTypesTool : ICortexTool
{
    public string Name => "list_system_types";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Lists system types (non-loadable ElementTypes) of a category: walls, floors, ceilings, roofs, " +
        "railings, stairs, ramps, viewports, text, dimensions, sheets. Accepts OST_* codes, English names " +
        "or localized labels; omit the category to get the per-category inventory. " +
        "Use the returned typeId with create_wall / create_railing / duplicate_system_type.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var category = input["category"]?.Value<string>();
        var nameFilter = input["nameFilter"]?.Value<string>();
        var includeLoadable = input["includeLoadable"]?.Value<bool>() ?? false;
        var limit = input["limit"]?.Value<int>() ?? 200;
        if (limit <= 0) limit = 200;

        try
        {
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(type => includeLoadable || type is not FamilySymbol)
                .ToList();

            if (!string.IsNullOrWhiteSpace(category))
            {
                var categoryId = CategoryResolver.ResolveToId(doc, category!);
                if (categoryId == null || categoryId == ElementId.InvalidElementId)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Category '{category}' could not be resolved in this document.",
                        suggestion: "Use an OST_* code (OST_StairsRailing, OST_Walls…), the English name, " +
                                    "or the exact localized label. Call this tool without a category to see " +
                                    "which categories actually carry system types.");

                types = types.Where(type => type.Category != null && type.Category.Id == categoryId).ToList();
            }

            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                var needle = ParameterNameResolver.Normalize(nameFilter!);
                types = types.Where(type =>
                        ParameterNameResolver.Normalize(type.Name).Contains(needle, StringComparison.Ordinal) ||
                        ParameterNameResolver.Normalize(FamilyNameOf(type)).Contains(needle, StringComparison.Ordinal))
                    .ToList();
            }

            // No category given: return the inventory so the caller can pick one
            // without guessing localized labels.
            if (string.IsNullOrWhiteSpace(category))
            {
                var inventory = types
                    .Where(type => type.Category != null)
                    .GroupBy(type => new
                    {
                        Name = type.Category!.Name,
                        Bic = CategoryResolver.DescribeBuiltInCategory(type.Category)
                    })
                    .Select(group => new
                    {
                        category = group.Key.Name,
                        categoryBic = group.Key.Bic,
                        typeCount = group.Count()
                    })
                    .OrderByDescending(entry => entry.typeCount)
                    .ToList();

                return CortexResult<object>.Ok(new
                {
                    message = $"{types.Count} system type(s) across {inventory.Count} categories. " +
                              "Pass a category (categoryBic is language-independent) to list its types.",
                    totalTypeCount = types.Count,
                    categories = inventory
                });
            }

            var instanceCounts = CountInstancesByType(doc, types);
            var items = types
                .OrderBy(FamilyNameOf, StringComparer.OrdinalIgnoreCase)
                .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(type => new
                {
                    typeId = ToolHelpers.GetElementIdValue(type.Id),
                    typeName = type.Name,
                    familyName = FamilyNameOf(type),
                    className = type.GetType().Name,
                    kind = type is FamilySymbol ? "loadable" : "system",
                    category = type.Category?.Name,
                    categoryBic = CategoryResolver.DescribeBuiltInCategory(type.Category),
                    instanceCount = instanceCounts.TryGetValue(type.Id, out var count) ? count : 0,
                    thicknessMm = ThicknessMm(type)
                })
                .ToList();

            return CortexResult<object>.Ok(new
            {
                message = $"{items.Count} type(s) returned of {types.Count} matching '{category}'.",
                count = items.Count,
                totalCount = types.Count,
                truncated = types.Count > items.Count,
                items
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to list system types: {exception.Message}");
        }
    }

    private static string FamilyNameOf(ElementType type)
    {
        if (type is FamilySymbol symbol) return symbol.FamilyName;
        return type.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString()
               ?? type.FamilyName
               ?? type.GetType().Name;
    }

    private static double? ThicknessMm(ElementType type)
    {
        var parameter = type.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM)
                        ?? type.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
        if (parameter == null || !parameter.HasValue) return null;
        return Math.Round(parameter.AsDouble() * MmPerFoot, 1);
    }

    /// <summary>
    /// One collector pass, grouped by type id — cheaper than a per-type collector
    /// and it tells the caller which types are actually in use in the model.
    /// </summary>
    private static Dictionary<ElementId, int> CountInstancesByType(Document doc, List<ElementType> types)
    {
        var wanted = new HashSet<ElementId>(types.Select(type => type.Id));
        var counts = new Dictionary<ElementId, int>();
        if (wanted.Count == 0) return counts;

        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId || !wanted.Contains(typeId)) continue;
            counts[typeId] = counts.TryGetValue(typeId, out var current) ? current + 1 : 1;
        }

        return counts;
    }
}

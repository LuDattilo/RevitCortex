using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists available family types (loadable and system) with optional category/name filtering.
/// </summary>
[ToolSafety(true, false)]
public class GetAvailableFamilyTypesTool : ICortexTool
{
    public string Name => "list_family_types";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists available family types (loadable and system) with optional category/name filtering.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var categoryList     = input["categoryList"]?.ToObject<List<string>>() ?? new List<string>();
        var familyNameFilter = input["familyNameFilter"]?.Value<string>() ?? "";
        var limit            = input["limit"]?.Value<int>() ?? 100;

        try
        {
            // One pass over every ElementType in the document. The previous version
            // enumerated FamilySymbol plus a hardcoded list of five system classes
            // (wall/floor/roof/ceiling/curtain system), so railing, stair, ramp,
            // viewport, text and title-block types were invisible — searching for a
            // railing type returned nothing and looked like "no railings exist".
            IEnumerable<ElementType> allElements = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>();

            // Category filter
            if (categoryList.Count > 0)
            {
                var validCatIds = new List<long>();
                var unresolvedCategories = new List<string>();
                foreach (var catName in categoryList)
                {
                    var catId = CategoryResolver.ResolveToId(doc, catName);
                    if (catId != null && catId != ElementId.InvalidElementId)
                    {
                        validCatIds.Add(catId.Value);
                    }
                    else
                    {
                        unresolvedCategories.Add(catName);
                    }
                }

                // A category that fails to resolve must never silently widen the
                // result set: skipping it (or dropping the whole filter at zero
                // matches) used to return the entire model's types as Ok.
                if (unresolvedCategories.Count > 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"These categories could not be resolved in this document: {string.Join(", ", unresolvedCategories)}",
                        suggestion: "Use OST_* BuiltInCategory codes (e.g. OST_Doors, OST_StairsRailing) or the exact localized display name; the category must exist in the document.");

                allElements = allElements.Where(et =>
                {
                    if (et.Category == null) return false;
                    return validCatIds.Contains(et.Category.Id.Value);
                });
            }

            // Name filter
            if (!string.IsNullOrEmpty(familyNameFilter))
            {
                allElements = allElements.Where(et =>
                {
                    var famName = et is FamilySymbol fs
                        ? fs.FamilyName
                        : et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString() ?? "";

                    return (famName?.IndexOf(familyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (et.Name.IndexOf(familyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                });
            }

            var matched = allElements.ToList();
            var result = matched.Take(limit).Select(et =>
            {
                var familyName = et is FamilySymbol fs
                    ? fs.FamilyName
                    : et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString()
                      ?? et.GetType().Name.Replace("Type", "");

                return new
                {
                    familyTypeId = et.Id.Value,
                    uniqueId   = et.UniqueId,
                    familyName,
                    typeName   = et.Name,
                    category   = et.Category?.Name,
                    categoryBic = CategoryResolver.DescribeBuiltInCategory(et.Category),
                    // System types cannot be duplicated with duplicate_family_type;
                    // they need duplicate_system_type. Say which is which.
                    kind = et is FamilySymbol ? "loadable" : "system",
                    className = et.GetType().Name
                };
            }).ToList();

            // A bare array response cannot carry counters, and the client-side
            // compact shaper silently produced count:0 from it. The object form
            // keeps totals truthful and makes truncation visible.
            return CortexResult<object>.Ok(new
            {
                count = result.Count,
                totalCount = matched.Count,
                truncated = matched.Count > result.Count,
                limit,
                items = result
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get available family types: {ex.Message}");
        }
    }
}

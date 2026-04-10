using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists available family types (loadable and system) with optional category/name filtering.
/// </summary>
public class GetAvailableFamilyTypesTool : ICortexTool
{
    public string Name => "get_available_family_types";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

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
            // Loadable families
            var familySymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Cast<ElementType>();

            // System family types
            var systemTypes = new List<ElementType>();
            systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<ElementType>());
            systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<ElementType>());
            systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<ElementType>());
            systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<ElementType>());
            systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(CurtainSystemType)).Cast<ElementType>());

            IEnumerable<ElementType> allElements = familySymbols.Concat(systemTypes);

            // Category filter
            if (categoryList.Count > 0)
            {
                var validCatIds = new List<long>();
                foreach (var catName in categoryList)
                {
                    string bicName = catName.StartsWith("OST_") ? catName : "OST_" + catName;
                    if (Enum.TryParse<BuiltInCategory>(bicName, true, out var bic))
                        validCatIds.Add((long)(int)bic);
                }

                if (validCatIds.Count > 0)
                {
                    allElements = allElements.Where(et =>
                    {
                        if (et.Category == null) return false;
#if REVIT2024_OR_GREATER
                        return validCatIds.Contains(et.Category.Id.Value);
#else
                        return validCatIds.Contains((long)et.Category.Id.IntegerValue);
#endif
                    });
                }
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

            var result = allElements.Take(limit).Select(et =>
            {
                var familyName = et is FamilySymbol fs
                    ? fs.FamilyName
                    : et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString()
                      ?? et.GetType().Name.Replace("Type", "");

                return new
                {
#if REVIT2024_OR_GREATER
                    familyTypeId = et.Id.Value,
#else
                    familyTypeId = (long)et.Id.IntegerValue,
#endif
                    uniqueId   = et.UniqueId,
                    familyName,
                    typeName   = et.Name,
                    category   = et.Category?.Name
                };
            }).ToList();

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get available family types: {ex.Message}");
        }
    }
}

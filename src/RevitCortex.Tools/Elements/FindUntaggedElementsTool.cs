using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// QA/audit tool — finds elements in specified categories that have no tag in the target view.
/// Mirrors the fork's FindUntaggedElementsEventHandler logic.
/// </summary>
public class FindUntaggedElementsTool : ICortexTool
{
    public string Name => "find_untagged_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "QA/audit tool — finds elements in specified categories that have no tag in the target view. Mirrors the fork's FindUntaggedElementsEventHandler logic.";
    private static readonly List<BuiltInCategory> DefaultCategories = new List<BuiltInCategory>
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralFraming
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Parse inputs ───────────────────────────────────────────────────
        var categoriesToken = input["categories"] as JArray;
        var viewIdToken = input["viewId"]?.Value<long?>();
        var limit = input["limit"]?.Value<int>() ?? 500;

        try
        {
            // ── Determine target view ──────────────────────────────────────
            View targetView;
            if (viewIdToken.HasValue)
            {
#if REVIT2024_OR_GREATER
                var viewElement = doc.GetElement(new ElementId(viewIdToken.Value));
#else
                var viewElement = doc.GetElement(new ElementId((int)viewIdToken.Value));
#endif
                targetView = viewElement as View
                    ?? throw new ArgumentException($"Element {viewIdToken.Value} is not a view");
            }
            else
            {
                targetView = doc.ActiveView;
            }

            if (targetView == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No active view available and no viewId was provided");

            // ── Parse categories ───────────────────────────────────────────
            List<BuiltInCategory> builtInCategories;
            if (categoriesToken != null && categoriesToken.Count > 0)
            {
                builtInCategories = new List<BuiltInCategory>();
                foreach (var catToken in categoriesToken)
                {
                    var catStr = catToken.ToString();
                    if (Enum.TryParse(catStr, out BuiltInCategory bic))
                        builtInCategories.Add(bic);
                }
                if (builtInCategories.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "None of the provided categories could be parsed as valid BuiltInCategory OST_* codes");
            }
            else
            {
                builtInCategories = DefaultCategories;
            }

            // ── Collect all IndependentTags in the view ────────────────────
            var taggedElementIds = new HashSet<long>();

            var tagCollector = new FilteredElementCollector(doc, targetView.Id)
                .OfClass(typeof(IndependentTag))
                .WhereElementIsNotElementType();

            foreach (IndependentTag tag in tagCollector)
            {
                foreach (var id in tag.GetTaggedLocalElementIds())
                {
                    if (id != ElementId.InvalidElementId)
                    {
#if REVIT2024_OR_GREATER
                        taggedElementIds.Add(id.Value);
#else
                        taggedElementIds.Add(id.IntegerValue);
#endif
                    }
                }
            }

            // ── Collect SpatialElementTags (room, space, area tags) ────────
            var spatialTagCollector = new FilteredElementCollector(doc, targetView.Id)
                .OfClass(typeof(SpatialElementTag))
                .WhereElementIsNotElementType();

            foreach (SpatialElementTag spatialTag in spatialTagCollector)
            {
                Element? taggedElement = null;
                if (spatialTag is RoomTag roomTag)
                    taggedElement = roomTag.Room;
                else if (spatialTag is Autodesk.Revit.DB.Mechanical.SpaceTag spaceTag)
                    taggedElement = spaceTag.Space;
                else if (spatialTag is AreaTag areaTag)
                    taggedElement = areaTag.Area;

                if (taggedElement != null)
                {
#if REVIT2024_OR_GREATER
                    taggedElementIds.Add(taggedElement.Id.Value);
#else
                    taggedElementIds.Add(taggedElement.Id.IntegerValue);
#endif
                }
            }

            // ── Find untagged elements in target categories ────────────────
            // Collect one extra beyond limit to detect truncation (mirrors fork pattern)
            var allUntagged = new List<object>();
            int totalChecked = 0;

            foreach (var category in builtInCategories)
            {
                var elementCollector = new FilteredElementCollector(doc, targetView.Id)
                    .OfCategory(category)
                    .WhereElementIsNotElementType();

                foreach (var element in elementCollector)
                {
                    totalChecked++;
#if REVIT2024_OR_GREATER
                    long elementIdValue = element.Id.Value;
#else
                    long elementIdValue = element.Id.IntegerValue;
#endif
                    if (!taggedElementIds.Contains(elementIdValue))
                    {
                        // Collect up to limit+1 so we can detect truncation
                        if (allUntagged.Count <= limit)
                        {
                            allUntagged.Add(new
                            {
                                elementId = elementIdValue,
                                name = element.Name,
                                category = element.Category?.Name ?? "Unknown"
                            });
                        }
                    }
                }
            }

            bool isTruncated = allUntagged.Count > limit;
            var returnedElements = isTruncated ? allUntagged.Take(limit).ToList() : allUntagged;

            return CortexResult<object>.Ok(new
            {
#if REVIT2024_OR_GREATER
                viewId = targetView.Id.Value,
#else
                viewId = (long)targetView.Id.IntegerValue,
#endif
                viewName = targetView.Name,
                totalElementsChecked = totalChecked,
                untaggedCount = returnedElements.Count,
                truncated = isTruncated,
                untaggedElements = returnedElements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to find untagged elements: {ex.Message}");
        }
    }
}

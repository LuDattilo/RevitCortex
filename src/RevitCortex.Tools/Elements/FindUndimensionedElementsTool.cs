using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// QA/audit tool — finds elements in specified categories that have no dimension reference in the target view.
/// Mirrors the fork's FindUndimensionedElementsEventHandler logic.
/// </summary>
public class FindUndimensionedElementsTool : ICortexTool
{
    public string Name => "find_undimensioned_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "QA/audit tool — finds elements in specified categories that have no dimension reference in the target view. Mirrors the fork's FindUndimensionedElementsEventHandler logic.";
    private static readonly List<BuiltInCategory> DefaultCategories = new List<BuiltInCategory>
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_Grids,
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
        // Singular "category" alias: the server wrapper exposed it long before the
        // plural form, and direct-bridge callers still use it.
        if (categoriesToken == null && input["category"]?.Type == JTokenType.String)
            categoriesToken = new JArray(input["category"]!.ToString());
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
                    var bic = Utilities.CategoryResolver.Resolve(catStr);
                    if (bic != null)
                        builtInCategories.Add(bic.Value);
                }
                if (builtInCategories.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "None of the provided categories could be resolved",
                        suggestion: "Use OST_* codes like OST_Walls, or English friendly names like Walls, Doors");
            }
            else
            {
                builtInCategories = DefaultCategories;
            }

            // ── Collect all dimensions in the view and extract referenced IDs ──
            var dimensionedElementIds = new HashSet<long>();

            var dimensionCollector = new FilteredElementCollector(doc, targetView.Id)
                .OfClass(typeof(Dimension))
                .WhereElementIsNotElementType();

            foreach (Dimension dim in dimensionCollector)
            {
                var refs = dim.References;
                if (refs == null) continue;

                foreach (Reference reference in refs)
                {
                    var refElementId = reference.ElementId;
                    if (refElementId != ElementId.InvalidElementId)
                    {
#if REVIT2024_OR_GREATER
                        dimensionedElementIds.Add(refElementId.Value);
#else
                        dimensionedElementIds.Add(refElementId.IntegerValue);
#endif
                    }
                }
            }

            // ── Find undimensioned elements in target categories ───────────
            // Collect up to limit+1 to detect truncation (mirrors fork pattern)
            var allUndimensioned = new List<object>();
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
                    if (!dimensionedElementIds.Contains(elementIdValue))
                    {
                        // Collect up to limit+1 so we can detect truncation
                        if (allUndimensioned.Count <= limit)
                        {
                            allUndimensioned.Add(new
                            {
                                elementId = elementIdValue,
                                name = element.Name,
                                category = element.Category?.Name ?? "Unknown"
                            });
                        }
                    }
                }
            }

            bool isTruncated = allUndimensioned.Count > limit;
            var returnedElements = isTruncated ? allUndimensioned.Take(limit).ToList() : allUndimensioned;

            return CortexResult<object>.Ok(new
            {
#if REVIT2024_OR_GREATER
                viewId = targetView.Id.Value,
#else
                viewId = (long)targetView.Id.IntegerValue,
#endif
                viewName = targetView.Name,
                totalElementsChecked = totalChecked,
                undimensionedCount = returnedElements.Count,
                truncated = isTruncated,
                undimensionedElements = returnedElements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to find undimensioned elements: {ex.Message}");
        }
    }
}

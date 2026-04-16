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
/// Scans all views and counts detail/model lines per view, returning views
/// exceeding a threshold sorted by line count. Useful for performance auditing.
/// </summary>
public class LinesPerViewCountTool : ICortexTool
{
    public string Name => "lines_per_view_count";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Scans all views and counts detail/model lines per view, returning views exceeding a threshold sorted by line count. Useful for performance auditing.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var threshold          = input["threshold"]?.Value<int>() ?? 0;
        var includeDetailLines = input["includeDetailLines"]?.Value<bool>() ?? true;
        var includeModelLines  = input["includeModelLines"]?.Value<bool>() ?? true;
        var limit              = input["limit"]?.Value<int>() ?? 200;

        try
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType()
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .ToList();

            // Safety cap: prevent timeout on very large models
            const int maxViewScan = 300;
            bool capped = false;
            if (views.Count > maxViewScan && threshold == 0)
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Model has {views.Count} views. A full scan (threshold=0) would likely time out.",
                    suggestion: $"Set threshold >= 1 to filter results, or limit the scan scope. " +
                                $"Recommended: threshold >= 50 for models with 300+ views.");
            }
            if (views.Count > maxViewScan)
            {
                views = views.Take(maxViewScan).ToList();
                capped = true;
            }

            var viewStats = new List<(int total, object data)>();
            int totalLines = 0;
            int skippedViews = 0;

            // Determine which categories to scan (combine into one filter if both)
            bool scanBoth = includeDetailLines && includeModelLines;

            foreach (var view in views)
            {
                try
                {
                    int detailLineCount = 0;
                    int modelLineCount = 0;

                    if (scanBoth)
                    {
                        // Single collector with multi-category filter (faster than two separate collectors)
                        var catFilter = new LogicalOrFilter(
                            new ElementCategoryFilter(BuiltInCategory.OST_Lines),
                            new ElementCategoryFilter(BuiltInCategory.OST_GenericLines));

                        var allLines = new FilteredElementCollector(doc, view.Id)
                            .WherePasses(catFilter)
                            .WhereElementIsNotElementType();

                        foreach (var lineElem in allLines)
                        {
                            if (lineElem.Category == null) continue;
                            var bicId = lineElem.Category.BuiltInCategory;
                            if (bicId == BuiltInCategory.OST_Lines)
                                detailLineCount++;
                            else
                                modelLineCount++;
                        }
                    }
                    else if (includeDetailLines)
                    {
                        detailLineCount = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_Lines)
                            .WhereElementIsNotElementType()
                            .GetElementCount();
                    }
                    else if (includeModelLines)
                    {
                        modelLineCount = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_GenericLines)
                            .WhereElementIsNotElementType()
                            .GetElementCount();
                    }

                    int total = detailLineCount + modelLineCount;
                    totalLines += total;

                    if (total >= threshold)
                    {
                        viewStats.Add((total, (object)new
                        {
#if REVIT2024_OR_GREATER
                            viewId = view.Id.Value,
#else
                            viewId = (long)view.Id.IntegerValue,
#endif
                            viewName    = view.Name,
                            viewType    = view.ViewType.ToString(),
                            detailLines = detailLineCount,
                            modelLines  = modelLineCount,
                            totalLines  = total
                        }));
                    }
                }
                catch
                {
                    skippedViews++;
                }
            }

            var sorted = viewStats
                .OrderByDescending(v => v.total)
                .Select(v => v.data)
                .ToList();

            var limited = sorted.Take(limit).ToList();

            return CortexResult<object>.Ok(new
            {
                totalViewsScanned   = views.Count,
                totalLinesInProject = totalLines,
                viewsAboveThreshold = sorted.Count,
                returnedCount       = limited.Count,
                truncated           = sorted.Count > limit,
                capped,
                threshold,
                skippedViews,
                views = limited
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to count lines per view: {ex.Message}");
        }
    }
}

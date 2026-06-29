using System;
using System.Collections.Generic;
using System.Diagnostics;
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
[ToolSafety(true, false)]
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
        var timeBudgetMs       = input["timeBudgetMs"]?.Value<int>() ?? 15000;

        try
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType()
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .ToList();

            timeBudgetMs = Math.Max(1000, timeBudgetMs);

            // Single document-wide pass over the Lines category. Detail lines are
            // view-specific (OwnerViewId set) and grouped per view; model lines belong
            // to the model (no owner view) and are reported as one project-wide count.
            // The previous implementation ran one view-scoped collector PER VIEW for
            // model lines — O(views) visibility graphs, the root cause of the TCP
            // timeout/crash on 300+ view models — and double-counted each model line
            // in every view it was visible in.
            var detailLineCounts = new Dictionary<ElementId, int>();
            int modelLinesInProject = 0;
            if (includeDetailLines || includeModelLines)
            {
                var lines = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Lines)
                    .WhereElementIsNotElementType();
                foreach (var line in lines)
                {
                    var ownerViewId = line.OwnerViewId;
                    if (ownerViewId == ElementId.InvalidElementId)
                    {
                        if (includeModelLines) modelLinesInProject++;
                    }
                    else if (includeDetailLines)
                    {
                        int current;
                        detailLineCounts.TryGetValue(ownerViewId, out current);
                        detailLineCounts[ownerViewId] = current + 1;
                    }
                }
            }

            var viewStats = new List<(int total, object data)>();
            int totalDetailLines = 0;
            int skippedViews = 0;
            bool timedOut = false;

            var stopwatch = Stopwatch.StartNew();
            foreach (var view in views)
            {
                if (stopwatch.ElapsedMilliseconds > timeBudgetMs)
                {
                    timedOut = true;
                    break;
                }

                try
                {
                    int detailLineCount;
                    detailLineCounts.TryGetValue(view.Id, out detailLineCount);
                    totalDetailLines += detailLineCount;

                    if (detailLineCount >= threshold)
                    {
                        viewStats.Add((detailLineCount, (object)new
                        {
#if REVIT2024_OR_GREATER
                            viewId = view.Id.Value,
#else
                            viewId = (long)view.Id.IntegerValue,
#endif
                            viewName    = view.Name,
                            viewType    = view.ViewType.ToString(),
                            detailLines = detailLineCount,
                            totalLines  = detailLineCount
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
                totalLinesInProject = totalDetailLines + modelLinesInProject,
                detailLinesInProject = totalDetailLines,
                // Model lines are not view-specific, so they are reported once at
                // project level instead of per view.
                modelLinesInProject,
                viewsAboveThreshold = sorted.Count,
                returnedCount       = limited.Count,
                truncated           = sorted.Count > limit,
                timedOut,
                timeBudgetMs,
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

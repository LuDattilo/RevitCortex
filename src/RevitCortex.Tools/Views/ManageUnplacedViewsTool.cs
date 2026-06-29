using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Lists or deletes views that are not placed on any sheet.
/// </summary>
[ToolSafety(false, true)]
public class ManageUnplacedViewsTool : ICortexTool
{
    public string Name => "manage_unplaced_views";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists or deletes views that are not placed on any sheet.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";
        var viewTypes = input["viewTypes"]?.ToObject<List<string>>() ?? new List<string>();
        var filterName = input["filterName"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var maxResults = input["maxResults"]?.Value<int>() ?? 500;

        try
        {
            // Get all placed view IDs
            var placedViewIds = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => vp.ViewId)
                .ToHashSet();

            // Get unplaced views
            var unplaced = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && !placedViewIds.Contains(v.Id))
                .Where(v => v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
                .Where(v => v.ViewType != ViewType.Internal && v.ViewType != ViewType.DrawingSheet);

            // Filter by view types
            if (viewTypes.Count > 0)
            {
                unplaced = unplaced.Where(v =>
                    viewTypes.Any(vt => v.ViewType.ToString().Equals(vt, StringComparison.OrdinalIgnoreCase)));
            }

            // Filter by name
            if (!string.IsNullOrEmpty(filterName))
                unplaced = unplaced.Where(v => v.Name.IndexOf(filterName, StringComparison.OrdinalIgnoreCase) >= 0);

            var views = unplaced.Take(maxResults).ToList();

            if (action == "delete" && !dryRun)
            {
                // H4: confirm before permanently deleting views.
                if (!session.RequestConfirmation("delete unplaced views", views.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                        "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Delete Unplaced Views");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                int deleted = 0;
                foreach (var v in views)
                {
                    try { doc.Delete(v.Id); deleted++; } catch { /* skip protected views */ }
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
                return CortexResult<object>.Ok(new { action = "delete", deletedCount = deleted });
            }

            var result = views.Select(v => new
            {
                id = ToolHelpers.GetElementIdValue(v.Id),
                name = v.Name,
                viewType = v.ViewType.ToString()
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                action = action == "delete" ? "delete_preview" : "list",
                unplacedViewCount = result.Count,
                views = result
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

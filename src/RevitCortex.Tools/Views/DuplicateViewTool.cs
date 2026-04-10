using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Duplicates one or more views with optional naming prefix/suffix.
/// </summary>
public class DuplicateViewTool : ICortexTool
{
    public string Name => "duplicate_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates one or more views with optional naming prefix/suffix.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (viewIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewIds array is required");

        var duplicateOption = input["duplicateOption"]?.Value<string>() ?? "Duplicate";
        var prefix = input["newNamePrefix"]?.Value<string>() ?? "";
        var suffix = input["newNameSuffix"]?.Value<string>() ?? "";

        var option = duplicateOption.ToLowerInvariant() switch
        {
            "withddetailing" or "withdetailing" or "duplicate_with_detailing" => ViewDuplicateOption.WithDetailing,
            "asdependent" or "dependent" or "duplicate_as_dependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.Duplicate
        };

        try
        {
            var results = new List<object>();
            using var tx = new Transaction(doc, "RevitCortex: Duplicate Views");
            tx.Start();

            foreach (var vid in viewIds)
            {
#if REVIT2024_OR_GREATER
                var view = doc.GetElement(new ElementId(vid)) as View;
#else
                var view = doc.GetElement(new ElementId((int)vid)) as View;
#endif
                if (view == null) continue;

                var newId = view.Duplicate(option);
                var newView = doc.GetElement(newId) as View;
                if (newView != null)
                {
                    if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
                    {
                        try { newView.Name = $"{prefix}{view.Name}{suffix}"; }
                        catch { /* name conflict, keep auto-generated */ }
                    }
                    results.Add(new
                    {
                        originalViewId = vid,
                        newViewId = GetIdLong(newId),
                        newViewName = newView.Name
                    });
                }
            }

            tx.Commit();
            return CortexResult<object>.Ok(new { duplicatedCount = results.Count, views = results });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

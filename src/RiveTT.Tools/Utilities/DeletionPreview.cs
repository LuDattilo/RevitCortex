using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RiveTT.Core.Results;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// The shared dryRun preview for the single-element delete tools.
///
/// delete_material, delete_schedule and manage_selection's delete action (formerly the
/// standalone delete_selection) all destroyed their target on the first call: they went
/// through session.RequestConfirmation, which is a compatibility no-op that always returns
/// true, and had no dryRun at all — while delete_element, the tool they most resemble,
/// previews by default. This is that preview, once.
///
/// The cascade is probed exactly as DeleteElementTool does it: doc.Delete inside a
/// transaction that is then rolled back returns every element the deletion would drag
/// along, and the rollback restores them so their names still resolve. Naming only the
/// requested element understates the damage — deleting one Level previewed as 1 element
/// and really removed about 100.
/// </summary>
public static class DeletionPreview
{
    /// <summary>Previews the deletion of a single element.</summary>
    /// <param name="label">Human label for the message, e.g. "Material 'Béton'".</param>
    /// <param name="identity">Tool-specific identity fields merged into the response.</param>
    public static CortexResult<object> Build(Document doc, ElementId id, string label, object identity)
    {
        return Build(doc, new[] { id }, label, identity);
    }

    /// <summary>Previews the deletion of a set of elements.</summary>
    public static CortexResult<object> Build(
        Document doc, IList<ElementId> ids, string label, object identity)
    {
        var dependentCount = 0;
        List<object>? dependentSample = null;
        string? cascadePreviewError = null;

        try
        {
            List<ElementId> wouldDelete;
            using (var probe = new Transaction(doc, "RiveTT: Delete Preview"))
            {
                TransactionFailureHandling.SuppressWarnings(probe);
                probe.Start();
                wouldDelete = doc.Delete(ids.ToList()).ToList();
                probe.RollBack();
            }

            var requested = new HashSet<ElementId>(ids);
            var dependents = wouldDelete.Where(x => !requested.Contains(x)).ToList();
            dependentCount = dependents.Count;
            dependentSample = dependents
                .Take(20)
                .Select(doc.GetElement)
                .Where(e => e != null)
                .Select(e => (object)new
                {
                    elementId = ToolHelpers.GetElementIdValue(e!.Id),
                    name = e.Name,
                    category = e.Category?.Name,
                    categoryBic = CategoryResolver.DescribeBuiltInCategory(e.Category)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            // A rolled-back probe can still fail (an element Revit refuses to delete at
            // all). Say so rather than reporting a cascade of zero.
            cascadePreviewError = ex.Message;
        }

        var payload = Newtonsoft.Json.Linq.JObject.FromObject(new
        {
            dryRun = true,
            message = cascadePreviewError == null
                ? $"DryRun: {label} would be deleted, cascading to {dependentCount} dependent element(s) "
                  + $"({ids.Count + dependentCount} total). Set dryRun=false to execute."
                : $"DryRun: {label} would be deleted (cascade preview unavailable). "
                  + "Set dryRun=false to execute.",
            requestedCount = ids.Count,
            dependentCount,
            totalWouldDelete = ids.Count + dependentCount,
            dependentSample,
            cascadePreviewError
        });

        // Merge the tool's own identity fields (materialName, scheduleName, ...) alongside.
        payload.Merge(Newtonsoft.Json.Linq.JObject.FromObject(identity));
        return CortexResult<object>.Ok(payload);
    }
}

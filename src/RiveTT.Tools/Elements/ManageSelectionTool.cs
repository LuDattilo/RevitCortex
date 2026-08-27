using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// CRUD on named saved selections (SelectionFilterElement), replacing save_selection,
/// load_selection, and delete_selection: all three resolved the same element by the same
/// name lookup, the textbook case for an action-based merge. capture_selection stays a
/// separate tool — it manipulates a session-scoped token, not a document-persisted element,
/// and merging the two would mix an ephemeral TTL with document persistence.
/// Actions: save | load | list | delete. name is required for save/load/delete, ignored for
/// list. elementIds/overwrite apply to save only (elementIds absent = current UI selection).
/// selectInView applies to load only. dryRun applies to delete only.
/// </summary>
[ToolSafety(false, true)]
public class ManageSelectionTool : ICortexTool
{
    public string Name => "manage_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "CRUD on named saved selections (SelectionFilterElement). action=save|load|list|delete. "
        + "name is required for save/load/delete (ignored for list). save: elementIds (absent = "
        + "current UI selection) and overwrite (default false) apply only here. load: "
        + "selectInView (default true) applies only here. delete: dryRun (default true) applies "
        + "only here. Use capture_selection instead for a temporary session-scoped token — this "
        + "tool always persists into the document.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "").ToLowerInvariant();
        return action switch
        {
            "save" => Save(doc, input),
            "load" => Load(doc, input, requireName: true),
            "list" => Load(doc, input, requireName: false),
            "delete" => Delete(doc, input),
            _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unsupported action: '{action}'", suggestion: "Use: save | load | list | delete")
        };
    }

    private static SelectionFilterElement? FindByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(SelectionFilterElement))
            .Cast<SelectionFilterElement>()
            .FirstOrDefault(sf => sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static CortexResult<object> Save(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required for action=save");

        var elementIds = input["elementIds"]?.ToObject<List<long>>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;

        try
        {
            ICollection<ElementId> ids;
            if (elementIds != null && elementIds.Count > 0)
            {
                ids = elementIds.Select(id => new ElementId(id)).ToList();
            }
            else
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                ids = uidoc.Selection.GetElementIds();
                if (ids.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "No elements selected and no elementIds provided");
            }

            var existing = FindByName(doc, name);

            using var tx = new Transaction(doc, "RiveTT: Save Selection");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (existing != null)
            {
                if (!overwrite)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Selection '{name}' already exists. Set overwrite=true to replace.");
                doc.Delete(existing.Id);
            }

            var filter = SelectionFilterElement.Create(doc, name);
            filter.SetElementIds(ids);

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                overwritten = existing != null
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to save selection: {ex.Message}");
        }
    }

    private static CortexResult<object> Load(Document doc, JObject input, bool requireName)
    {
        var name = input["name"]?.Value<string>();
        if (requireName && string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required for action=load");

        var selectInView = input["selectInView"]?.Value<bool>() ?? true;

        try
        {
            var allFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            if (string.IsNullOrEmpty(name))
            {
                var selections = allFilters.Select(sf => new
                {
                    name = sf.Name,
                    id = ToolHelpers.GetElementIdValue(sf.Id),
                    elementCount = sf.GetElementIds().Count
                }).ToList();

                return CortexResult<object>.Ok(new
                {
                    selectionCount = selections.Count,
                    selections
                });
            }

            var filter = allFilters.FirstOrDefault(sf =>
                sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call manage_selection(action: \"list\") to see the saved selections.");

            var elementIds = filter.GetElementIds();

            if (selectInView)
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                uidoc.Selection.SetElementIds(elementIds);
            }

            var ids = elementIds.Select(ToolHelpers.GetElementIdValue).ToList();

            return CortexResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                elementIds = ids,
                selectedInView = selectInView
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to load selection: {ex.Message}");
        }
    }

    private static CortexResult<object> Delete(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required for action=delete");

        try
        {
            var filter = FindByName(doc, name);
            if (filter == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call manage_selection(action: \"list\") to see the saved selections.");

            if (ToolHelpers.GetDryRun(input))
            {
                var memberCount = 0;
                try { memberCount = filter.GetElementIds().Count; } catch { }

                return DeletionPreview.Build(doc, filter.Id,
                    $"Saved selection '{name}'",
                    new
                    {
                        selectionName = name,
                        selectionId = ToolHelpers.GetElementIdValue(filter.Id),
                        elementsInSelection = memberCount,
                        note = "Only the saved list is deleted; the " + memberCount
                             + " element(s) it references stay in the model."
                    });
            }

            using var tx = new Transaction(doc, "RiveTT: Delete Selection");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            doc.Delete(filter.Id);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                deletedSelection = name,
                success = true
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to delete selection: {ex.Message}");
        }
    }
}

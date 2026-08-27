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
/// Lists, creates, and deletes named view/sheet sets (ViewSheetSet) — the saved
/// selections batch_export lacks, so a print/export list does not need re-passing
/// every time. Built on Document.PrintManager.ViewSheetSetting, the API's own path
/// for saving a named set (PrintRange.Select + ViewSheetSetting.SaveAs).
/// </summary>
[ToolSafety(false, false)]
public class ManageSheetSetsTool : ICortexTool
{
    public string Name => "manage_sheet_sets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Lists, creates, or deletes named view/sheet sets (ViewSheetSet), so batch_export and printing can " +
        "reuse a saved list of views/sheets instead of one passed on every call. action=list|create|delete. " +
        "create needs name + viewIds/sheetIds (element IDs, either views or sheets). Revit has no rename API for " +
        "a saved set: recreate it under the new name instead.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();
        try
        {
            return action switch
            {
                "list" => ListSheetSets(doc),
                "create" => CreateSheetSet(doc, input),
                "delete" => DeleteSheetSet(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported action: {action}",
                    suggestion: "Use: list | create | delete")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListSheetSets(Document doc)
    {
        var sets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheetSet))
            .Cast<ViewSheetSet>()
            .Select(s => new
            {
                id = ToolHelpers.GetElementIdValue(s.Id),
                name = s.Name,
                viewCount = s.Views.Size
            })
            .ToList();

        return CortexResult<object>.Ok(new { count = sets.Count, sheetSets = sets });
    }

    private static CortexResult<object> CreateSheetSet(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        var ids = input["viewIds"]?.ToObject<List<long>>() ?? input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (string.IsNullOrWhiteSpace(name) || ids.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "name and a non-empty viewIds/sheetIds array are required");

        var viewSet = new ViewSet();
        var missing = new List<long>();
        foreach (var vid in ids)
        {
            var view = doc.GetElement(ToolHelpers.ToElementId(vid)) as View;
            if (view == null) { missing.Add(vid); continue; }
            viewSet.Insert(view);
        }

        if (viewSet.IsEmpty)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "None of the given IDs resolved to a view or sheet", suggestion: "Check the element IDs");

        var printManager = doc.PrintManager;

        using var tx = new Transaction(doc, "RiveTT: Create Sheet Set");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        printManager.PrintRange = PrintRange.Select;
        var viewSheetSetting = printManager.ViewSheetSetting;
        viewSheetSetting.CurrentViewSheetSet.Views = viewSet;

        // A name already in use throws; surface that instead of a rolled-back transaction
        // with no explanation.
        try
        {
            viewSheetSetting.SaveAs(name!);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not save sheet set '{name}': {ex.Message}",
                suggestion: "A sheet set with this name may already exist; delete it first or pick another name.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        var created = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>()
            .FirstOrDefault(s => s.Name == name);

        return CortexResult<object>.Ok(new
        {
            action = "create",
            name,
            id = created != null ? ToolHelpers.GetElementIdValue(created.Id) : (long?)null,
            includedCount = viewSet.Size,
            missingIds = missing
        });
    }

    private static CortexResult<object> DeleteSheetSet(Document doc, JObject input)
    {
        var elementIdLong = input["elementId"]?.Value<long?>() ?? 0;
        var name = input["name"]?.Value<string>();

        ViewSheetSet? target = elementIdLong > 0
            ? doc.GetElement(ToolHelpers.ToElementId(elementIdLong)) as ViewSheetSet
            : new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == name);

        if (target == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No matching sheet set found", suggestion: "Provide elementId or an exact name from action=list");

        using var tx = new Transaction(doc, "RiveTT: Delete Sheet Set");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var deletedName = target.Name;
        doc.Delete(target.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { action = "delete", name = deletedName });
    }
}

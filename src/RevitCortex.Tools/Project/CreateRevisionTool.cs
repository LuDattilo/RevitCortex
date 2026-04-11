using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists, creates, or assigns revisions to sheets.
/// </summary>
public class CreateRevisionTool : ICortexTool
{
    public string Name => "create_revision";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, creates, or assigns revisions to sheets.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListRevisions(doc),
                "create" => CreateNewRevision(doc, input),
                "add_to_sheets" => AddToSheets(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: list, create, or add_to_sheets")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListRevisions(Document doc)
    {
        var revisionIds = Revision.GetAllRevisionIds(doc);
        var revisions = revisionIds.Select(id =>
        {
            var rev = doc.GetElement(id) as Revision;
            return new
            {
                id = ToolHelpers.GetElementIdValue(id),
                sequenceNumber = rev?.SequenceNumber ?? 0,
                date = rev?.RevisionDate ?? "",
                description = rev?.Description ?? "",
                issuedBy = rev?.IssuedBy ?? "",
                issuedTo = rev?.IssuedTo ?? ""
            };
        }).ToList();

        return CortexResult<object>.Ok(new { revisionCount = revisions.Count, revisions });
    }

    private static CortexResult<object> CreateNewRevision(Document doc, JObject input)
    {
        var date = input["date"]?.Value<string>();
        var description = input["description"]?.Value<string>();
        var issuedBy = input["issuedBy"]?.Value<string>();
        var issuedTo = input["issuedTo"]?.Value<string>();

        using var tx = new Transaction(doc, "RevitCortex: Create Revision");
        tx.Start();

        var revision = Revision.Create(doc);
        if (!string.IsNullOrEmpty(date)) revision.RevisionDate = date;
        if (!string.IsNullOrEmpty(description)) revision.Description = description;
        if (!string.IsNullOrEmpty(issuedBy)) revision.IssuedBy = issuedBy;
        if (!string.IsNullOrEmpty(issuedTo)) revision.IssuedTo = issuedTo;

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "create",
            revisionId = ToolHelpers.GetElementIdValue(revision.Id),
            date = revision.RevisionDate,
            description = revision.Description
        });
    }

    private static CortexResult<object> AddToSheets(Document doc, JObject input)
    {
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>();
        var revisionIdLong = input["revisionId"]?.Value<long>() ?? 0;

        if (sheetIds == null || sheetIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetIds array is required");

        // Use specified revision or latest
        ElementId revisionId;
        if (revisionIdLong > 0)
        {
#if REVIT2024_OR_GREATER
            revisionId = new ElementId(revisionIdLong);
#else
            revisionId = new ElementId((int)revisionIdLong);
#endif
        }
        else
        {
            var allRevIds = Revision.GetAllRevisionIds(doc);
            if (allRevIds.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No revisions exist");
            revisionId = allRevIds.Last();
        }

        using var tx = new Transaction(doc, "RevitCortex: Add Revision to Sheets");
        tx.Start();

        int updatedCount = 0;
        foreach (var sid in sheetIds)
        {
#if REVIT2024_OR_GREATER
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
#else
            var sheet = doc.GetElement(new ElementId((int)sid)) as ViewSheet;
#endif
            if (sheet == null) continue;

            var existing = sheet.GetAdditionalRevisionIds().ToList();
            if (!existing.Contains(revisionId))
            {
                existing.Add(revisionId);
                sheet.SetAdditionalRevisionIds(existing);
                updatedCount++;
            }
        }

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "add_to_sheets",
            revisionId = ToolHelpers.GetElementIdValue(revisionId),
            updatedSheetCount = updatedCount
        });
    }
}

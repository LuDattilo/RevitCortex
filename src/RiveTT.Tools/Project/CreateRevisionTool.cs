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
/// Lists, creates, or assigns revisions to sheets, and draws the cloud that localizes
/// one on a view (create_revision alone only created the Revision element itself).
/// </summary>
[ToolSafety(false, false)]
public class CreateRevisionTool : ICortexTool
{
    public string Name => "create_revision";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, creates, updates, or assigns revisions to sheets, and draws revision clouds. Actions: list, create, set, add_to_sheets, create_cloud.";
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
                "set" => SetRevision(doc, input),
                "add_to_sheets" => AddToSheets(doc, input),
                "create_cloud" => CreateCloud(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: list, create, set, add_to_sheets, or create_cloud")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws a revision cloud in a view (RevisionCloud.Create). Revit refuses this once
    /// the revision has been marked Issued — the failure surfaces as a clear message
    /// rather than a raw exception.
    /// </summary>
    private static CortexResult<object> CreateCloud(Document doc, JObject input)
    {
        var revisionIdLong = input["revisionId"]?.Value<long?>() ?? 0;
        var viewIdLong = input["viewId"]?.Value<long?>() ?? 0;
        var curvesArray = input["curves"] as JArray;
        if (revisionIdLong <= 0 || viewIdLong <= 0 || curvesArray == null || curvesArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "revisionId, viewId, and a non-empty curves array are required",
                suggestion: "Provide {\"revisionId\":123, \"viewId\":456, \"curves\":[{\"type\":\"line\",\"start\":{...},\"end\":{...}}, ...]} forming a closed loop");

        var revision = doc.GetElement(ToolHelpers.ToElementId(revisionIdLong)) as Revision;
        if (revision == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Revision {revisionIdLong} not found");
        if (revision.Issued)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "This revision is marked Issued: Revit refuses new clouds on an issued revision",
                suggestion: "Use a non-issued revision, or create_revision(action=create) a new one first");

        var view = doc.GetElement(ToolHelpers.ToElementId(viewIdLong)) as View;
        if (view == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"View {viewIdLong} not found");

        var curves = CurveSpecHelpers.ParseCurveSpecsMm(curvesArray, out var curveError);
        if (curveError != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, curveError);

        using var tx = new Transaction(doc, "RiveTT: Create Revision Cloud");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        RevisionCloud cloud;
        try
        {
            cloud = RevisionCloud.Create(doc, view, revision.Id, curves);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"RevisionCloud.Create failed: {ex.Message}");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "create_cloud",
            revisionCloudId = ToolHelpers.GetElementIdValue(cloud.Id),
            revisionId = ToolHelpers.GetElementIdValue(revision.Id),
            viewId = ToolHelpers.GetElementIdValue(view.Id)
        });
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

        using var tx = new Transaction(doc, "RiveTT: Create Revision");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var revision = Revision.Create(doc);
        if (!string.IsNullOrEmpty(date)) revision.RevisionDate = date;
        if (!string.IsNullOrEmpty(description)) revision.Description = description;
        if (!string.IsNullOrEmpty(issuedBy)) revision.IssuedBy = issuedBy;
        if (!string.IsNullOrEmpty(issuedTo)) revision.IssuedTo = issuedTo;
        ApplyIssuedAndVisibility(revision, input);

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "create",
            revisionId = ToolHelpers.GetElementIdValue(revision.Id),
            date = revision.RevisionDate,
            description = revision.Description,
            issued = revision.Issued,
            visibility = revision.Visibility.ToString()
        });
    }

    private static CortexResult<object> SetRevision(Document doc, JObject input)
    {
        var revisionIdLong = input["revisionId"]?.Value<long>() ?? 0;
        if (revisionIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "revisionId is required for action 'set'");

        var revision = doc.GetElement(ToolHelpers.ToElementId(revisionIdLong)) as Revision;
        if (revision == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Revision {revisionIdLong} not found");

        using var tx = new Transaction(doc, "RiveTT: Update Revision");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var date = input["date"]?.Value<string>();
        var description = input["description"]?.Value<string>();
        var issuedBy = input["issuedBy"]?.Value<string>();
        var issuedTo = input["issuedTo"]?.Value<string>();
        if (date != null) revision.RevisionDate = date;
        if (description != null) revision.Description = description;
        if (issuedBy != null) revision.IssuedBy = issuedBy;
        if (issuedTo != null) revision.IssuedTo = issuedTo;
        ApplyIssuedAndVisibility(revision, input);

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "set",
            revisionId = ToolHelpers.GetElementIdValue(revision.Id),
            date = revision.RevisionDate,
            description = revision.Description,
            issued = revision.Issued,
            visibility = revision.Visibility.ToString()
        });
    }

    /// <summary>Applies the optional Issued flag and Visibility (cloud/tag/none) from input.</summary>
    private static void ApplyIssuedAndVisibility(Revision revision, JObject input)
    {
        var issued = input["issued"]?.Value<bool?>();
        if (issued.HasValue) revision.Issued = issued.Value;

        var visibility = input["visibility"]?.Value<string>();
        if (!string.IsNullOrEmpty(visibility))
        {
            revision.Visibility = visibility!.ToLowerInvariant().Replace("_", "").Replace(" ", "") switch
            {
                "none" or "hidden" => RevisionVisibility.Hidden,
                "tagonly" or "tag" or "tagvisible" => RevisionVisibility.TagVisible,
                _ => RevisionVisibility.CloudAndTagVisible
            };
        }
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
            revisionId = new ElementId(revisionIdLong);
        }
        else
        {
            var allRevIds = Revision.GetAllRevisionIds(doc);
            if (allRevIds.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No revisions exist");
            revisionId = allRevIds.Last();
        }

        using var tx = new Transaction(doc, "RiveTT: Add Revision to Sheets");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        int updatedCount = 0;
        foreach (var sid in sheetIds)
        {
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
            if (sheet == null) continue;

            var existing = sheet.GetAdditionalRevisionIds().ToList();
            if (!existing.Contains(revisionId))
            {
                existing.Add(revisionId);
                sheet.SetAdditionalRevisionIds(existing);
                updatedCount++;
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "add_to_sheets",
            revisionId = ToolHelpers.GetElementIdValue(revisionId),
            updatedSheetCount = updatedCount
        });
    }
}

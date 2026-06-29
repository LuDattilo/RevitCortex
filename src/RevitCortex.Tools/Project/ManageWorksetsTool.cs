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
/// Creates, renames, deletes, opens, closes, or sets the active workset.
/// Write counterpart of <c>get_worksets</c>; only available for workshared documents.
/// </summary>
[ToolSafety(false, true)]
public class ManageWorksetsTool : ICortexTool
{
    public string Name => "manage_worksets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Creates, renames, deletes, or sets the active workset (workshared models only). Actions: create, rename, delete, set_active. (Opening/closing worksets on a live document is a Revit UI operation with no API — not exposed.)";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        if (!doc.IsWorkshared)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Project is not workshared — worksets are not available",
                suggestion: "Worksets require a workshared model. Use get_project_info to check isWorkshared.");

        var action = (input["action"]?.Value<string>() ?? "").ToLowerInvariant();

        try
        {
            return action switch
            {
                "create"     => CreateWorkset(doc, input, session),
                "rename"     => RenameWorkset(doc, input, session),
                "delete"     => DeleteWorkset(doc, input, session),
                "set_active" => SetActiveWorkset(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: create, rename, delete, set_active")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to manage worksets: {ex.Message}");
        }
    }

    private static CortexResult<object> CreateWorkset(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required for create");

        if (!WorksetTable.IsWorksetNameUnique(doc, name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"A workset named '{name}' already exists");

        if (!session.RequestConfirmation("create workset", 1, name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var workset = Workset.Create(doc, name);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "create",
            worksetId = workset.Id.IntegerValue,
            name = workset.Name
        });
    }

    private static CortexResult<object> RenameWorkset(Document doc, JObject input, CortexSession session)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newName is required for rename");

        if (!WorksetTable.IsWorksetNameUnique(doc, newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"A workset named '{newName}' already exists");

        if (!session.RequestConfirmation("rename workset", 1, workset!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var oldName = workset.Name;
        using var tx = new Transaction(doc, "RevitCortex: Rename Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        WorksetTable.RenameWorkset(doc, workset.Id, newName);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new { action = "rename", worksetId = workset.Id.IntegerValue, oldName, newName });
    }

    private static CortexResult<object> DeleteWorkset(Document doc, JObject input, CortexSession session)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        if (workset!.Kind != WorksetKind.UserWorkset)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Only user worksets can be deleted");

        if (!session.RequestConfirmation("delete workset", 1, workset.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        // Elements on a deleted workset must move somewhere; default to another user workset.
        var fallback = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
            .FirstOrDefault(w => w.Id != workset.Id);
        if (fallback == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Cannot delete the only user workset");

        var name = workset.Name;
        using var tx = new Transaction(doc, "RevitCortex: Delete Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var settings = new DeleteWorksetSettings(DeleteWorksetOption.MoveElementsToWorkset, fallback.Id);
        WorksetTable.DeleteWorkset(doc, workset.Id, settings);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "delete",
            deletedWorkset = name,
            elementsMovedTo = fallback.Name
        });
    }

    private static CortexResult<object> SetActiveWorkset(Document doc, JObject input)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        doc.GetWorksetTable().SetActiveWorksetId(workset!.Id);
        return CortexResult<object>.Ok(new { action = "set_active", worksetId = workset.Id.IntegerValue, name = workset.Name });
    }

    /// <summary>Resolves a workset by worksetId (int) or name from the input.</summary>
    private static (Workset?, CortexResult<object>?) ResolveWorkset(Document doc, JObject input)
    {
        var worksetIdInt = input["worksetId"]?.Value<int?>();
        var name = input["name"]?.Value<string>();

        var all = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToList();

        Workset? workset = null;
        if (worksetIdInt.HasValue)
            workset = all.FirstOrDefault(w => w.Id.IntegerValue == worksetIdInt.Value);
        if (workset == null && !string.IsNullOrEmpty(name))
            workset = all.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (workset == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Workset not found", suggestion: "Provide a valid worksetId or name (list them with get_worksets)"));

        return (workset, null);
    }
}

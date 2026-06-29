using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Writes editable Project Information fields (name, number, address, author,
/// organization, status, client, etc.). Write counterpart of <c>get_project_info</c>.
/// </summary>
[ToolSafety(false, false)]
public class SetProjectInfoTool : ICortexTool
{
    public string Name => "set_project_info";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Sets editable Project Information fields: projectName, projectNumber, projectAddress, buildingName, author, organizationName, organizationDescription, issueDate, status, clientName. Only the fields you provide are changed.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var info = doc.ProjectInformation;
        if (info == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No Project Information element in document");

        // Map of input key -> setter action. Only keys present in the request are applied.
        var changed = new List<string>();

        if (!session.RequestConfirmation("update project information", 1, doc.Title))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            using var tx = new Transaction(doc, "RevitCortex: Set Project Info");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            ApplyString(input, "projectName",              v => info.Name = v,                    changed, "projectName");
            ApplyString(input, "projectNumber",            v => info.Number = v,                  changed, "projectNumber");
            ApplyString(input, "projectAddress",           v => info.Address = v,                 changed, "projectAddress");
            ApplyString(input, "buildingName",             v => info.BuildingName = v,            changed, "buildingName");
            ApplyString(input, "author",                   v => info.Author = v,                  changed, "author");
            ApplyString(input, "organizationName",         v => info.OrganizationName = v,        changed, "organizationName");
            ApplyString(input, "organizationDescription",  v => info.OrganizationDescription = v, changed, "organizationDescription");
            ApplyString(input, "issueDate",                v => info.IssueDate = v,               changed, "issueDate");
            ApplyString(input, "status",                   v => info.Status = v,                  changed, "status");

            // Client name has no dedicated property — it's a built-in parameter.
            var clientName = input["clientName"]?.Value<string>();
            if (clientName != null)
            {
                var p = info.get_Parameter(BuiltInParameter.CLIENT_NAME);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(clientName);
                    changed.Add("clientName");
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            if (changed.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No recognized fields provided",
                    suggestion: "Provide at least one of: projectName, projectNumber, projectAddress, buildingName, author, organizationName, organizationDescription, issueDate, status, clientName");

            return CortexResult<object>.Ok(new
            {
                message = $"Updated {changed.Count} field(s)",
                changedFields = changed
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to set project info: {ex.Message}");
        }
    }

    /// <summary>Applies a string setter only when the key is present (non-null) in the input.</summary>
    private static void ApplyString(JObject input, string key, Action<string> setter, List<string> changed, string label)
    {
        var token = input[key];
        if (token == null || token.Type == JTokenType.Null) return;
        setter(token.Value<string>() ?? "");
        changed.Add(label);
    }
}

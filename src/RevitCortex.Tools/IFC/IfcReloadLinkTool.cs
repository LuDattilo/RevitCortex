using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reloads an existing IFC link, optionally from a new IFC file path.
/// </summary>
public class IfcReloadLinkTool : ICortexTool
{
    public string Name => "ifc_reload_link";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reload an existing IFC link, optionally from a new IFC file path";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var linkTypeId = input["linkTypeId"]?.Value<long>() ?? 0;
        if (linkTypeId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "linkTypeId is required",
                suggestion: "Provide the RevitLinkType element ID of the IFC link");

        var elementId = ToolHelpers.ToElementId(linkTypeId);
        var linkType = doc!.GetElement(elementId) as RevitLinkType;
        if (linkType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"RevitLinkType {linkTypeId} not found");

        var currentRvtPath = "";
        try
        {
            var extRef = linkType.GetExternalFileReference();
            if (extRef != null)
                currentRvtPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
        }
        catch { /* path unavailable */ }

        var newIfcFilePath = input["newIfcFilePath"]?.Value<string>();
        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        if (!string.IsNullOrWhiteSpace(newIfcFilePath))
        {
            // H25-wave: gate caller paths; UNC allowed because linking from network shares
            // is a standard BIM workflow and the confirmation dialog shows the path.
            // The derived .RVT cache is written next to this path, so it is covered too.
            if (!PathSafety.TryResolveSafe(newIfcFilePath, out var safeIfcPath, out var pathError, allowUnc: true))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    pathError,
                    suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
            newIfcFilePath = safeIfcPath;

            if (!File.Exists(newIfcFilePath))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"New IFC file not found: {newIfcFilePath}");
        }

        var description = string.IsNullOrWhiteSpace(newIfcFilePath)
            ? $"Reload IFC link '{linkType.Name}'"
            : $"Reload IFC link '{linkType.Name}' from '{Path.GetFileName(newIfcFilePath)}'";

        if (!session.RequestConfirmation("reload IFC link", 1, description))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            if (!string.IsNullOrWhiteSpace(newIfcFilePath))
            {
                var revitFilePath = newIfcFilePath + ".RVT";
                var options = new RevitLinkOptions(false);
                // CreateFromIFC modifies the document and must run inside a transaction
                // (mirrors IfcLinkTool). Without it Revit throws "Cannot modify the document
                // outside of a transaction" (ultrareview C6).
                using (var tx = new Transaction(doc!, "RevitCortex: Reload IFC Link"))
                {
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    RevitLinkType.CreateFromIFC(doc!, newIfcFilePath, revitFilePath, recreateLink, options);
                    if (tx.Commit() != TransactionStatus.Committed)
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                            $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                            suggestion: "Fix the reported model errors and retry.");
                }
            }
            else
            {
                var result = linkType.Reload();
                if (result.LoadResult != LinkLoadResultType.LinkLoaded)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Reload failed with status: {result.LoadResult}");
            }

            return CortexResult<object>.Ok(new
            {
                linkTypeId,
                name = linkType.Name,
                action = string.IsNullOrWhiteSpace(newIfcFilePath) ? "reloaded" : "reloaded_from_new_path",
                newIfcFilePath,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to reload IFC link: {ex.Message}");
        }
    }
}

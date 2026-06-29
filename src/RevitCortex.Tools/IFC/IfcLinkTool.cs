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
/// Links an IFC file into the active document using RevitLinkType.CreateFromIFC.
/// Creates an intermediate .ifc.RVT file and a RevitLinkInstance.
/// </summary>
[ToolSafety(false, false)]
public class IfcLinkTool : ICortexTool
{
    public string Name => "ifc_link";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Link an IFC file into the active Revit document";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var ifcFilePath = input["ifcFilePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(ifcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "ifcFilePath is required",
                suggestion: "Provide the full path to the IFC file to link");

        // H25-wave: gate caller paths; UNC allowed because linking from network shares
        // is a standard BIM workflow and the confirmation dialog shows the path.
        if (!PathSafety.TryResolveSafe(ifcFilePath, out var safeIfcPath, out var pathError, allowUnc: true))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
        ifcFilePath = safeIfcPath;

        if (!File.Exists(ifcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"IFC file not found: {ifcFilePath}");

        var revitFilePath = input["revitFilePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(revitFilePath))
        {
            revitFilePath = ifcFilePath + ".RVT";
        }
        else
        {
            // CreateFromIFC overwrites this file — never let it point at an unvetted location.
            if (!PathSafety.TryResolveSafe(revitFilePath, out var safeRvtPath, out var rvtPathError, allowUnc: true))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    rvtPathError,
                    suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
            revitFilePath = safeRvtPath;
        }

        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        var confirmDetail = File.Exists(revitFilePath)
            ? $"Link: {Path.GetFileName(ifcFilePath)} (overwrites cache {Path.GetFileName(revitFilePath)})"
            : $"Link: {Path.GetFileName(ifcFilePath)}";
        if (!session.RequestConfirmation("link IFC file", 1, confirmDetail))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new RevitLinkOptions(false);

            ElementId linkTypeId;
            using (var tx = new Transaction(doc!, "RevitCortex: Link IFC"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                var linkResult = RevitLinkType.CreateFromIFC(
                    doc!, ifcFilePath, revitFilePath, recreateLink, options);

                linkTypeId = linkResult.ElementId;
                if (linkTypeId == null || linkTypeId == ElementId.InvalidElementId)
                {
                    tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        "CreateFromIFC returned an invalid element ID");
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            RevitLinkInstance instance;
            using (var tx2 = new Transaction(doc!, "RevitCortex: Place IFC Link"))
            {
                var txFailures2 = TransactionFailureHandling.SuppressWarnings(tx2);
                tx2.Start();
                instance = RevitLinkInstance.Create(doc!, linkTypeId);
                if (tx2.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures2)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            return CortexResult<object>.Ok(new
            {
                linkTypeId = ToolHelpers.GetElementIdValue(linkTypeId),
                instanceId = ToolHelpers.GetElementIdValue(instance.Id),
                name = instance.Name,
                ifcFilePath,
                revitFilePath,
                recreatedLink = recreateLink,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to link IFC file: {ex.Message}",
                suggestion: "Ensure the IFC file is valid and Revit can access the path");
        }
    }
}

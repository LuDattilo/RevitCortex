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

        if (!File.Exists(ifcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"IFC file not found: {ifcFilePath}");

        var revitFilePath = input["revitFilePath"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(revitFilePath))
            revitFilePath = ifcFilePath + ".RVT";

        var recreateLink = input["recreateLink"]?.Value<bool>() ?? true;

        if (!session.RequestConfirmation("link IFC file", 1, $"Link: {Path.GetFileName(ifcFilePath)}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new RevitLinkOptions(false);

            ElementId linkTypeId;
            using (var tx = new Transaction(doc!, "RevitCortex: Link IFC"))
            {
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
                tx.Commit();
            }

            RevitLinkInstance instance;
            using (var tx2 = new Transaction(doc!, "RevitCortex: Place IFC Link"))
            {
                tx2.Start();
                instance = RevitLinkInstance.Create(doc!, linkTypeId);
                tx2.Commit();
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

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

        if (!string.IsNullOrWhiteSpace(newIfcFilePath) && !File.Exists(newIfcFilePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"New IFC file not found: {newIfcFilePath}");

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
                RevitLinkType.CreateFromIFC(doc!, newIfcFilePath, revitFilePath, recreateLink, options);
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

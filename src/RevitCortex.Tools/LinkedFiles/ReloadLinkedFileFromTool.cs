using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Reloads a linked Revit file from a different file path.
/// </summary>
[ToolSafety(false, true)]
public class ReloadLinkedFileFromTool : ICortexTool
{
    public string Name => "reload_linked_file_from";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Reloads a linked Revit file from a different file path. Use this to repath a link.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var linkTypeId = input["linkTypeId"]?.Value<long>() ?? 0;
        var newPath = input["newPath"]?.Value<string>();

        if (linkTypeId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "linkTypeId is required");
        if (string.IsNullOrWhiteSpace(newPath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newPath is required");

        // H25-wave: gate caller paths; UNC allowed because linking models from network
        // shares is a standard BIM workflow and the confirmation dialog shows the path.
        if (!PathSafety.TryResolveSafe(newPath, out var safePath, out var pathError, allowUnc: true))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, temp, or a network share");
        newPath = safePath;

        try
        {
#if REVIT2024_OR_GREATER
            var linkType = doc.GetElement(new ElementId(linkTypeId)) as RevitLinkType;
#else
            var linkType = doc.GetElement(new ElementId((int)linkTypeId)) as RevitLinkType;
#endif
            if (linkType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "RevitLinkType not found");

            // Get old path for reference
            var oldPath = "";
            try
            {
                var extRef = linkType.GetExternalFileReference();
                if (extRef != null)
                    oldPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
            }
            catch { /* old path unavailable */ }

            if (!session.RequestConfirmation("repath linked file", 1,
                $"Repath '{linkType.Name}' from '{oldPath}' to '{newPath}'"))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(newPath);

            // LoadFrom is not a transactable operation — must be called outside a Transaction
            linkType.LoadFrom(modelPath, new WorksetConfiguration());

            return CortexResult<object>.Ok(new
            {
                linkTypeId,
                name = linkType.Name,
                oldPath,
                newPath,
                action = "reloaded_from"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

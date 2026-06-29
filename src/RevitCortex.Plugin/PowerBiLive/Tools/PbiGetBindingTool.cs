using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Returns the ProjectBinding stored for the active Revit document,
/// or a clear message if no binding exists yet.
///
/// Useful for:
///   - Verifying which workspace/dataset a document is bound to.
///   - Debugging why pbi_publish_elements resolved an unexpected dataset.
///   - Confirming that a binding was saved after the first publish.
///
/// No inputs required.
/// </summary>
[ToolSafety(true, false)]
public class PbiGetBindingTool : ICortexTool
{
    public string Name => "pbi_get_binding";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Returns the Power BI ProjectBinding stored for the active Revit document " +
        "(workspaceId, datasetId, datasetName, docKey, updatedAt). " +
        "Returns a clear message if no binding exists yet. Read-only.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

        var docKey = ProjectDocumentKey.Compute(doc);
        var settings = PowerBiSettings.Load();
        var binding = settings.GetBinding(docKey);

        string projectName = "";
        try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }

        if (binding == null)
            return CortexResult<object>.Ok(new
            {
                bound = false,
                docKey,
                projectName,
                message = "No binding found for this document. Run pbi_publish_elements to create one.",
                tip = "Pass workspaceId explicitly on the first publish; subsequent publishes will auto-resolve from the binding."
            });

        return CortexResult<object>.Ok(new
        {
            bound = true,
            docKey,
            projectName,
            workspaceId = binding.WorkspaceId,
            datasetId = binding.DatasetId,
            datasetName = binding.DatasetName,
            documentGuid = binding.DocumentGuid,
            lastPathHash = binding.LastPathHash,
            schemaVersion = binding.SchemaVersion,
            updatedAtUtc = binding.UpdatedAtUtc
        });
    }
}

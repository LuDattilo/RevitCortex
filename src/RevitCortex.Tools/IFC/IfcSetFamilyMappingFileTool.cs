using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Sets the IFC family mapping file path in the session store.
/// Subsequent ifc_export_basic and ifc_export_with_configuration calls
/// will use this mapping file automatically.
/// </summary>
[ToolSafety(true, false)]
public class IfcSetFamilyMappingFileTool : ICortexTool
{
    public string Name => "ifc_set_family_mapping_file";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Set the family mapping file for IFC exports (persists in session)";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (filePath == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to a .txt family mapping file, or empty string to clear");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            session.Store.Set("ifc_family_mapping_file", "");
            return CortexResult<object>.Ok(new
            {
                action = "cleared",
                message = "Family mapping file cleared from session",
            });
        }

        // H25-wave: the stored path is later read by the IFC export tools; restrict it
        // to user-owned directories like every other caller-supplied file path.
        if (!PathSafety.TryResolveSafe(filePath, out var safePath, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        filePath = safePath;

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".txt")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Expected .txt file, got: {ext}",
                suggestion: "The family mapping file must be a .txt file");

        session.Store.Set("ifc_family_mapping_file", filePath);

        return CortexResult<object>.Ok(new
        {
            action = "set",
            filePath,
            message = "Family mapping file set. Subsequent IFC exports will use this mapping.",
        });
    }
}

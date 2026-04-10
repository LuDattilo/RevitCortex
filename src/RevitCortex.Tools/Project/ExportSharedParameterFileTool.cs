using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Exports the shared parameter file contents as structured data or to a file path.
/// </summary>
public class ExportSharedParameterFileTool : ICortexTool
{
    public string Name => "export_shared_parameter_file";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var filePath = input["filePath"]?.Value<string>();

        try
        {
            var app = doc.Application;
            var spFile = app.OpenSharedParameterFile();

            if (spFile == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "No shared parameter file is set",
                    suggestion: "Set a shared parameter file in Revit settings");

            var groups = new List<object>();
            foreach (DefinitionGroup group in spFile.Groups)
            {
                var parameters = new List<object>();
                foreach (ExternalDefinition def in group.Definitions)
                {
                    parameters.Add(new
                    {
                        name = def.Name,
                        guid = def.GUID.ToString(),
#if REVIT2024_OR_GREATER
                        dataType = def.GetDataType().TypeId,
#else
                        dataType = def.ParameterType.ToString(),
#endif
                        description = def.Description,
                        visible = def.Visible
                    });
                }
                groups.Add(new { groupName = group.Name, parameterCount = parameters.Count, parameters });
            }

            // Copy file if path requested
            if (!string.IsNullOrEmpty(filePath))
            {
                var sourceFile = app.SharedParametersFilename;
                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, filePath, true);
                    return CortexResult<object>.Ok(new
                    {
                        exportedTo = filePath,
                        sourceFile,
                        groupCount = groups.Count,
                        groups
                    });
                }
            }

            return CortexResult<object>.Ok(new
            {
                sourceFile = app.SharedParametersFilename,
                groupCount = groups.Count,
                groups
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

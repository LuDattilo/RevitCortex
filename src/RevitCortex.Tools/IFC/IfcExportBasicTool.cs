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
/// Exports the active document to IFC using standard IFCExportOptions.
/// </summary>
public class IfcExportBasicTool : ICortexTool
{
    public string Name => "ifc_export_basic";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Export the active Revit document to IFC with standard options";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "outputDirectory is required");

        if (!Directory.Exists(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Output directory does not exist: {outputDirectory}",
                suggestion: "Create the directory first or provide an existing path");

        var fileName = input["fileName"]?.Value<string>() ?? "";
        var fileVersionStr = input["fileVersion"]?.Value<string>() ?? "IFC4RV";
        var filterViewIdRaw = input["filterViewId"]?.Value<long>();
        var exportBaseQuantities = input["exportBaseQuantities"]?.Value<bool>() ?? false;
        var wallAndColumnSplitting = input["wallAndColumnSplitting"]?.Value<bool>() ?? false;
        var spaceBoundaryLevel = input["spaceBoundaryLevel"]?.Value<int>() ?? 0;

        if (!Enum.TryParse<IFCVersion>(fileVersionStr, ignoreCase: true, out var fileVersion))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown IFC version: {fileVersionStr}",
                suggestion: "Use: Default, IFC2x2, IFC2x3, IFC2x3CV2, IFC4, IFC4RV, IFC4DTV, IFC4x3");

        if (!session.RequestConfirmation("export IFC", 1, $"Export to {outputDirectory}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var options = new IFCExportOptions
            {
                FileVersion = fileVersion,
                ExportBaseQuantities = exportBaseQuantities,
                WallAndColumnSplitting = wallAndColumnSplitting,
                SpaceBoundaryLevel = spaceBoundaryLevel,
            };

            if (filterViewIdRaw.HasValue)
                options.FilterViewId = ToolHelpers.ToElementId(filterViewIdRaw.Value);

            var mappingFile = session.Store.Get<string>("ifc_family_mapping_file");
            if (!string.IsNullOrWhiteSpace(mappingFile) && File.Exists(mappingFile))
                options.FamilyMappingFile = mappingFile;

            using var tx = new Transaction(doc!, "RevitCortex: Export IFC");
            tx.Start();
            var success = doc!.Export(outputDirectory, fileName, options);
            tx.Commit();

            var actualFileName = string.IsNullOrEmpty(fileName) ? doc.Title : fileName;

            return CortexResult<object>.Ok(new
            {
                success,
                outputDirectory,
                fileName = actualFileName + ".ifc",
                fileVersion = fileVersionStr,
                exportBaseQuantities,
                wallAndColumnSplitting,
                spaceBoundaryLevel,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"IFC export failed: {ex.Message}");
        }
    }
}

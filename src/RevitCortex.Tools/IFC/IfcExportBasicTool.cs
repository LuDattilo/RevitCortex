using System;
using System.Collections.Generic;
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
[ToolSafety(true, false)]
public class IfcExportBasicTool : ICortexTool, ICommandTimeoutTool
{
    public string Name => "ifc_export_basic";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Export the active Revit document to IFC with standard options";
    public int CommandTimeoutSeconds => 900;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "outputDirectory is required");

        // H25-wave: restrict writes to user-owned directories; reject traversal/UNC/system paths.
        if (!PathSafety.TryResolveSafe(outputDirectory, out var safeOutputDirectory, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        outputDirectory = safeOutputDirectory;

        if (!Directory.Exists(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Output directory does not exist: {outputDirectory}",
                suggestion: "Create the directory first or provide an existing path");

        var requestedFileName = input["fileName"]?.Value<string>() ?? "";
        var fileName = NormalizeIfcFileName(requestedFileName, doc!.Title);
        var fileVersionStr = input["fileVersion"]?.Value<string>() ?? "IFC4RV";
        var filterViewIdRaw = input["filterViewId"]?.Value<long>();
        var exportBaseQuantities = input["exportBaseQuantities"]?.Value<bool>() ?? false;
        var wallAndColumnSplitting = input["wallAndColumnSplitting"]?.Value<bool>() ?? false;
        var spaceBoundaryLevel = input["spaceBoundaryLevel"]?.Value<int>() ?? 0;
        var overrides = input["overrides"]?.ToObject<Dictionary<string, string>>();

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

            // Free-form IFC export options (ExportInternalRevitPropertySets,
            // ExportIFCCommonPropertySets, Export2DElements, VisibleElementsOfViewExport,
            // ExportRoomsInView, ActivePhaseId, site placement, tessellation level, etc.).
            if (overrides != null)
            {
                foreach (var kvp in overrides)
                    options.AddOption(kvp.Key, kvp.Value);
            }

            var mappingFile = session.Store.Get<string>("ifc_family_mapping_file");
            if (!string.IsNullOrWhiteSpace(mappingFile) && File.Exists(mappingFile))
                options.FamilyMappingFile = mappingFile;

            using var tx = new Transaction(doc!, "RevitCortex: Export IFC");
            tx.Start();
            var exportResult = doc!.Export(outputDirectory, fileName, options);
            tx.Commit();

            var outputPath = Path.Combine(outputDirectory, fileName + ".ifc");

            if (!exportResult)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    $"Revit export returned false for {outputPath}",
                    suggestion: "Check that the document contains exportable elements and the output path is writable");

            if (!File.Exists(outputPath))
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    $"Export reported success but file was not written: {outputPath}",
                    suggestion: "Check disk space, permissions, and antivirus exclusions for the output directory");

            var fileInfo = new FileInfo(outputPath);

            return CortexResult<object>.Ok(new
            {
                outputDirectory,
                fileName = fileName + ".ifc",
                outputPath,
                fileSizeBytes = fileInfo.Length,
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

    private static string NormalizeIfcFileName(string fileName, string documentTitle)
    {
        var effectiveName = string.IsNullOrWhiteSpace(fileName) ? documentTitle : fileName.Trim();
        var extension = Path.GetExtension(effectiveName);
        if (extension.Equals(".ifc", StringComparison.OrdinalIgnoreCase))
            effectiveName = Path.GetFileNameWithoutExtension(effectiveName);

        return effectiveName;
    }
}

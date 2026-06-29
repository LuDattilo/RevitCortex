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
/// Exports to IFC using a named configuration. Configurations are predefined
/// sets of options that map to common IFC MVDs. Extra key-value overrides
/// are passed via IFCExportOptions.AddOption().
/// </summary>
[ToolSafety(true, false)]
public class IfcExportWithConfigurationTool : ICortexTool, ICommandTimeoutTool
{
    public string Name => "ifc_export_with_configuration";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Export to IFC using a named configuration with optional overrides";
    public int CommandTimeoutSeconds => 900;

    private static readonly Dictionary<string, Dictionary<string, string>> BuiltInConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new()
        {
            { "IFCVersion", "IFC4RV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC4 Design Transfer View"] = new()
        {
            { "IFCVersion", "IFC4DTV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "1" },
            { "WallAndColumnSplitting", "true" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC2x3 Coordination View 2.0"] = new()
        {
            { "IFCVersion", "IFC2x3CV2" },
            { "ExportBaseQuantities", "false" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
        ["IFC2x3 COBie 2.4"] = new()
        {
            { "IFCVersion", "IFCCOBIE" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "2" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "true" },
            { "ExportSchedulesAsPsets", "true" },
        },
        ["IFC4x3"] = new()
        {
            { "IFCVersion", "IFC4x3" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
        },
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "outputDirectory is required");

        // H25-wave: restrict writes to user-owned directories; reject traversal/UNC/system paths.
        if (!PathSafety.TryResolveSafe(outputDirectory, out var safeOutputDirectory, out var pathError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        outputDirectory = safeOutputDirectory;

        if (!Directory.Exists(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Output directory does not exist: {outputDirectory}");

        var requestedFileName = input["fileName"]?.Value<string>() ?? "";
        var fileName = NormalizeIfcFileName(requestedFileName, doc!.Title);
        var configName = input["configurationName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(configName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "configurationName is required",
                suggestion: $"Available: {string.Join(", ", BuiltInConfigs.Keys)}");

        if (!BuiltInConfigs.TryGetValue(configName!, out var configOptions))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Configuration '{configName}' not found",
                suggestion: $"Available: {string.Join(", ", BuiltInConfigs.Keys)}");

        var filterViewIdRaw = input["filterViewId"]?.Value<long>();
        var overrides = input["overrides"]?.ToObject<Dictionary<string, string>>();

        if (!session.RequestConfirmation("export IFC", 1,
            $"Export with config '{configName}' to {outputDirectory}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var versionStr = configOptions.TryGetValue("IFCVersion", out var v) ? v : "IFC4RV";
            Enum.TryParse<IFCVersion>(versionStr, ignoreCase: true, out var fileVersion);

            var options = new IFCExportOptions { FileVersion = fileVersion };

            if (filterViewIdRaw.HasValue)
                options.FilterViewId = ToolHelpers.ToElementId(filterViewIdRaw.Value);

            if (configOptions.TryGetValue("ExportBaseQuantities", out var ebq))
                options.ExportBaseQuantities = ebq.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (configOptions.TryGetValue("WallAndColumnSplitting", out var wcs))
                options.WallAndColumnSplitting = wcs.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (configOptions.TryGetValue("SpaceBoundaries", out var sb) && int.TryParse(sb, out var sbLevel))
                options.SpaceBoundaryLevel = sbLevel;

            foreach (var kvp in configOptions)
            {
                if (kvp.Key == "IFCVersion") continue;
                options.AddOption(kvp.Key, kvp.Value);
            }

            if (overrides != null)
            {
                foreach (var kvp in overrides)
                    options.AddOption(kvp.Key, kvp.Value);
            }

            var mappingFile = session.Store.Get<string>("ifc_family_mapping_file");
            if (!string.IsNullOrWhiteSpace(mappingFile) && File.Exists(mappingFile))
                options.FamilyMappingFile = mappingFile;

            using var tx = new Transaction(doc!, "RevitCortex: Export IFC (configured)");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var exportResult = doc!.Export(outputDirectory, fileName, options);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

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
                configurationName = configName,
                outputDirectory,
                fileName = fileName + ".ifc",
                outputPath,
                fileSizeBytes = fileInfo.Length,
                fileVersion = fileVersion.ToString(),
                overridesApplied = overrides?.Count ?? 0,
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

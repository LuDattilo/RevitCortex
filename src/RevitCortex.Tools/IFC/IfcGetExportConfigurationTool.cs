using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Returns the full details of a specific IFC export configuration.
/// </summary>
[ToolSafety(true, false)]
public class IfcGetExportConfigurationTool : ICortexTool
{
    public string Name => "ifc_get_export_configuration";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Get details of a specific IFC export configuration";

    private static readonly Dictionary<string, Dictionary<string, string>> ConfigDetails = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new()
        {
            { "IFCVersion", "IFC4RV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC4 Design Transfer View"] = new()
        {
            { "IFCVersion", "IFC4DTV" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "1" },
            { "WallAndColumnSplitting", "true" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC2x3 Coordination View 2.0"] = new()
        {
            { "IFCVersion", "IFC2x3CV2" },
            { "ExportBaseQuantities", "false" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
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
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "true" },
            { "UseActiveViewGeometry", "false" },
        },
        ["IFC4x3"] = new()
        {
            { "IFCVersion", "IFC4x3" },
            { "ExportBaseQuantities", "true" },
            { "SpaceBoundaries", "0" },
            { "WallAndColumnSplitting", "false" },
            { "ExportIFCCommonPropertySets", "true" },
            { "ExportInternalRevitPropertySets", "false" },
            { "Export2DElements", "false" },
            { "ExportRoomsInView", "false" },
            { "UseActiveViewGeometry", "false" },
        },
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var configName = input["configurationName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(configName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "configurationName is required",
                suggestion: "Use ifc_list_export_configurations to see available names");

        if (!ConfigDetails.TryGetValue(configName!, out var options))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Configuration '{configName}' not found",
                suggestion: $"Available: {string.Join(", ", ConfigDetails.Keys)}");

        if (!IfcListExportConfigurationsTool.Configurations.TryGetValue(configName!, out var info))
            info = new IfcListExportConfigurationsTool.ConfigInfo("unknown", "");

        return CortexResult<object>.Ok(new
        {
            name = configName,
            ifcVersion = info.Version,
            description = info.Description,
            options,
        });
    }
}

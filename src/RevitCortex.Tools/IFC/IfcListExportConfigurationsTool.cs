using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Lists available IFC export configurations (built-in presets).
/// </summary>
public class IfcListExportConfigurationsTool : ICortexTool
{
    public string Name => "ifc_list_export_configurations";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "List available IFC export configurations";

    internal static readonly Dictionary<string, ConfigInfo> Configurations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IFC4 Reference View"] = new("IFC4RV", "IFC4 Reference View — lightweight geometry for coordination and reference"),
        ["IFC4 Design Transfer View"] = new("IFC4DTV", "IFC4 Design Transfer View — full parametric data for design handoff"),
        ["IFC2x3 Coordination View 2.0"] = new("IFC2x3CV2", "IFC 2x3 CV2 — widely supported legacy format"),
        ["IFC2x3 COBie 2.4"] = new("IFCCOBIE", "IFC 2x3 COBie — facility management handover"),
        ["IFC4x3"] = new("IFC4x3", "IFC 4x3 — latest standard with infrastructure support"),
    };

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var configs = Configurations.Select(kvp => new
        {
            name = kvp.Key,
            ifcVersion = kvp.Value.Version,
            description = kvp.Value.Description,
        }).ToList();

        return CortexResult<object>.Ok(new
        {
            count = configs.Count,
            configurations = configs,
        });
    }

    internal class ConfigInfo
    {
        public string Version { get; }
        public string Description { get; }

        public ConfigInfo(string version, string description)
        {
            Version = version;
            Description = description;
        }
    }
}

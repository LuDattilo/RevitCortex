using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reports IFC capabilities: supported versions, available actions, and
/// whether the open-source revit-ifc add-in is installed.
/// </summary>
public class IfcGetCapabilitiesTool : ICortexTool
{
    public string Name => "ifc_get_capabilities";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Get IFC capabilities: supported versions, import/export availability, revit-ifc add-in detection";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var supportedExportVersions = new List<string>();
        foreach (var v in Enum.GetValues(typeof(IFCVersion)))
        {
            if ((int)v > 0)
                supportedExportVersions.Add(v.ToString()!);
        }

        var revitIfcAddinInstalled = DetectRevitIfcAddin();

        var capabilities = new
        {
            supportedExportVersions,
            supportedImportActions = new[] { "open", "link" },
            supportedImportIntents = new[] { "reference", "parametric" },
            revitIfcAddinInstalled,
            canExport = true,
            canImport = true,
            canLink = true,
        };

        return CortexResult<object>.Ok(capabilities);
    }

    private static bool DetectRevitIfcAddin()
    {
        try
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Any(a => a.GetName().Name != null &&
                          a.GetName().Name!.StartsWith("IFCExporter", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}

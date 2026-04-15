using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Variables injected into every script executed by send_code_to_revit.
/// Property names are lowercase to match CLAUDE.md conventions.
/// Works on both net48 (CodeDom) and net8+ (Roslyn).
/// </summary>
public class ScriptGlobals
{
    public Document document { get; set; } = null!;
    public UIDocument uiDocument { get; set; } = null!;
    public Autodesk.Revit.ApplicationServices.Application app { get; set; } = null!;
}

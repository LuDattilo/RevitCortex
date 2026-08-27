using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// Variables injected into every script executed by send_code_to_revit.
/// Property names are lowercase to match CLAUDE.md conventions.
/// Used by the .NET 10 Roslyn execution path.
/// </summary>
public class ScriptGlobals
{
    public Document document { get; set; } = null!;
    public UIDocument uiDocument { get; set; } = null!;
    public Autodesk.Revit.ApplicationServices.Application app { get; set; } = null!;
}

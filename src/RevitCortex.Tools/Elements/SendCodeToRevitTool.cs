using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit context.
/// Uses Roslyn Scripting on Revit 2025+ (net8), CSharpCodeProvider on Revit 2023/2024 (net48).
/// </summary>
public class SendCodeToRevitTool : ICortexTool
{
    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Execute custom C# code in the Revit context. Globals: document (Document), uiDocument (UIDocument), app (Application). Auto-imports: System, System.Linq, Autodesk.Revit.DB, Autodesk.Revit.UI. On Revit 2023/2024 use explicit 'return' statements.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Sandbox validation: block prohibited namespace patterns
        var sandboxResult = CodeSandbox.Validate(code);
        if (sandboxResult != null)
            return sandboxResult;

        // Build globals from session
        var uiApp = session.Store.Get<object>("uiApplication") as Autodesk.Revit.UI.UIApplication;
        var uiDoc = uiApp?.ActiveUIDocument;

        if (uiDoc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "UIApplication not available in session");

        var globals = new ScriptGlobals
        {
            document = doc,
            uiDocument = uiDoc,
            app = uiApp!.Application
        };

#if REVIT2025_OR_GREATER
        return RoslynExecutor.Execute(code, globals, transactionMode);
#else
        return CodeDomExecutor.Execute(code, globals, transactionMode);
#endif
    }
}

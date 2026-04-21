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
/// Uses Roslyn (net8) on Revit 2025+, CSharpCodeProvider (net48) on Revit 2023/2024.
/// HARD-GATED by CortexSettings.EnableCodeExecution — default false.
/// Every invocation is recorded via AuditLogger.
/// </summary>
public class SendCodeToRevitTool : ICortexTool
{
    private static readonly AuditLogger _audit = new AuditLogger();

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Execute custom C# code in the Revit context. Globals: document (Document), uiDocument (UIDocument), app (Application). Auto-imports: System, System.Linq, Autodesk.Revit.DB, Autodesk.Revit.UI. On Revit 2023/2024 use explicit 'return' statements. REQUIRES EnableCodeExecution=true in ~/.revitcortex/settings.json.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Gate 1: settings flag must be explicitly enabled
        var settings = CortexSettings.Load();
        if (!settings.EnableCodeExecution)
        {
            _audit.Log(Name, "BLOCKED: EnableCodeExecution=false", success: false,
                errorCode: CortexErrorCode.PermissionDenied);
            return CortexResult<object>.Fail(
                CortexErrorCode.PermissionDenied,
                "send_code_to_revit is disabled. This tool compiles and executes arbitrary C# against the live Revit document — enable only if you trust the caller. Set \"EnableCodeExecution\": true in ~/.revitcortex/settings.json or use the Revit plugin Settings > Tools page.",
                suggestion: "Edit ~/.revitcortex/settings.json and set EnableCodeExecution to true.");
        }

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Gate 2: sandbox validation on cleaned code (comments/strings stripped first)
        var sandboxResult = CodeSandbox.Validate(code!);
        if (sandboxResult != null)
        {
            _audit.Log(Name, "BLOCKED: sandbox violation", success: false,
                errorCode: CortexErrorCode.PermissionDenied);
            return sandboxResult;
        }

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

        CortexResult<object> result;
#if REVIT2025_OR_GREATER
        result = RoslynExecutor.Execute(code!, globals, transactionMode);
#else
        result = CodeDomExecutor.Execute(code!, globals, transactionMode);
#endif

        // Audit: always log code execution attempts with a bounded summary of the code
        var summaryLen = System.Math.Min(code!.Length, 200);
        _audit.Log(Name, $"code[{code.Length}ch]: {code.Substring(0, summaryLen)}",
            success: result.Success, errorCode: result.Error?.Code);

        return result;
    }
}

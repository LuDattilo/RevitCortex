using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit context.
/// Uses Roslyn (net8) on Revit 2025+, CSharpCodeProvider (net48) on Revit 2023/2024.
/// HARD-GATED by CortexSettings.EnableCodeExecution — default false.
/// Every invocation is recorded via AuditLogger.
/// Scripts are persisted to ~/.revitcortex/scripts/ and cleaned up at Revit shutdown
/// unless marked as reusable.
/// </summary>
public class SendCodeToRevitTool : ICortexTool
{
    private static readonly AuditLogger _audit = new AuditLogger();

    public static readonly string ScriptsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "scripts");

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Execute custom C# code in the Revit context. Use ONLY when no dedicated tool covers the task — prefer specific tools always. Globals: document (Document), uiDocument (UIDocument), app (Application). REQUIRES EnableCodeExecution=true in ~/.revitcortex/settings.json.";

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
                "send_code_to_revit is disabled. Set \"EnableCodeExecution\": true in ~/.revitcortex/settings.json or via Settings > Tools.",
                suggestion: "Edit ~/.revitcortex/settings.json and set EnableCodeExecution to true.");
        }

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";
        var reusable = input["reusable"]?.Value<bool>() ?? false;
        var scriptName = SanitizeName(input["scriptName"]?.Value<string>() ?? "script");

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Gate 2: sandbox validation
        var sandboxResult = CodeSandbox.Validate(code!);
        if (sandboxResult != null)
        {
            _audit.Log(Name, "BLOCKED: sandbox violation", success: false,
                errorCode: CortexErrorCode.PermissionDenied);
            return sandboxResult;
        }

        // Gate 3: explicit user confirmation before any script execution
        if (!session.RequestConfirmation("execute C# script", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Script execution cancelled by user");

        // Persist script to ~/.revitcortex/scripts/
        var scriptPath = PersistScript(code!, scriptName, reusable);

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

        // Audit: always log code execution attempts
        var summaryLen = System.Math.Min(code!.Length, 200);
        _audit.Log(Name, $"code[{code.Length}ch]: {code.Substring(0, summaryLen)}",
            success: result.Success, errorCode: result.Error?.Code);

        // Attach script path to result so the caller knows where it was saved
        if (result.Success && result.Data is not null)
        {
            var data = Newtonsoft.Json.Linq.JObject.FromObject(result.Data);
            data["scriptSavedTo"] = scriptPath;
            data["scriptLifetime"] = reusable ? "REUSABLE" : "TEMP (deleted at Revit close)";
            return CortexResult<object>.Ok(data);
        }

        return result;
    }

    /// <summary>
    /// Saves the script to ~/.revitcortex/scripts/ with a TEMP or REUSABLE header.
    /// Returns the full path of the saved file.
    /// </summary>
    private static string PersistScript(string code, string scriptName, bool reusable)
    {
        try
        {
            Directory.CreateDirectory(ScriptsFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{stamp}_{scriptName}.cs";
            var filePath = Path.Combine(ScriptsFolder, fileName);

            var lifetime = reusable ? "REUSABLE" : "TEMP";
            var header =
                $"// {lifetime}\n" +
                $"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"// Name: {scriptName}\n" +
                "// =============================================\n";

            File.WriteAllText(filePath, header + code);
            return filePath;
        }
        catch
        {
            return "(could not save script)";
        }
    }

    /// <summary>Removes all TEMP scripts from ~/.revitcortex/scripts/.</summary>
    public static void CleanupTempScripts()
    {
        if (!Directory.Exists(ScriptsFolder)) return;
        foreach (var file in Directory.GetFiles(ScriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
            catch { }
        }
    }

    private static string SanitizeName(string name)
    {
        var safe = Regex.Replace(name, @"[^\w\-]", "-").Trim('-');
        return safe.Length == 0 ? "script" : safe.Substring(0, Math.Min(safe.Length, 40));
    }
}

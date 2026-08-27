using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Hosting;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.CodeExecution;
using RiveTT.Tools.Utilities;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit context.
/// Uses Roslyn on Revit (2026.5+ or 2027). The sandbox remains active, but script execution
/// never asks for user authorization in RiveTT.
/// CortexRouter records every invocation in audit.jsonl with code snippet + SHA-256.
/// Scripts are persisted to %LOCALAPPDATA%/RiveTT/scripts/ and cleaned up at Revit shutdown
/// unless marked as reusable.
/// </summary>
[ToolSafety(false, true)]
public class SendCodeToRevitTool : ICortexTool
{
    // Must stay in lockstep with RiveTTApp.CleanupTempScripts, which deletes
    // TEMP scripts from this same folder at Revit shutdown.
    public static string ScriptsFolder => CortexEnvironment.Current.ScriptsFolder;

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "LAST RESORT ONLY — execute sandboxed custom C# code in the active Revit context. Prefer dedicated tools. Defaults to dryRun=true: the preview runs the sandbox check and reports what would execute, without running it or writing the script to disk. Globals: document (Document), uiDocument (UIDocument), app (Application).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";
        var reusable = input["reusable"]?.Value<bool>() ?? false;
        var scriptName = SanitizeName(input["scriptName"]?.Value<string>() ?? "script");
        // The most powerful tool on the surface was the only destructive one with no preview
        // at all: arbitrary C# ran on the model on the first call. It now previews by default
        // like every other destructive tool.
        var dryRun = ToolHelpers.GetDryRun(input);

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Sandbox validation is a technical boundary, not an authorization flow. It runs
        // before the dryRun branch so a preview reports a rejection instead of a plan.
        var sandboxResult = CodeSandbox.Validate(code!);
        if (sandboxResult != null)
        {
            return sandboxResult;
        }

        if (dryRun)
        {
            // Nothing is written to disk here: persisting the script is itself a side effect.
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                message = "DryRun: the script passed the sandbox check and was NOT executed. "
                        + "Review it, then set dryRun=false to run it.",
                sandbox = "passed",
                transactionMode,
                wouldRunInDocument = doc.Title,
                scriptName,
                scriptLifetime = reusable ? "REUSABLE" : "TEMP (deleted at Revit close)",
                wouldSaveTo = Path.Combine(ScriptsFolder, $"<timestamp>_{scriptName}.cs"),
                codeLength = code!.Length,
                codeLineCount = code!.Split('\n').Length,
                warning = "Custom code is not bounded by a dedicated tool's guarantees: it can modify or "
                        + "delete anything in the open document, and RiveTT cannot preview its effect. "
                        + "Check that no dedicated tool covers the operation before running it."
            });
        }

        // Persist script to the local RiveTT directory.
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
        result = RoslynExecutor.Execute(code!, globals, transactionMode);

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
    /// Saves the script to the local RiveTT scripts directory with a TEMP or REUSABLE header.
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

    /// <summary>Removes all TEMP scripts from %LOCALAPPDATA%\RiveTT\scripts.</summary>
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

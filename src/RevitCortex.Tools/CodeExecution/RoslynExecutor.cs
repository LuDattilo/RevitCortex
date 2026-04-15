#if REVIT2025_OR_GREATER
using System;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code snippets inside the Revit process using Roslyn Scripting API.
/// </summary>
public static class RoslynExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] AutoImports = new[]
    {
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "Autodesk.Revit.DB",
        "Autodesk.Revit.UI",
    };

    public static CortexResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto")
    {
        try
        {
            var options = ScriptOptions.Default
                .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)))
                .AddImports(AutoImports);

            object? result;

            if (transactionMode == "none")
            {
                result = ExecuteScript(code, options, globals);
            }
            else
            {
                using var tx = new Transaction(globals.document, "RevitCortex: Script");
                tx.Start();
                try
                {
                    result = ExecuteScript(code, options, globals);
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    throw;
                }
            }

            return CortexResult<object>.Ok(SerializeResult(result));
        }
        catch (CompilationErrorException ex)
        {
            var errors = string.Join("\n", ex.Diagnostics.Select(d => d.ToString()));
            return CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                $"Compilation error:\n{errors}",
                suggestion: "Check C# syntax. Globals: document (Document), uiDocument (UIDocument), app (Application). Auto-imports: System, System.Linq, Autodesk.Revit.DB, Autodesk.Revit.UI.");
        }
        catch (OperationCanceledException)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Timeout,
                $"Script execution timed out after {DefaultTimeout.TotalSeconds}s");
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException?.Message ?? ex.Message;
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Runtime error: {innerMsg}",
                suggestion: "Check variable names, null references, and Revit API usage.");
        }
    }

    private static object? ExecuteScript(string code, ScriptOptions options, ScriptGlobals globals)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var task = CSharpScript.EvaluateAsync<object?>(
            code, options, globals, typeof(ScriptGlobals), cts.Token);
        return task.GetAwaiter().GetResult();
    }

    private static object SerializeResult(object? result)
    {
        if (result == null)
            return new { result = (object?)null };

        if (result is string or int or long or double or float or bool or decimal)
            return new { result };

        if (result is Element elem)
        {
#if REVIT2024_OR_GREATER
            var id = elem.Id.Value;
#else
            var id = elem.Id.IntegerValue;
#endif
            return new { elementId = id, name = elem.Name, category = elem.Category?.Name };
        }

        try
        {
            var json = JsonConvert.SerializeObject(result, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = 5,
                Error = (sender, args) => args.ErrorContext.Handled = true
            });
            return JToken.Parse(json);
        }
        catch
        {
            return new { result = result.ToString() };
        }
    }
}
#endif

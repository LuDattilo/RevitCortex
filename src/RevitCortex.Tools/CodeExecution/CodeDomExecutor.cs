// CodeDom executor for .NET Framework 4.8 (Revit 2023/2024)
// On net8+ builds, Roslyn is used instead — this file is excluded via #if.
#if !REVIT2025_OR_GREATER
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Microsoft.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code using CSharpCodeProvider (CodeDom).
/// Fallback for Revit 2023/2024 where Roslyn Scripting is not available.
/// </summary>
public static class CodeDomExecutor
{
    public static CortexResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto")
    {
        try
        {
            // Wrap user code in a class with a static method
            var wrappedCode = WrapCode(code);

            // Compile
            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                IncludeDebugInformation = false,
            };

            // Add references from loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                        parameters.ReferencedAssemblies.Add(asm.Location);
                }
                catch { }
            }

            var compileResult = provider.CompileAssemblyFromSource(parameters, wrappedCode);

            if (compileResult.Errors.HasErrors)
            {
                var errors = new List<string>();
                foreach (CompilerError err in compileResult.Errors)
                {
                    if (!err.IsWarning)
                        errors.Add($"Line {err.Line - CountPrefixLines()}: {err.ErrorText}");
                }
                return CortexResult<object>.Fail(
                    CortexErrorCode.InvalidInput,
                    $"Compilation error:\n{string.Join("\n", errors)}",
                    suggestion: "Check C# syntax. Globals: document (Document), uiDocument (UIDocument), app (Application).");
            }

            // Execute
            var assembly = compileResult.CompiledAssembly;
            var type = assembly.GetType("RevitCortex.DynamicScript.ScriptRunner");
            var method = type!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

            object result;

            if (transactionMode == "none")
            {
                result = method!.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
            }
            else if (transactionMode == "group")
            {
                using var txGroup = new TransactionGroup(globals.document, "RevitCortex: Script Group");
                txGroup.Start();
                try
                {
                    result = method!.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
                    if (txGroup.GetStatus() == TransactionStatus.Started)
                        txGroup.Assimilate();
                }
                catch
                {
                    if (txGroup.GetStatus() == TransactionStatus.Started)
                        txGroup.RollBack();
                    throw;
                }
            }
            else
            {
                using var tx = new Transaction(globals.document, "RevitCortex: Script");
                tx.Start();
                try
                {
                    result = method!.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
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
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Runtime error: {ex.InnerException}",
                suggestion: "Check variable names, null references, and Revit API usage. Full stack trace above.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Execution error: {ex.Message}");
        }
    }

    private static int CountPrefixLines() => 12; // Number of lines before user code in WrapCode()

    private static string WrapCode(string userCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Autodesk.Revit.DB;");
        sb.AppendLine("using Autodesk.Revit.UI;");
        sb.AppendLine("using Newtonsoft.Json.Linq;");
        sb.AppendLine("namespace RevitCortex.DynamicScript {");
        sb.AppendLine("  public static class ScriptRunner {");
        sb.AppendLine("    public static object Run(");
        sb.AppendLine("      Autodesk.Revit.DB.Document document,");
        sb.AppendLine("      Autodesk.Revit.UI.UIDocument uiDocument,");
        sb.AppendLine("      Autodesk.Revit.ApplicationServices.Application app) {");
        sb.AppendLine(userCode);
        sb.AppendLine("      return null;");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static object SerializeResult(object result)
    {
        if (result == null)
            return new { result = (object)null };

        if (result is string || result is int || result is long || result is double || result is float || result is bool)
            return new { result };

        if (result is Element elem)
            return new { elementId = elem.Id.IntegerValue, name = elem.Name, category = elem.Category?.Name };

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

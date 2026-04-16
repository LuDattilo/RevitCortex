#if REVIT2025_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code snippets inside the Revit process using the Roslyn
/// CSharpCompilation API (no Scripting layer — avoids CreateFromAssemblyInternal conflicts).
/// Requires Revit 2025+ (net8).
/// </summary>
public static class RoslynExecutor
{
    private static readonly int PrefixLines = 12; // lines added by WrapCode before user code

    public static CortexResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto")
    {
        try
        {
            var wrappedCode = WrapCode(code);

            // Collect file-based references — deduplicate by simple name to avoid
            // "assembly already imported" errors when multiple plugins load the same DLL
            var refs = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                    {
                        var name = asm.GetName().Name ?? string.Empty;
                        if (seen.Add(name))
                            refs.Add(MetadataReference.CreateFromFile(asm.Location));
                    }
                }
                catch { }
            }

            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest);

            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, parseOptions);

            var compilation = CSharpCompilation.Create(
                assemblyName: $"RevitCortexScript_{Guid.NewGuid():N}",
                syntaxTrees: new[] { syntaxTree },
                references: refs,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d =>
                    {
                        var line = d.Location.GetLineSpan().StartLinePosition.Line + 1 - PrefixLines;
                        return $"Line {line}: {d.GetMessage()}";
                    });
                return CortexResult<object>.Fail(
                    CortexErrorCode.InvalidInput,
                    $"Compilation error:\n{string.Join("\n", errors)}",
                    suggestion: "Globals: document (Document), uiDocument (UIDocument), app (Application). Use explicit 'return'.");
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            var type = assembly.GetType("RevitCortex.DynamicScript.ScriptRunner")!;
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

            object? result;

            if (transactionMode == "none")
            {
                result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
            }
            else if (transactionMode == "group")
            {
                using var txGroup = new TransactionGroup(globals.document, "RevitCortex: Script Group");
                txGroup.Start();
                try
                {
                    result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
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
                    result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
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

    private static object SerializeResult(object? result)
    {
        if (result == null)
            return new { result = (object?)null };

        if (result is string or int or long or double or float or bool or decimal)
            return new { result };

#if REVIT2024_OR_GREATER
        if (result is Element elem)
            return new { elementId = elem.Id.Value, name = elem.Name, category = elem.Category?.Name };
#else
        if (result is Element elem)
            return new { elementId = elem.Id.IntegerValue, name = elem.Name, category = elem.Category?.Name };
#endif

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

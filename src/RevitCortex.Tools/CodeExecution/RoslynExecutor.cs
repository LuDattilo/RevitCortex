#if REVIT2025_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code snippets inside the Revit process. Requires Revit 2025+ (net8).
///
/// Roslyn isolation: Revit hosts every add-in in ONE shared AssemblyLoadContext, and sibling
/// add-ins ship older System.Collections.Immutable / System.Reflection.Metadata copies. Simple-name
/// binding in the default ALC then hands Microsoft.CodeAnalysis 4.12 the wrong (older) dependency,
/// failing with "Could not load Microsoft.CodeAnalysis 4.12.0.0". To avoid this, the COMPILATION
/// step (the only Roslyn-touching code) is delegated to <see cref="RoslynCompilerWorker"/> loaded
/// into a dedicated <see cref="RoslynLoadContext"/> that resolves Roslyn + its 8.0 deps from OUR
/// plugin folder. This type itself references NO Microsoft.CodeAnalysis types, so the default ALC
/// never loads Roslyn (which would re-introduce the race). The emitted assembly bytes are then
/// loaded and run here in the default ALC, where the script's Revit API types match the live globals.
/// </summary>
public static class RoslynExecutor
{
    private static readonly int PrefixLines = 12; // lines added by WrapCode before user code

    private static MethodInfo? _compileMethod;
    private static readonly object _compileLock = new object();

    public static CortexResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto")
    {
        try
        {
            var wrappedCode = WrapCode(code);
            var referencePaths = GatherReferencePaths();

            // Compile inside the isolated ALC (Roslyn + 8.0 deps resolved from our folder).
            byte[]? assemblyBytes;
            string[] compileErrors;
            try
            {
                var compile = GetCompileMethod();
                var args = new object?[] { wrappedCode, referencePaths.ToArray(), PrefixLines, null };
                assemblyBytes = (byte[]?)compile.Invoke(null, args);
                compileErrors = (string[])args[3]! ?? Array.Empty<string>();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                return CortexResult<object>.Fail(
                    CortexErrorCode.Unknown,
                    $"Roslyn compilation failed: {ex.InnerException.Message}",
                    suggestion: "This is an internal compiler/assembly-loading error, not a problem with your code.");
            }

            if (assemblyBytes == null)
            {
                return CortexResult<object>.Fail(
                    CortexErrorCode.InvalidInput,
                    $"Compilation error:\n{string.Join("\n", compileErrors)}",
                    suggestion: "Globals: document (Document), uiDocument (UIDocument), app (Application). Use explicit 'return'.");
            }

            // Load + run the emitted assembly in the DEFAULT ALC so the script's Revit API
            // types are identical to the live document/globals passed in below.
            var assembly = Assembly.Load(assemblyBytes);
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
                    if (txGroup.GetStatus() == TransactionStatus.Started
                        && txGroup.Assimilate() != TransactionStatus.Committed)
                    {
                        return CortexResult<object>.Fail(
                            CortexErrorCode.TransactionFailed,
                            "Revit rolled back the script transaction group on commit.",
                            suggestion: "The script triggered a Revit error during commit. Fix the reported model errors and retry.");
                    }
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
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                try
                {
                    result = method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app });
                    if (tx.GetStatus() == TransactionStatus.Started
                        && tx.Commit() != TransactionStatus.Committed)
                    {
                        return CortexResult<object>.Fail(
                            CortexErrorCode.TransactionFailed,
                            $"Revit rolled back the script transaction: {TransactionFailureHandling.Describe(txFailures)}",
                            suggestion: "The script triggered a Revit error during commit. Fix the reported model errors and retry.");
                    }
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

    /// <summary>
    /// Lazily creates the isolated Roslyn ALC, loads RevitCortex.Tools into it, and caches the
    /// <see cref="RoslynCompilerWorker.Compile"/> MethodInfo. Invoking it runs the Roslyn
    /// compilation inside that context, where Microsoft.CodeAnalysis + Immutable/Metadata 8.0
    /// bind to our plugin-folder copies instead of a sibling add-in's older versions.
    /// </summary>
    private static MethodInfo GetCompileMethod()
    {
        if (_compileMethod != null)
            return _compileMethod;

        lock (_compileLock)
        {
            if (_compileMethod != null)
                return _compileMethod;

            var dir = Path.GetDirectoryName(typeof(RoslynExecutor).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
                dir = AppContext.BaseDirectory;

            var alc = new RoslynLoadContext(dir!);
            var toolsAsm = alc.LoadFromAssemblyPath(Path.Combine(dir!, "RevitCortex.Tools.dll"));
            var workerType = toolsAsm.GetType("RevitCortex.Tools.CodeExecution.RoslynCompilerWorker", throwOnError: true)!;
            _compileMethod = workerType.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("RoslynCompilerWorker.Compile not found");
            return _compileMethod;
        }
    }

    /// <summary>
    /// AssemblyLoadContext that serves Roslyn (Microsoft.CodeAnalysis*) and its 8.0 dependencies
    /// (System.Collections.Immutable / System.Reflection.Metadata) plus RevitCortex.Tools from the
    /// plugin folder, and defers everything else (Revit API, RevitCortex.Core, the .NET runtime) to
    /// the default ALC so those types stay shared.
    /// </summary>
    private sealed class RoslynLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public RoslynLoadContext(string dir) : base(name: "RevitCortexRoslyn", isCollectible: false)
        {
            _dir = dir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            bool isolate = name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || name == "System.Collections.Immutable"
                || name == "System.Reflection.Metadata"
                || name == "RevitCortex.Tools";

            if (isolate)
            {
                var path = Path.Combine(_dir, name + ".dll");
                if (File.Exists(path))
                    return LoadFromAssemblyPath(path);
            }

            return null; // defer to the default ALC
        }
    }

    private static string WrapCode(string userCode)
    {
        var sb = new System.Text.StringBuilder();
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

    /// <summary>
    /// Collects the file paths of the assemblies the script may reference. Pure string work —
    /// deliberately does NOT touch Microsoft.CodeAnalysis (MetadataReference creation happens in
    /// the isolated worker), so JITing this type never loads Roslyn into the default ALC.
    /// </summary>
    private static List<string> GatherReferencePaths()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddTrustedPlatformAssemblies(paths, seen);

        AddAssemblyPath(paths, seen, typeof(object).Assembly);
        AddAssemblyPath(paths, seen, typeof(Enumerable).Assembly);
        AddAssemblyPath(paths, seen, typeof(List<>).Assembly);
        AddAssemblyPath(paths, seen, typeof(Document).Assembly);
        AddAssemblyPath(paths, seen, typeof(Autodesk.Revit.UI.UIDocument).Assembly);
        AddAssemblyPath(paths, seen, typeof(JObject).Assembly);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldReferenceLoadedAssembly(asm))
                AddAssemblyPath(paths, seen, asm);
        }

        return paths;
    }

    private static void AddTrustedPlatformAssemblies(List<string> paths, HashSet<string> seen)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa))
            return;

        foreach (var path in tpa!.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (!IsFrameworkReference(name))
                continue;

            AddPath(paths, seen, path);
        }
    }

    private static bool IsFrameworkReference(string name)
    {
        return name == "netstandard"
            || name == "Microsoft.CSharp"
            || name == "WindowsBase"
            || name == "PresentationCore"
            || name == "PresentationFramework"
            || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldReferenceLoadedAssembly(Assembly asm)
    {
        try
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                return false;

            var name = asm.GetName().Name ?? string.Empty;
            return name == "RevitAPI"
                || name == "RevitAPIUI"
                || name == "Newtonsoft.Json"
                || name.StartsWith("Autodesk.Revit.", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("RevitCortex.", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AddAssemblyPath(List<string> paths, HashSet<string> seen, Assembly asm)
    {
        try
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                return;

            AddPath(paths, seen, asm.Location);
        }
        catch { }
    }

    private static void AddPath(List<string> paths, HashSet<string> seen, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                return;

            paths.Add(path);
        }
        catch { }
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
            var token = JToken.Parse(json);
            // The result envelope expects a JObject. A script that returns a collection
            // (List/array) parses to a JArray — wrap it so it doesn't fail downstream with
            // "Object serialized to Array. JObject instance expected."
            return token is JObject ? token : new JObject { ["result"] = token };
        }
        catch
        {
            return new { result = result.ToString() };
        }
    }
}
#endif

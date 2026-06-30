#if REVIT2025_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// The Roslyn-touching half of send_code_to_revit compilation. This type is designed to be
/// loaded and invoked inside an isolated <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// (see <c>RoslynExecutor.GetCompileMethod</c>), so Microsoft.CodeAnalysis 4.12 and its
/// System.Collections.Immutable / System.Reflection.Metadata 8.0 dependencies bind to OUR
/// copies in the plugin folder instead of an older version that a sibling Revit add-in may
/// have loaded into the shared default ALC (the cause of
/// "Could not load Microsoft.CodeAnalysis 4.12.0.0").
///
/// IMPORTANT: the public signature uses only framework types (string / string[] / byte[] /
/// int) so it can be invoked by reflection across the ALC boundary without any shared-type
/// identity problems. It must NOT expose Microsoft.CodeAnalysis types. Compilation produces
/// the emitted assembly bytes; the bytes are loaded and run later in the DEFAULT ALC by
/// RoslynExecutor, where the script's Revit API types match the live document globals.
/// </summary>
public static class RoslynCompilerWorker
{
    /// <summary>
    /// Compiles <paramref name="wrappedCode"/> to an in-memory assembly.
    /// Returns the emitted bytes on success (with <paramref name="errors"/> empty), or null
    /// on failure (with <paramref name="errors"/> populated, line numbers already adjusted to
    /// be relative to the user's code via <paramref name="prefixLines"/>).
    /// </summary>
    public static byte[]? Compile(string wrappedCode, string[] referencePaths, int prefixLines, out string[] errors)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);

        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, parseOptions);

        var refs = new List<MetadataReference>(referencePaths.Length);
        foreach (var path in referencePaths)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch { }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "RevitCortexScript_" + Guid.NewGuid().ToString("N"),
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
            errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var line = d.Location.GetLineSpan().StartLinePosition.Line + 1 - prefixLines;
                    return $"Line {line}: {d.GetMessage()}";
                })
                .ToArray();
            return null;
        }

        errors = Array.Empty<string>();
        return ms.ToArray();
    }
}
#endif

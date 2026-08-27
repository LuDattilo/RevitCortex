using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// Locks the parameter TYPES the MCP surface may publish.
///
/// A <c>bool?</c> parameter breaks the whole call: the host answers the generic
/// "An error occurred invoking '&lt;tool&gt;'", the request never reaches Revit,
/// nothing is audited and no transaction runs. Measured live on 0.2.0:
/// list_system_types(category) answered normally while
/// list_system_types(category, includeLoadable: true) — a single added bool? —
/// failed. Nullable long/double/string parameters bind correctly; only the
/// nullable boolean is affected.
///
/// 148 parameters across 110 tools were nullable booleans, including dryRun on
/// fifteen write tools — so "preview before writing" was impossible on those,
/// and the failure looked like a broken tool rather than a type problem. Flags
/// with a documented default are now plain <c>bool</c>; the few whose third
/// state is meaningful travel as a "true"/"false" string through
/// <see cref="RiveTT.Server.Tools.TriStateFlag"/>.
/// </summary>
public class McpParameterTypeContractTests
{
    private static IEnumerable<(MethodInfo Method, string ToolName)> McpTools()
    {
        var assembly = typeof(RiveTT.Server.Tools.ProjectTools).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() == null) continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute == null) continue;
                yield return (method, attribute.Name ?? method.Name);
            }
        }
    }

    [Fact]
    public void NoMcpToolPublishesANullableBooleanParameter()
    {
        var offenders = McpTools()
            .SelectMany(tool => tool.Method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(bool?))
                .Select(parameter => $"{tool.ToolName}.{parameter.Name}"))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Nullable boolean parameters make the tool call fail before it reaches Revit. " +
            "Use a plain bool with the documented default, or TriStateFlag for a real " +
            "three-state flag:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryMcpToolIsDiscoverableAndNamed()
    {
        var tools = McpTools().ToList();

        // Sanity: if reflection stops finding the tools, the test above would pass
        // vacuously and the guard would be worthless. The floor was 250 when the surface
        // still carried the Rebar and StructuralSteel tools; removing those 112 left 196
        // and the floor outlived them, failing the build on a surface that was correct.
        // Keep it well under the real count — this asserts "reflection still works", not
        // a tool budget.
        Assert.True(tools.Count > 150, $"Only {tools.Count} MCP tools discovered by reflection.");
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.ToolName)));
    }

    [Fact]
    public void NoMcpToolPublishesAnOptionalArrayParameter()
    {
        // An OPTIONAL array parameter does not bind either: the call fails before the
        // method runs, exactly like bool?. A REQUIRED array binds fine — measured
        // live: get_element_parameters(elementIds:[...]) works, adding the optional
        // parameterNames:[...] breaks the same call. 55 parameters across 41 tools
        // were in that state, which removed every category filter, id filter and
        // field list from the surface. Optional collections travel as a JSON string
        // through JsonArrayParam instead.
        var offenders = McpTools()
            .SelectMany(tool => tool.Method.GetParameters()
                .Where(parameter => parameter.ParameterType.IsArray && parameter.HasDefaultValue)
                .Select(parameter => $"{tool.ToolName}.{parameter.Name} ({parameter.ParameterType.Name})"))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Optional array parameters make the tool call fail before it reaches Revit. " +
            "Publish them as a JSON string and parse with JsonArrayParam:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryPublishedParameterIsActuallyForwarded()
    {
        // A parameter declared on the signature but never written into the request
        // is invisible dead weight: filter_by_parameter_value published elementIds
        // and forwarded it nowhere. Reflection cannot see the request body, so this
        // reads the source of the declaring type.
        var offenders = new List<string>();

        foreach (var group in McpTools().GroupBy(tool => tool.Method.DeclaringType!))
        {
            var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
                "RiveTT.Server", "Tools", group.Key.Name + ".cs"));
            if (!File.Exists(path)) continue;
            var source = File.ReadAllText(path);

            foreach (var (method, toolName) in group)
            {
                var body = MethodBody(source, method.Name);
                if (body == null) continue;

                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name is null or "revit" or "ct") continue;
                    // Either forwarded by key, or consumed by name in the body.
                    if (body.Contains($"\"{parameter.Name}\"", StringComparison.Ordinal)) continue;
                    if (body.Contains(parameter.Name, StringComparison.Ordinal)) continue;
                    offenders.Add($"{toolName}.{parameter.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "MCP parameters declared but never forwarded to Revit:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>Source text of one method, from its signature to the next method.</summary>
    private static string? MethodBody(string source, string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (start < 0) return null;
        var next = source.IndexOf("[McpServerTool", start, StringComparison.Ordinal);
        return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
    }

    [Fact]
    public void DryRunIsAlwaysForwardedAsAPlainBoolean()
    {
        // dryRun is the safety contract of the whole connector: it must never be
        // the type that silently breaks the call.
        var dryRunParameters = McpTools()
            .SelectMany(tool => tool.Method.GetParameters()
                .Where(parameter => parameter.Name == "dryRun")
                .Select(parameter => (tool.ToolName, parameter)))
            .ToList();

        Assert.NotEmpty(dryRunParameters);
        Assert.All(dryRunParameters, entry => Assert.Equal(typeof(bool), entry.parameter.ParameterType));
    }
}

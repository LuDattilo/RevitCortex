using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RevitCortex.Core.Tools;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Rebar;

/// <summary>
/// Source-text + reflection assertions that the Rebar server wrappers forward the JObject keys
/// the plugin reads, and that wrapper MCP names map to registered plugin tools. This catches the
/// wrapper/plugin name-and-key drift footgun that unit tests of either side miss.
///
/// NOTE on the reflection approach: outside a Revit install the Tools assembly cannot fully load
/// (RevitAPI.dll is reference-only), so <c>Assembly.GetTypes()</c> throws ReflectionTypeLoadException
/// and only a NON-DETERMINISTIC subset of plugin tool types is loadable. We therefore assert only
/// the *direction that is robust*: every rebar tool the runtime CAN load must have a server wrapper
/// (a missing wrapper = an unreachable tool). We do NOT assert exact counts or "every wrapper has a
/// plugin" — those depend on the unloadable subset and would flake. The exhaustive 1:1 check is
/// covered deterministically by the source-text facts below plus the build-time wiring.
/// </summary>
public class RebarServerForwardingSourceTests
{
    private static string ReadRebarTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", "RebarTools.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void CreateRebarFromShape_ForwardsHostAndVectors()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"hostId\"] = hostId", src);
        Assert.Contains("[\"origin\"] = JObject.Parse(origin)", src);
        Assert.Contains("[\"xVec\"] = JObject.Parse(xVec)", src);
        Assert.Contains("[\"yVec\"] = JObject.Parse(yVec)", src);
    }

    [Fact]
    public void SetRebarLayout_ForwardsLayoutObject()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"layout\"] = JObject.Parse(layout)", src);
    }

    [Fact]
    public void CreateAreaReinforcement_ForwardsMajorDirection()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"majorDirection\"] = JObject.Parse(majorDirection)", src);
    }

    [Fact]
    public void CreateRebarCoupler_ForwardsEndDescriptors()
    {
        var src = ReadRebarTools();
        Assert.Contains("create_rebar_coupler", src);
        Assert.Contains("[\"end1\"] = JObject.Parse(end1)", src);
    }

    [Fact]
    public void GetRebarNumbering_ForwardsCapAndSummary()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"maxResults\"] = maxResults", src);
        Assert.Contains("[\"summaryOnly\"] = summaryOnly", src);
    }

    // ── Reflection: every wrapper forwards via the SAME name it declares ──────────
    // This is fully deterministic (it never touches the plugin/Tools assembly).

    [Fact]
    public void EveryWrapper_ForwardsViaItsOwnDeclaredName()
    {
        var src = ReadRebarTools();
        var methods = typeof(RebarTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var m in methods)
        {
            var name = m.GetCustomAttribute<McpServerToolAttribute>()!.Name;
            Assert.False(string.IsNullOrEmpty(name), $"{m.Name} has an empty McpServerTool name");
            // The forwarding call must target the same tool name the attribute declares.
            Assert.True(src.Contains($"ExecuteAsync(\"{name}\""),
                $"Wrapper '{m.Name}' declares MCP name '{name}' but does not forward to ExecuteAsync(\"{name}\", ...)");
        }
    }

    [Fact]
    public void AllWrapperNames_AreUniqueAndSnakeCase()
    {
        var names = typeof(RebarTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

        Assert.NotEmpty(names);
        var dups = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, $"Duplicate wrapper names: {string.Join(", ", dups)}");
        foreach (var n in names) Assert.Matches("^[a-z][a-z0-9_]*$", n);
    }

    // ── Reflection: loadable rebar plugin tools must each have a wrapper ──────────
    // Robust direction only (see class note). The loadable set is a subset outside Revit,
    // so this catches a wrapper that was forgotten for a tool that DID load, without
    // flaking on tools whose Revit-typed metadata can't resolve in CI.

    [Fact]
    public void LoadablePluginRebarTools_AllHaveAWrapper()
    {
        var wrappers = typeof(RebarTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        Assembly toolsAsm = typeof(RevitCortex.Tools.Meta.SayHelloTool).Assembly;
        IEnumerable<Type> types;
        try { types = toolsAsm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null)!; }

        var loadableRebarNames = new List<string>();
        foreach (var t in types.Where(t => t.IsClass && !t.IsAbstract && typeof(ICortexTool).IsAssignableFrom(t)))
        {
            ICortexTool inst;
            try { inst = (ICortexTool)Activator.CreateInstance(t)!; }
            catch { continue; } // a tool whose ctor needs Revit can't be inspected here; skip
            if (inst.Category == "Rebar") loadableRebarNames.Add(inst.Name);
        }

        var missing = loadableRebarNames.Where(p => !wrappers.Contains(p)).ToList();
        Assert.True(missing.Count == 0,
            $"These loadable rebar plugin tools have no server wrapper (unreachable via MCP): {string.Join(", ", missing)}");
    }
}

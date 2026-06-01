using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RevitCortex.Core.Tools;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

/// <summary>
/// Source-text + reflection facts about the StructuralSteel server wrappers. Mirrors
/// RebarServerForwardingSourceTests.
///
/// CRITICAL (learned from rebar): outside a Revit install, GetTypes() on the Tools assembly
/// throws ReflectionTypeLoadException and the loadable subset is NON-DETERMINISTIC. So we do
/// NOT assert an exact wrapper<->plugin count. We assert only robust directions:
///   - every wrapper forwards via ITS OWN declared name,
///   - wrapper names are unique + snake_case,
///   - every LOADABLE steel plugin tool has a wrapper (one-directional, tolerant of the
///     non-deterministic loadable set).
/// </summary>
public class StructuralSteelServerForwardingSourceTests
{
    private static string ReadSteelTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", "StructuralSteelTools.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void CreateGenericConnection_ForwardsElementIds()
    {
        var src = ReadSteelTools();
        Assert.Contains("create_generic_steel_connection", src);
        Assert.Contains("[\"elementIds\"]", src);
    }

    [Fact]
    public void AddSolidCut_ForwardsCutAndTarget()
    {
        var src = ReadSteelTools();
        Assert.Contains("[\"cutElementId\"] = cutElementId", src);
        Assert.Contains("[\"targetElementId\"] = targetElementId", src);
    }

    [Fact]
    public void EveryWrapper_ForwardsViaItsOwnDeclaredName()
    {
        var src = ReadSteelTools();
        var methods = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToList();
        Assert.NotEmpty(methods);
        foreach (var m in methods)
        {
            var name = m.GetCustomAttribute<McpServerToolAttribute>()!.Name;
            Assert.False(string.IsNullOrEmpty(name), $"{m.Name} has an empty McpServerTool name");
            Assert.True(src.Contains($"ExecuteAsync(\"{name}\""),
                $"Wrapper '{m.Name}' declares '{name}' but does not forward to ExecuteAsync(\"{name}\", ...)");
        }
    }

    [Fact]
    public void AllWrapperNames_AreUniqueAndSnakeCase()
    {
        var names = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        Assert.NotEmpty(names);
        var dups = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, $"Duplicate wrapper names: {string.Join(", ", dups)}");
        foreach (var n in names) Assert.Matches("^[a-z][a-z0-9_]*$", n);
    }

    [Fact]
    public void LoadablePluginSteelTools_AllHaveAWrapper()
    {
        var wrappers = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        Assembly toolsAsm = typeof(RevitCortex.Tools.Meta.SayHelloTool).Assembly;
        IEnumerable<Type> types;
        try { types = toolsAsm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null)!; }

        var loadable = new List<string>();
        foreach (var t in types.Where(t => t.IsClass && !t.IsAbstract && typeof(ICortexTool).IsAssignableFrom(t)))
        {
            ICortexTool inst;
            try { inst = (ICortexTool)Activator.CreateInstance(t)!; } catch { continue; }
            if (inst.Category == "StructuralSteel") loadable.Add(inst.Name);
        }
        var missing = loadable.Where(p => !wrappers.Contains(p)).ToList();
        Assert.True(missing.Count == 0,
            $"These loadable steel plugin tools have no server wrapper: {string.Join(", ", missing)}");
    }
}

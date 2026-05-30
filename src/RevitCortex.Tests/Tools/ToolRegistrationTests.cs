using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Tools;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Structural tests that verify tool correctness without requiring Revit.
/// These ensure all ICortexTool implementations follow conventions.
/// </summary>
public class ToolRegistrationTests
{
    private static readonly List<Type> AllToolTypes = GetLoadableTypes(
            typeof(RevitCortex.Tools.Meta.SayHelloTool).Assembly)
        .Where(t => t.IsClass && !t.IsAbstract && typeof(ICortexTool).IsAssignableFrom(t))
        .ToList();

    /// <summary>
    /// Safely loads types from an assembly, skipping types whose dependencies
    /// (e.g. RevitAPI.dll) are not available in the test environment.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    [Fact]
    public void AllToolTypes_ImplementICortexTool()
    {
        Assert.NotEmpty(AllToolTypes);
        foreach (var toolType in AllToolTypes)
        {
            Assert.True(typeof(ICortexTool).IsAssignableFrom(toolType),
                $"{toolType.Name} should implement ICortexTool");
        }
    }

    [Fact]
    public void AllTools_HaveUniqueNames()
    {
        var tools = AllToolTypes
            .Select(t => (ICortexTool)Activator.CreateInstance(t)!)
            .ToList();

        var names = tools.Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void AllTools_HaveSnakeCaseNames()
    {
        var tools = AllToolTypes
            .Select(t => (ICortexTool)Activator.CreateInstance(t)!)
            .ToList();

        foreach (var tool in tools)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", tool.Name);
        }
    }

    [Fact]
    public void AllTools_HaveNonEmptyCategory()
    {
        var tools = AllToolTypes
            .Select(t => (ICortexTool)Activator.CreateInstance(t)!)
            .ToList();

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Category),
                $"Tool '{tool.Name}' must have a non-empty Category");
        }
    }

    [Fact]
    public void AllTools_CanBeInstantiated()
    {
        foreach (var toolType in AllToolTypes)
        {
            var instance = Activator.CreateInstance(toolType) as ICortexTool;
            Assert.NotNull(instance);
        }
    }

    [Fact]
    public void ToolCount_MatchesExpected()
    {
        // Update this number when adding new tools to catch accidental omissions.
        // Rebar Module 1 added 12 discovery tools (133 -> 145).
        // Rebar Module 2 added 12 tools: 3 creation + 9 mutators (145 -> 157).
        Assert.True(AllToolTypes.Count >= 157,
            $"Expected at least 157 tools but found {AllToolTypes.Count}. " +
            $"If you removed tools intentionally, update this test.");
    }

    [Fact]
    public void DynamicCapabilityNames_AllMapToRegisteredTools()
    {
        var tools = AllToolTypes
            .Select(t => (ICortexTool)Activator.CreateInstance(t)!)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var toolName in DocumentCapabilities.KnownDynamicToolNames)
        {
            Assert.Contains(toolName, tools);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RevitCortex.Core.Tools;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Structural tests that verify tool correctness without requiring Revit.
/// These ensure all ICortexTool implementations follow conventions and
/// that C# and TypeScript tool sets stay in sync.
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
        // Update this number when adding new tools to catch accidental omissions
        Assert.True(AllToolTypes.Count >= 78,
            $"Expected at least 78 tools but found {AllToolTypes.Count}. " +
            $"If you removed tools intentionally, update this test.");
    }

    [Fact]
    public void TypeScript_ToolRegistrations_MatchCSharp()
    {
        // Read register.ts and extract tool names
        var registerTsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
            "server", "src", "tools", "register.ts");

        // Try to find the file from the repo root
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            // Skip if we can't find the repo root (e.g., in CI without full checkout)
            return;
        }

        registerTsPath = Path.Combine(repoRoot, "server", "src", "tools", "register.ts");
        if (!File.Exists(registerTsPath))
            return; // Skip if TS server not present

        var registerContent = File.ReadAllText(registerTsPath);

        var csharpToolNames = AllToolTypes
            .Select(t => ((ICortexTool)Activator.CreateInstance(t)!).Name)
            .OrderBy(n => n)
            .ToList();

        // Extract tool names from register.ts entries like: { name: "tool_name",
        var tsToolNames = new List<string>();
        foreach (var line in registerContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("{ name:") || trimmed.StartsWith("{name:"))
            {
                var start = trimmed.IndexOf('"') + 1;
                var end = trimmed.IndexOf('"', start);
                if (start > 0 && end > start)
                    tsToolNames.Add(trimmed.Substring(start, end - start));
            }
        }
        tsToolNames.Sort();

        // Every C# tool should have a TS registration
        var missingInTs = csharpToolNames.Except(tsToolNames).ToList();
        Assert.Empty(missingInTs);
    }

    private static string? FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

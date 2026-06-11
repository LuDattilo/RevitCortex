using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the uniform dryRun contract (audit 2026-06-11).
/// The steel tools read dryRun as `?.Value&lt;bool?&gt;() == true` — an implicit
/// default of FALSE (execute immediately when omitted), opposite to every other
/// destructive tool in the codebase (`?? true`, preview-first). The default now
/// goes through ToolHelpers.GetDryRun (default true), and the five steel write
/// tools that had no dryRun at all (the open Fix 3 from the 2026-06-01 steel
/// review) gained one.
/// </summary>
public class DryRunUniformitySourceTests
{
    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", project };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void ToolHelpers_ExposesTheSharedDryRunReader()
    {
        var src = ReadSource("RevitCortex.Tools", "Utilities", "ToolHelpers.cs");
        Assert.Contains("public static bool GetDryRun(JObject input, bool defaultValue = true)", src);
    }

    [Theory]
    [InlineData("StructuralSteelConnectionTools.cs", 7)]
    [InlineData("StructuralSteelConnectionTypeTools.cs", 5)]
    [InlineData("StructuralSteelCutTools.cs", 4)]
    [InlineData("StructuralSteelFabricationTools.cs", 1)]
    public void SteelTools_UseTheSharedReader_NoImplicitFalseDefault(string file, int expectedReaders)
    {
        var src = ReadSource("RevitCortex.Tools", "StructuralSteel", file);
        Assert.DoesNotContain("input[\"dryRun\"]?.Value<bool?>() == true", src);
        var readers = Regex.Matches(src, @"ToolHelpers\.GetDryRun\(input\)");
        Assert.Equal(expectedReaders, readers.Count);
    }

    [Fact]
    public void ManageProjectParameters_SetGroup_DefaultsToPreview()
    {
        var src = ReadSource("RevitCortex.Tools", "Parameters", "ManageProjectParametersTool.cs");
        Assert.DoesNotContain("input[\"dryRun\"]?.Value<bool>() ?? false", src);
        Assert.Contains("ToolHelpers.GetDryRun(input)", src);
    }

    [Fact]
    public void SteelServerWrappers_DocumentTheNewDefault_AndForwardDryRunForTheFiveTools()
    {
        var src = ReadSource("RevitCortex.Server", "Tools", "StructuralSteelTools.cs");
        Assert.DoesNotContain("Default false\")] bool? dryRun", src);
        foreach (var tool in new[]
                 {
                     "modify_steel_connection_inputs",
                     "set_steel_connection_approval",
                     "set_steel_connection_status",
                     "remove_steel_solid_cut",
                     "remove_steel_instance_void_cut",
                 })
        {
            var start = src.IndexOf($"Name = \"{tool}\"", System.StringComparison.Ordinal);
            Assert.True(start >= 0, $"wrapper for {tool} not found");
            var end = src.IndexOf("[McpServerTool", start, System.StringComparison.Ordinal);
            var section = end > start ? src.Substring(start, end - start) : src.Substring(start);
            Assert.Contains("dryRun", section);
        }
    }
}

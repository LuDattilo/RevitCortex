using System.IO;
using Xunit;

namespace RiveTT.Tests.Tools;

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
        var src = ReadSource("RiveTT.Tools", "Utilities", "ToolHelpers.cs");
        Assert.Contains("public static bool GetDryRun(JObject input, bool defaultValue = true)", src);
    }

    [Fact]
    public void ManageProjectParameters_SetGroup_DefaultsToPreview()
    {
        var src = ReadSource("RiveTT.Tools", "Parameters", "ManageProjectParametersTool.cs");
        Assert.DoesNotContain("input[\"dryRun\"]?.Value<bool>() ?? false", src);
        Assert.Contains("ToolHelpers.GetDryRun(input)", src);
    }

    [Fact]
    public void CreateLevel_CreateAction_ReadsDryRunBeforeOpeningTransaction()
    {
        // Regression: create_level ignored dryRun entirely — it opened the Transaction and
        // committed regardless, so dryRun:true still created the level (confirmed live
        // 2026-07-08). The create path must consult GetDryRun and return a preview BEFORE
        // `new Transaction(`.
        var src = ReadSource("RiveTT.Tools", "Elements", "CreateLevelTool.cs");
        Assert.Contains("ToolHelpers.GetDryRun(input)", src);

        var createStart = src.IndexOf("private static CortexResult<object> CreateLevel(", System.StringComparison.Ordinal);
        Assert.True(createStart >= 0, "CreateLevel method not found");
        var createEnd = src.IndexOf("private static CortexResult<object> SetLevel(", createStart, System.StringComparison.Ordinal);
        var createBody = createEnd > createStart ? src.Substring(createStart, createEnd - createStart) : src.Substring(createStart);

        var dryRunIdx = createBody.IndexOf("ToolHelpers.GetDryRun(input)", System.StringComparison.Ordinal);
        var txIdx = createBody.IndexOf("new Transaction(", System.StringComparison.Ordinal);
        Assert.True(dryRunIdx >= 0, "CreateLevel create-action does not read GetDryRun");
        Assert.True(txIdx >= 0, "CreateLevel does not open a Transaction (unexpected)");
        Assert.True(dryRunIdx < txIdx, "CreateLevel reads dryRun AFTER opening the transaction — preview must come first");
    }

    [Fact]
    public void CreateLevelServerWrapper_ForwardsDryRun()
    {
        // Regression: the create_level server wrapper had no dryRun parameter, so the MCP
        // path could never send it (and could never actually execute — always preview).
        var src = ReadSource("RiveTT.Server", "Tools", "ProjectTools.cs");
        var start = src.IndexOf("Name = \"create_level\"", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "create_level wrapper not found");
        var end = src.IndexOf("[McpServerTool", start + 1, System.StringComparison.Ordinal);
        var section = end > start ? src.Substring(start, end - start) : src.Substring(start);
        Assert.Contains("dryRun", section);
        Assert.Contains("p[\"dryRun\"] = dryRun", section);
    }
}

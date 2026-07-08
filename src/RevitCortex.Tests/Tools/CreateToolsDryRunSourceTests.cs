using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions closing the create_* dryRun gap (safety bug found
/// 2026-07-08: create_level with dryRun:true created a real level). The whole
/// creation family (level, grid, room, sheet) went straight to Transaction +
/// Commit without reading dryRun and, for room/sheet, without any confirmation.
///
/// These are source assertions because the tools call Autodesk.Revit.DB.* and
/// cannot be exercised by a plain unit test (RevitAPI is reference-only in the
/// test host — see feedback_unit_tests_cannot_touch_revit_types).
///
/// Contract enforced per tool:
///   plugin  → reads ToolHelpers.GetDryRun(input) AND short-circuits with a
///             "dryRun = true" preview object BEFORE opening the Transaction.
///   wrapper → declares a `bool dryRun` param and forwards p["dryRun"].
/// </summary>
public class CreateToolsDryRunSourceTests
{
    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", project };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    /// <summary>
    /// The dryRun short-circuit must appear before the tool constructs its
    /// Transaction, otherwise the flag is read but ignored.
    /// </summary>
    private static void AssertDryRunPrecedesTransaction(string src, string label)
    {
        var dryRunIdx = src.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal);
        Assert.True(dryRunIdx >= 0, $"{label}: does not read ToolHelpers.GetDryRun(input)");

        var previewIdx = src.IndexOf("dryRun = true", StringComparison.Ordinal);
        Assert.True(previewIdx >= 0, $"{label}: has no 'dryRun = true' preview result");

        var txIdx = src.IndexOf("new Transaction(", StringComparison.Ordinal);
        Assert.True(txIdx >= 0, $"{label}: no Transaction found (unexpected)");

        Assert.True(previewIdx < txIdx,
            $"{label}: the dryRun preview must be returned BEFORE the first Transaction is opened");
    }

    // ---- Plugin tools honor dryRun before writing -------------------------

    [Fact]
    public void CreateLevelTool_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Elements", "CreateLevelTool.cs");
        AssertDryRunPrecedesTransaction(src, "CreateLevelTool");
    }

    [Fact]
    public void CreateGridTool_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Elements", "CreateGridTool.cs");
        AssertDryRunPrecedesTransaction(src, "CreateGridTool");
    }

    [Fact]
    public void CreateRoomTool_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Elements", "CreateRoomTool.cs");
        AssertDryRunPrecedesTransaction(src, "CreateRoomTool");
    }

    [Fact]
    public void CreateSheetTool_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Project", "CreateSheetTool.cs");
        AssertDryRunPrecedesTransaction(src, "CreateSheetTool");
    }

    // ---- Plugin tools confirm before the real write ------------------------

    [Theory]
    [InlineData("Elements", "CreateLevelTool.cs", "CreateLevelTool")]
    [InlineData("Elements", "CreateGridTool.cs", "CreateGridTool")]
    [InlineData("Elements", "CreateRoomTool.cs", "CreateRoomTool")]
    [InlineData("Project", "CreateSheetTool.cs", "CreateSheetTool")]
    public void CreateTools_RequestConfirmationOnWritePath(string folder, string file, string label)
    {
        var src = ReadSource("RevitCortex.Tools", folder, file);
        Assert.True(src.Contains("session.RequestConfirmation", StringComparison.Ordinal),
            $"{label}: create path must call session.RequestConfirmation before committing");
    }

    // ---- Server wrappers declare and forward dryRun ------------------------

    private static void AssertWrapperForwardsDryRun(string serverFile, string toolName)
    {
        var src = ReadSource("RevitCortex.Server", "Tools", serverFile);
        var start = src.IndexOf($"Name = \"{toolName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"wrapper for {toolName} not found in {serverFile}");

        // Bound the search to this single wrapper method (up to the next tool).
        var end = src.IndexOf("[McpServerTool", start + 1, StringComparison.Ordinal);
        var section = end > start ? src.Substring(start, end - start) : src.Substring(start);

        Assert.True(section.Contains("bool dryRun", StringComparison.Ordinal),
            $"{toolName}: wrapper must declare a `bool dryRun` parameter");
        // Accept both the assignment form (p["dryRun"] = dryRun) and the object
        // initializer form (new JObject { ["dryRun"] = dryRun }).
        Assert.True(section.Contains("[\"dryRun\"] = dryRun", StringComparison.Ordinal),
            $"{toolName}: wrapper must forward [\"dryRun\"] = dryRun to the plugin");
    }

    [Fact]
    public void CreateLevelWrapper_ForwardsDryRun() => AssertWrapperForwardsDryRun("ProjectTools.cs", "create_level");

    [Fact]
    public void CreateRoomWrapper_ForwardsDryRun() => AssertWrapperForwardsDryRun("ProjectTools.cs", "create_room");

    [Fact]
    public void CreateGridWrapper_ForwardsDryRun() => AssertWrapperForwardsDryRun("CreationTools.cs", "create_grid");

    [Fact]
    public void CreateSheetWrapper_ForwardsDryRun() => AssertWrapperForwardsDryRun("ViewTools.cs", "create_sheet");

    // ---- MODE B: mutating writes that had no preview and no confirmation ----
    // match_element_properties (destructive: overwrites params across N targets)
    // and modify_element (move/rotate/mirror/copy) previously committed with no
    // dryRun and no RequestConfirmation. Same safety class as the create_level
    // bypass (2026-07-08 audit).

    [Fact]
    public void MatchElementProperties_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Elements", "MatchElementPropertiesTool.cs");
        AssertDryRunPrecedesTransaction(src, "MatchElementPropertiesTool");
        Assert.True(src.Contains("session.RequestConfirmation", StringComparison.Ordinal),
            "MatchElementPropertiesTool: write path must call session.RequestConfirmation");
    }

    [Fact]
    public void ModifyElement_HonorsDryRunBeforeTransaction()
    {
        var src = ReadSource("RevitCortex.Tools", "Elements", "ModifyElementTool.cs");
        AssertDryRunPrecedesTransaction(src, "ModifyElementTool");
        Assert.True(src.Contains("session.RequestConfirmation", StringComparison.Ordinal),
            "ModifyElementTool: write path must call session.RequestConfirmation");
    }

    [Fact]
    public void MatchElementPropertiesWrapper_ForwardsDryRun()
        => AssertWrapperForwardsDryRun("ElementTools.cs", "match_element_properties");

    [Fact]
    public void ModifyElementWrapper_ForwardsDryRun()
        => AssertWrapperForwardsDryRun("ElementTools.cs", "modify_element");

    // ---- create_sheet title-block param-name mismatch ----------------------
    // The wrapper (ViewTools.cs) sends p["titleBlockId"], but the plugin read
    // only input["titleBlockTypeId"] — so an explicit title block was silently
    // dropped and the tool fell back to the first available one. The plugin
    // must accept BOTH keys (titleBlockId is the wrapper's name; the bridge may
    // send either). Same class as feedback_server_wrapper_plugin_param_mismatch.

    [Fact]
    public void CreateSheetTool_AcceptsTitleBlockIdAlias()
    {
        var src = ReadSource("RevitCortex.Tools", "Project", "CreateSheetTool.cs");
        Assert.True(src.Contains("input[\"titleBlockId\"]", StringComparison.Ordinal),
            "CreateSheetTool must read input[\"titleBlockId\"] (the key the server wrapper sends)");
    }

    [Fact]
    public void CreateSheetWrapper_SendsTitleBlockId()
    {
        var src = ReadSource("RevitCortex.Server", "Tools", "ViewTools.cs");
        var start = src.IndexOf("Name = \"create_sheet\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "create_sheet wrapper not found");
        var end = src.IndexOf("[McpServerTool", start + 1, StringComparison.Ordinal);
        var section = end > start ? src.Substring(start, end - start) : src.Substring(start);
        Assert.True(section.Contains("[\"titleBlockId\"]", StringComparison.Ordinal),
            "create_sheet wrapper is expected to send titleBlockId; the plugin must accept that key");
    }
}

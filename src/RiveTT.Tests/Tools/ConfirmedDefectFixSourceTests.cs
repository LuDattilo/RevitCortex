using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Source-text guards for the eight defects the 2026-08-24 tool-surface audit confirmed by
/// reading the code (docs/INVENTAIRE_OUTILS.md, "Défauts confirmés").
///
/// Source-level on purpose, like the rest of the suite: every one of these behaviours needs
/// a live Revit document to exercise, and the 13 tests that do try to reach the Revit API
/// already cannot run without an installed Revit. What CAN be pinned here is that the fix is
/// still in the file — which is exactly how each defect appeared in the first place: a
/// hardcoded point, a parameter nobody read, a preview nobody wrote.
/// </summary>
public class ConfirmedDefectFixSourceTests
{
    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", project };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    /// <summary>The wrapper block for one MCP tool, up to the next [McpServerTool].</summary>
    private static string ServerWrapper(string file, string toolName)
    {
        var src = ReadSource("RiveTT.Server", "Tools", file);
        var start = src.IndexOf($"Name = \"{toolName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{toolName} wrapper not found in {file}");
        var end = src.IndexOf("[McpServerTool", start + 1, StringComparison.Ordinal);
        return end > start ? src.Substring(start, end - start) : src.Substring(start);
    }

    private static void AssertPreviewsBeforeWriting(string source, string tool)
    {
        var dryRunIdx = source.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal);
        var txIdx = source.IndexOf("new Transaction(", StringComparison.Ordinal);
        Assert.True(dryRunIdx >= 0, $"{tool} does not read ToolHelpers.GetDryRun");
        Assert.True(txIdx >= 0, $"{tool} opens no Transaction (unexpected)");
        Assert.True(dryRunIdx < txIdx,
            $"{tool} reads dryRun AFTER opening the transaction — the preview must come first");
    }

    // ── majeur: destructive with no dryRun at all ────────────────────────────────
    // All three went through session.RequestConfirmation, which always returns true, and
    // had no preview: one call destroyed the target while delete_element, the tool they
    // resemble, previews by default.

    [Fact]
    public void DeleteMaterial_PreviewsBeforeDeleting()
    {
        var src = ReadSource("RiveTT.Tools", "Project", "DeleteMaterialTool.cs");
        AssertPreviewsBeforeWriting(src, "delete_material");
        Assert.Contains("DeletionPreview.Build", src);
        // The guard CALL must be gone. The prose above each tool still names the method to
        // explain why it was never a safety net, so match the call form, not the mention.
        Assert.DoesNotContain("if (!session.RequestConfirmation", src);
    }

    [Fact]
    public void DeleteSchedule_PreviewsBeforeDeleting()
    {
        var src = ReadSource("RiveTT.Tools", "Project", "DeleteScheduleTool.cs");
        AssertPreviewsBeforeWriting(src, "delete_schedule");
        Assert.Contains("DeletionPreview.Build", src);
        // The guard CALL must be gone. The prose above each tool still names the method to
        // explain why it was never a safety net, so match the call form, not the mention.
        Assert.DoesNotContain("if (!session.RequestConfirmation", src);
    }

    [Fact]
    public void DeleteSelection_PreviewsBeforeDeleting()
    {
        // manage_selection replaced delete_selection (and save_selection/load_selection) with
        // one action-dispatched tool: Save/Load/Delete are separate private methods in file
        // order, so the shared before/after check must be scoped to the Delete method only —
        // its own Transaction, not Save's, is what the dryRun check has to precede.
        var full = ReadSource("RiveTT.Tools", "Elements", "ManageSelectionTool.cs");
        var deleteStart = full.IndexOf("private static CortexResult<object> Delete(", StringComparison.Ordinal);
        Assert.True(deleteStart >= 0, "manage_selection has no Delete method");
        var src = full.Substring(deleteStart);
        AssertPreviewsBeforeWriting(src, "manage_selection(action=delete)");
        Assert.Contains("DeletionPreview.Build", src);
        // The guard CALL must be gone. The prose above each tool still names the method to
        // explain why it was never a safety net, so match the call form, not the mention.
        Assert.DoesNotContain("if (!session.RequestConfirmation", src);
    }

    [Fact]
    public void DeletionPreview_ProbesTheRealCascadeAndRollsBack()
    {
        // Naming only the requested element understates the damage: previewing one Level
        // deletion reported 1 element while the real delete removed about 100.
        var src = ReadSource("RiveTT.Tools", "Utilities", "DeletionPreview.cs");
        Assert.Contains("probe.Start();", src);
        Assert.Contains("doc.Delete(ids.ToList())", src);
        Assert.Contains("probe.RollBack();", src);
        Assert.Contains("dependentCount", src);
        Assert.Contains("cascadePreviewError", src);
    }

    // ── majeur: no dryRun on the most powerful tool ──────────────────────────────

    [Fact]
    public void SendCodeToRevit_PreviewsWithoutExecutingOrWritingToDisk()
    {
        var src = ReadSource("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs");

        Assert.Contains("ToolHelpers.GetDryRun(input)", src);

        // The BRANCH is what matters, not where the flag is read: the flag is parsed with
        // the other inputs at the top, and the early return has to sit after the sandbox
        // check and before anything with an effect.
        var branchIdx = src.IndexOf("if (dryRun)", StringComparison.Ordinal);
        var sandboxIdx = src.IndexOf("CodeSandbox.Validate(code!)", StringComparison.Ordinal);
        var persistIdx = src.IndexOf("PersistScript(code!", StringComparison.Ordinal);
        var executeIdx = src.IndexOf("RoslynExecutor.Execute(", StringComparison.Ordinal);

        Assert.True(branchIdx >= 0, "send_code_to_revit has no dryRun branch");
        Assert.True(sandboxIdx >= 0 && sandboxIdx < branchIdx,
            "the sandbox check must run before the dryRun branch so a preview reports a rejection");
        Assert.True(persistIdx > branchIdx,
            "the script is written to disk before the dryRun branch — a preview must not persist it");
        Assert.True(executeIdx > branchIdx, "the code runs before the dryRun branch is consulted");
    }

    [Fact]
    public void SendCodeToRevit_DoesNotClaimAnInRevitConfirmationThatDoesNotExist()
    {
        // CortexSession.RequestConfirmation is a no-op that always returns true, and this
        // tool never called it: advertising a human gate that cannot fire is worse than
        // advertising none, because the caller stops looking for one.
        var wrapper = ServerWrapper("ProjectTools.cs", "send_code_to_revit");
        Assert.DoesNotContain("require an in-Revit confirmation", wrapper);
        Assert.Contains("dryRun", wrapper);
    }

    // ── critique: viewports placed at a hardcoded point ──────────────────────────

    [Fact]
    public void BatchCreateSheets_PlacesViewportsInTheRealFrame()
    {
        var src = ReadSource("RiveTT.Tools", "Sheets", "BatchCreateSheetsTool.cs");

        // The frame reference is the title block instance, never a literal offset: on the
        // French A1 block, whose origin sits 650 mm inside the frame, (0.5 ft; 0.5 ft) put
        // every drawing off the paper.
        Assert.DoesNotContain("new XYZ(0.5, 0.5, 0)", src);
        Assert.Contains("SheetFrame.Measure(doc, sheet)", src);
        Assert.Contains("SheetFrame.PlaceCentred", src);
        Assert.Empty(Regex.Matches(src, @"Viewport\.Create\([^)]*new XYZ\(\s*[0-9]"));
    }

    [Fact]
    public void SheetFrame_MeasuresTheTitleBlockNotTheSheetSize()
    {
        var src = ReadSource("RiveTT.Tools", "Utilities", "SheetFrame.cs");
        Assert.Contains("BuiltInCategory.OST_TitleBlocks", src);
        Assert.Contains("get_BoundingBox(sheet)", src);
        // Without a title block the fallback must announce itself rather than pass as measured.
        Assert.Contains("FromTitleBlock = false", src);
    }

    [Fact]
    public void SheetFrame_TilesMultipleViewsInsteadOfStackingThem()
    {
        var src = ReadSource("RiveTT.Tools", "Utilities", "SheetFrame.cs");
        Assert.Contains("public static Frame[] Subdivide(", src);
    }

    // ── critique: a published parameter nobody read ──────────────────────────────

    [Fact]
    public void BatchCreateSheets_ReadsAndPlacesViewIds()
    {
        // viewIds was in the spec and never read on workflow_sheet_set, the tool later
        // retired into this one: every sheet came out empty, reported as a success, with
        // nothing to indicate the views had been dropped.
        var src = ReadSource("RiveTT.Tools", "Sheets", "BatchCreateSheetsTool.cs");
        Assert.Contains("sheetDef[\"viewIds\"]", src);
        Assert.Contains("SheetFrame.PlaceCentred", src);
        // And the count must be reconciled, not assumed.
        Assert.Contains("requestedViewCount", src);
        Assert.Contains("placedViewCount", src);
    }

    // ── majeur: the composed tool was laxer than the plain one ───────────────────

    [Fact]
    public void ClashReviewAndClashDetection_ShareOneDetectionPass()
    {
        var review = ReadSource("RiveTT.Tools", "Workflows", "WorkflowClashReviewTool.cs");
        var detect = ReadSource("RiveTT.Tools", "Project", "ClashDetectionTool.cs");

        Assert.Contains("ClashFinder.Find(", review);
        Assert.Contains("ClashFinder.Find(", detect);

        // The review tool must not keep a private bbox loop alongside the shared pass.
        Assert.DoesNotContain("get_BoundingBox(null)", review);
    }

    [Fact]
    public void ClashFinder_ConfirmsBoxCandidatesAgainstSolids()
    {
        var src = ReadSource("RiveTT.Tools", "Utilities", "ClashFinder.cs");
        Assert.Contains("ElementIntersectsElementFilter", src);
        // The box test survives as a pre-filter; only its role as the final answer was wrong.
        Assert.Contains("BoxesIntersect", src);
    }

    // ── majeur: a write tool classified read-only ────────────────────────────────

    [Fact]
    public void IfcSetFamilyMappingFile_IsClassifiedAsAWrite()
    {
        // Since the ribbon lock, [ToolSafety] is a permission boundary. Marked read-only,
        // this tool crossed the lock and let a read-only session redirect every later
        // IFC export.
        var src = ReadSource("RiveTT.Tools", "IFC", "IfcSetFamilyMappingFileTool.cs");
        Assert.Contains("[ToolSafety(false, false)]", src);
        Assert.DoesNotContain("[ToolSafety(true", src);
    }

    // ── the two-sided contract: a preview the MCP surface cannot request is useless ──

    [Theory]
    [InlineData("MaterialTools.cs", "delete_material")]
    [InlineData("ViewTools.cs", "delete_schedule")]
    [InlineData("ElementTools.cs", "manage_selection")]
    [InlineData("ViewTools.cs", "batch_create_sheets")]
    [InlineData("ProjectTools.cs", "send_code_to_revit")]
    public void EveryToolThatGainedADryRun_PublishesItOnTheMcpSurface(string file, string toolName)
    {
        // A runtime dryRun the wrapper never forwards is unreachable: the tool would always
        // preview and could never execute. create_level shipped exactly that way.
        var wrapper = ServerWrapper(file, toolName);
        Assert.Contains("bool dryRun = true", wrapper);
        Assert.Contains("[\"dryRun\"] = dryRun", wrapper);
    }

    [Fact]
    public void WorkflowClashReview_PublishesTheSolidGeometrySwitch()
    {
        var wrapper = ServerWrapper("ProjectTools.cs", "show_clashes");
        Assert.Contains("bool useSolidGeometry = true", wrapper);
        Assert.Contains("[\"useSolidGeometry\"] = useSolidGeometry", wrapper);
    }
}

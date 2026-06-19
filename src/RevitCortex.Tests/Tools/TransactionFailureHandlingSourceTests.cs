using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the centralized transaction failure preprocessor.
/// Without a preprocessor, any Revit warning raised during Commit() opens a modal
/// TaskDialog on the UI thread, freezing the MCP bridge until a human clicks it
/// (audit 2026-06-11, repo-wide grep: zero IFailuresPreprocessor). The helper and
/// its adoption in the batch write tools touch Revit types, so the contract is
/// locked at source level; behavior is verified live on Revit separately.
/// </summary>
public class TransactionFailureHandlingSourceTests
{
    private static string ReadTool(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", "RevitCortex.Tools" };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void Helper_ExistsAndImplementsThePreprocessorContract()
    {
        var src = ReadTool("Utilities", "TransactionFailureHandling.cs");
        // Must be a real IFailuresPreprocessor wired via SetFailuresPreprocessor.
        Assert.Contains("IFailuresPreprocessor", src);
        Assert.Contains("SetFailuresPreprocessor", src);
        // Warnings are deleted (no modal); errors roll the transaction back (no modal).
        Assert.Contains("DeleteWarning", src);
        Assert.Contains("ProceedWithRollBack", src);
        // Errors must be captured for the structured Fail message, not lost.
        Assert.Contains("GetDescriptionText", src);
        // Public entry point used by tools right after tx creation/Start.
        Assert.Contains("public static FailureCapture SuppressWarnings(Transaction tx)", src);
    }

    [Theory]
    [InlineData("Elements", "SetElementParametersTool.cs")]
    [InlineData("Elements", "RenumberElementsTool.cs")]
    [InlineData("Elements", "DeleteElementTool.cs")]
    [InlineData("Parameters", "BulkModifyParameterValuesTool.cs")]
    [InlineData("Parameters", "AddPrefixSuffixTool.cs")]
    [InlineData("Parameters", "SyncCsvParametersTool.cs")]
    [InlineData("Elements", "BatchRenameTool.cs")]
    [InlineData("Workflows", "WorkflowSheetSetTool.cs")]
    [InlineData("Workflows", "WorkflowRoomDocumentationTool.cs")]
    // 2026-06-19 transaction-safety sweep: tools hardened from the static audit.
    [InlineData("Elements", "CopyElementsTool.cs")]
    [InlineData("Elements", "ChangeElementTypeTool.cs")]
    [InlineData("Elements", "ColorElementsTool.cs")]
    [InlineData("Elements", "SetElementWorksetTool.cs")]
    [InlineData("Elements", "SetElementPhaseTool.cs")]
    [InlineData("Elements", "CreateLevelTool.cs")]
    [InlineData("Elements", "CreateArrayTool.cs")]
    [InlineData("Elements", "CreateFloorTool.cs")]
    [InlineData("Elements", "CreateGridTool.cs")]
    [InlineData("Elements", "CreateLineBasedElementTool.cs")]
    [InlineData("Elements", "ImportFromExcelTool.cs")]
    [InlineData("Elements", "DuplicateFamilyTypeTool.cs")]
    [InlineData("Elements", "RenameFamiliesTool.cs")]
    [InlineData("Parameters", "ClearParameterValuesTool.cs")]
    [InlineData("Parameters", "TransferParametersTool.cs")]
    [InlineData("Parameters", "AddSharedParameterTool.cs")]
    [InlineData("Parameters", "ManageGlobalParametersTool.cs")]
    [InlineData("Parameters", "ManageProjectParametersTool.cs")]
    [InlineData("Annotations", "TagRoomsTool.cs")]
    [InlineData("Annotations", "TagWallsTool.cs")]
    [InlineData("Annotations", "CreateTextNoteTool.cs")]
    [InlineData("Annotations", "CreateColorLegendTool.cs")]
    [InlineData("Annotations", "CreateDimensionsTool.cs")]
    [InlineData("Annotations", "ImportTableTool.cs")]
    [InlineData("Annotations", "WipeEmptyTagsTool.cs")]
    [InlineData("Sheets", "BatchCreateSheetsTool.cs")]
    [InlineData("Sheets", "CreatePlaceholderSheetsTool.cs")]
    [InlineData("Sheets", "DuplicateSheetWithViewsTool.cs")]
    [InlineData("Sheets", "DuplicateSheetWithContentTool.cs")]
    [InlineData("Sheets", "AlignViewportsTool.cs")]
    [InlineData("Views", "CreateViewTool.cs")]
    [InlineData("Views", "RenameViewsTool.cs")]
    [InlineData("Views", "DuplicateViewTool.cs")]
    [InlineData("Views", "ApplyViewTemplateTool.cs")]
    [InlineData("Views", "ManageViewTemplatesTool.cs")]
    [InlineData("Views", "CreateViewFilterTool.cs")]
    [InlineData("Views", "CreateViewsFromRoomsTool.cs")]
    [InlineData("Views", "BatchModifyViewRangeTool.cs")]
    [InlineData("Views", "ManageUnplacedViewsTool.cs")]
    [InlineData("Views", "OverrideGraphicsTool.cs")]
    [InlineData("Views", "PlaceViewportTool.cs")]
    [InlineData("Views", "SectionBoxFromSelectionTool.cs")]
    [InlineData("LinkedFiles", "AlignLinkToHostTool.cs")]
    [InlineData("LinkedFiles", "PinUnpinLinkInstanceTool.cs")]
    [InlineData("LinkedFiles", "MoveLinkInstanceTool.cs")]
    [InlineData("LinkedFiles", "HighlightLinkedElementTool.cs")]
    [InlineData("LinkedFiles", "ShowCrossModelElementsTool.cs")]
    [InlineData("Workflows", "WorkflowClashReviewTool.cs")]
    [InlineData("Project", "SetCompoundStructureTool.cs")]
    [InlineData("IFC", "IfcTagUnreconstructableElementsTool.cs")]
    [InlineData("IFC", "IfcReloadLinkTool.cs")]
    [InlineData("IFC", "IfcExportWithConfigurationTool.cs")]
    [InlineData("IFC", "IfcExportBasicTool.cs")]
    [InlineData("StructuralSteel", "StructuralSteelConnectionTools.cs")]
    [InlineData("StructuralSteel", "StructuralSteelCutTools.cs")]
    [InlineData("StructuralSteel", "StructuralSteelFabricationTools.cs")]
    [InlineData("Rebar", "RebarCreationTools.cs")]
    [InlineData("Rebar", "RebarSystemTools.cs")]
    [InlineData("Rebar", "FabricReinforcementTools.cs")]
    public void BatchWriteTools_AdoptTheFailurePreprocessor(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.Contains("TransactionFailureHandling.SuppressWarnings(", src);
        // A rolled-back commit must surface as a structured failure, not silent success:
        // the tool must inspect the commit status after adopting the preprocessor.
        Assert.Contains("TransactionStatus.Committed", src);
    }

    /// <summary>
    /// The send_code_to_revit executors run user-provided C# inside a Revit
    /// transaction; without a preprocessor a warning at Commit() freezes the bridge,
    /// and an unchecked Commit()/Assimilate() reports a Revit rollback as success.
    /// Both the net8 (Roslyn) and net48 (CodeDom) executors must adopt the pattern.
    /// </summary>
    [Theory]
    [InlineData("CodeExecution", "RoslynExecutor.cs")]
    [InlineData("CodeExecution", "CodeDomExecutor.cs")]
    public void CodeExecutors_AdoptTheFailurePreprocessorAndCheckCommit(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.Contains("TransactionFailureHandling.SuppressWarnings(", src);
        Assert.Contains("TransactionStatus.Committed", src);
        // The transaction-group path must verify Assimilate() too, not assume success.
        Assert.Contains("Assimilate()", src);
        Assert.Contains("TransactionStatus.Committed", src);
    }
}

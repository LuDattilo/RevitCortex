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
    public void BatchWriteTools_AdoptTheFailurePreprocessor(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.Contains("TransactionFailureHandling.SuppressWarnings(", src);
        // A rolled-back commit must surface as a structured failure, not silent success:
        // the tool must inspect the commit status after adopting the preprocessor.
        Assert.Contains("TransactionStatus.Committed", src);
    }
}

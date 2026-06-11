using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the ifc_rebuild_* transaction hygiene (audit
/// 2026-06-11): the six rebuild tools commit one transaction per element inside
/// the candidate loop, which fragmented Revit's undo stack into K steps and left
/// K-of-N partial state on a mid-run failure. The loop must be wrapped in a
/// TransactionGroup with Assimilate (single undo step), the per-element commits
/// must suppress warning dialogs, and FamilySymbol.Activate() — which writes to
/// the document — must run inside the transaction, not before it.
/// </summary>
public class IfcRebuildTransactionGroupSourceTests
{
    private static string ReadTool(string file)
    {
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..", "RevitCortex.Tools", "IFC", file)));
    }

    [Theory]
    [InlineData("IfcRebuildWallsTool.cs")]
    [InlineData("IfcRebuildFloorsTool.cs")]
    [InlineData("IfcRebuildRoofsTool.cs")]
    [InlineData("IfcRebuildOpeningsTool.cs")]
    [InlineData("IfcRebuildStructuralMembersTool.cs")]
    [InlineData("IfcRebuildFamilyInstancesTool.cs")]
    public void RebuildLoop_UsesTransactionGroupAndWarningSuppression(string file)
    {
        var src = ReadTool(file);
        Assert.Contains("new TransactionGroup(", src);
        Assert.Contains("txGroup.Assimilate()", src);
        Assert.Contains("TransactionFailureHandling.SuppressWarnings(", src);
        // A rolled-back per-element commit must surface as that element's failure.
        Assert.Contains("TransactionStatus.Committed", src);
    }

    [Theory]
    [InlineData("IfcRebuildStructuralMembersTool.cs", 2)]
    [InlineData("IfcRebuildFamilyInstancesTool.cs", 1)]
    public void Activate_RunsInsideTheTransaction(string file, int expectedSites)
    {
        var src = ReadTool(file);
        // Every Activate site carries the marker and sits right after tx.Start().
        var marked = Regex.Matches(src, @"tx\.Start\(\);\s*\r?\n\s*// H-IFC-ACT");
        Assert.Equal(expectedSites, marked.Count);
        // Every Activate in the file is one of the marked (inside-tx) sites — none
        // may remain in the old before-the-Transaction form.
        var activates = Regex.Matches(src, @"symbol\.Activate\(\);");
        Assert.Equal(expectedSites, activates.Count);
        var markers = Regex.Matches(src, "H-IFC-ACT");
        Assert.Equal(expectedSites, markers.Count);
    }
}

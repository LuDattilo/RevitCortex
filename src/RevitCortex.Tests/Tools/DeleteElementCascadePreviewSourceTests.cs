using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the delete_element cascade preview. The old dryRun
/// branch listed only the explicitly requested ids: previewing a Level deletion
/// reported "1 element" while the real delete cascaded to ~90-100 dependent views
/// (audit 2026-06-11). The preview must probe the real cascade with the
/// tx-sandbox pattern (doc.Delete inside a transaction, then RollBack) already
/// blessed in ListSchedulableFieldsTool.
/// </summary>
public class DeleteElementCascadePreviewSourceTests
{
    private static string ReadTool()
    {
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..", "RevitCortex.Tools", "Elements", "DeleteElementTool.cs")));
    }

    [Fact]
    public void DryRun_ProbesTheRealCascade_InsideARolledBackTransaction()
    {
        var src = ReadTool();
        Assert.Contains("RevitCortex: Delete Preview", src);
        Assert.Contains("probeTx.RollBack()", src);
        // The probe must run inside the dryRun branch, before the real-delete path.
        var dryRunIdx = src.IndexOf("if (dryRun)", System.StringComparison.Ordinal);
        var probeIdx = src.IndexOf("probeTx", System.StringComparison.Ordinal);
        var realDeleteIdx = src.IndexOf("RevitCortex: Delete Elements", System.StringComparison.Ordinal);
        Assert.True(dryRunIdx >= 0 && dryRunIdx < probeIdx && probeIdx < realDeleteIdx,
            "cascade probe must live inside the dryRun branch, before the real delete");
    }

    [Fact]
    public void DryRun_SurfacesDependentCountAndSample()
    {
        var src = ReadTool();
        Assert.Contains("dependentCount", src);
        Assert.Contains("dependentSample", src);
        Assert.Contains("totalWouldDelete", src);
        // A failed probe must degrade visibly, never silently.
        Assert.Contains("cascadePreviewError", src);
    }
}

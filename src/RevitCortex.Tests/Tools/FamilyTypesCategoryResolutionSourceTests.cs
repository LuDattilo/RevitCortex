using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for get_available_family_types category resolution.
/// Audit 2026-06-11: a category that failed to resolve was silently skipped, and
/// with zero resolved categories the filter was dropped entirely — the tool
/// returned the whole model's types as Ok. Any unresolved category must now be
/// a structured InvalidInput failure listing the offending names; the Ok payload
/// shape (bare array, consumed by ToolResponseShaper) stays unchanged.
/// </summary>
public class FamilyTypesCategoryResolutionSourceTests
{
    private static string ReadTool()
    {
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..", "RevitCortex.Tools", "Project", "GetAvailableFamilyTypesTool.cs")));
    }

    [Fact]
    public void UnresolvedCategories_AreCollected_NotSilentlySkipped()
    {
        var src = ReadTool();
        Assert.Contains("unresolvedCategories.Add(catName)", src);
    }

    [Fact]
    public void AnyUnresolvedCategory_FailsStructured_BeforeApplyingTheFilter()
    {
        var src = ReadTool();
        Assert.Contains("could not be resolved", src);
        // The fail must happen before the filter application, and the old
        // "skip the filter when nothing resolved" branch must be gone.
        var failIdx = src.IndexOf("could not be resolved", System.StringComparison.Ordinal);
        var filterIdx = src.IndexOf("allElements.Where", System.StringComparison.Ordinal);
        Assert.True(failIdx >= 0 && failIdx < filterIdx,
            "unresolved-category failure must precede the filter");
        Assert.DoesNotContain("if (validCatIds.Count > 0)", src);
    }
}

using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Regression guard for room interior elevations. The live behavior depends on
/// Revit's ElevationMarker API, so these tests lock the source-level contract.
/// </summary>
public class CreateViewsFromRoomsElevationSourceTests
{
    private static string ReadTool()
    {
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..",
            "RevitCortex.Tools", "Views", "CreateViewsFromRoomsTool.cs")));
    }

    private static string ExtractElevationBranch(string src)
    {
        var start = src.IndexOf("case \"elevation\":", StringComparison.Ordinal);
        var end = src.IndexOf("default:", start, StringComparison.Ordinal);
        Assert.True(start >= 0, "Elevation branch not found.");
        Assert.True(end > start, "Elevation branch end not found.");
        return src.Substring(start, end - start);
    }

    [Fact]
    public void ElevationBranch_UsesRevitElevationMarkersInsteadOfSectionBoxes()
    {
        var src = ReadTool();
        var elevationBranch = ExtractElevationBranch(src);

        Assert.Contains("ViewFamily.Elevation", src);
        Assert.Contains("ElevationMarker.CreateElevationMarker", src);
        Assert.Contains(".CreateElevation(doc, ownerPlan.Id, index)", src);
        Assert.Contains("FindOwnerFloorPlan(doc, room)", elevationBranch);
        Assert.Contains("CreateElevationsFromRoom(", elevationBranch);
        Assert.DoesNotContain("CreateSectionFromBB(doc, bb, offset, dir)", elevationBranch);
    }

    [Fact]
    public void CatchBlock_IncludesExceptionTypeWhenRevitProvidesAnEmptyMessage()
    {
        var src = ReadTool();

        Assert.Contains("FormatException(ex)", src);
        Assert.Contains("ex.GetType().FullName", src);
        Assert.Contains("string.IsNullOrWhiteSpace(message)", src);
    }
}

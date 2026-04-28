using RevitCortex.Tools.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop;

public class SelectionExporterTests
{
    [Fact]
    public void FormatCategory_FallsBackToDisplayNameForUserAndUnknownIds()
    {
        // Non-built-in (positive id, user category) → display name fallback.
        Assert.Equal("Custom",
            SelectionExporter.FormatCategory(123456, "Custom"));
        // Zero id with no display name → empty string.
        Assert.Equal("", SelectionExporter.FormatCategory(0, null));
        // Zero id with empty display name → empty string.
        Assert.Equal("", SelectionExporter.FormatCategory(0, ""));
    }

    [Fact]
    public void FormatCategory_BuiltInIdResolvesToOstNameOrFallsBackCleanly()
    {
        // -2000011 is BuiltInCategory.OST_Walls. When RevitAPI.dll is loadable
        // (live Revit) we get "OST_Walls"; when it isn't (this test host) we
        // expect the displayName fallback. Either is acceptable — what we DO
        // NOT want is an exception bubbling up to the caller.
        var result = SelectionExporter.FormatCategory(-2000011, "Muri");
        Assert.True(result == "OST_Walls" || result == "Muri",
            $"Expected OST_Walls or Muri, got '{result}'");
    }
}

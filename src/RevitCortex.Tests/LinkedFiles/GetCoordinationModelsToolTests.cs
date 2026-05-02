using RevitCortex.Tools.LinkedFiles;
using Xunit;

namespace RevitCortex.Tests.LinkedFiles;

public class GetCoordinationModelsToolTests
{
    [Fact]
    public void Metadata_DescribesReadOnlyLinkedFilesTool()
    {
        var tool = new GetCoordinationModelsTool();

        Assert.Equal("get_coordination_models", tool.Name);
        Assert.Equal("LinkedFiles", tool.Category);
        Assert.True(tool.RequiresDocument);
        Assert.False(tool.IsDynamic);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(500, 250)]
    public void NormalizeMaxInstances_UsesDefaultAndCap(int? rawValue, int expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.NormalizeMaxInstances(rawValue));
    }

    [Theory]
    [InlineData(null, "Coordination A", true)]
    [InlineData("", "Coordination A", true)]
    [InlineData("coord", "Coordination A", true)]
    [InlineData("MODEL", "Coordination Model", true)]
    [InlineData("navis", "Coordination Model", false)]
    public void MatchesNameFilter_IsCaseInsensitive(string? filter, string candidate, bool expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.MatchesNameFilter(filter, candidate));
    }
}

using System.IO;
using Xunit;

namespace RevitCortex.Tests.PowerBiLive;

public class PowerBiDatasetBindingSourceTests
{
    private static readonly string SchemaPath = Path.GetFullPath(
        Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "PowerBiLive", "PowerBiDatasetSchema.cs"));

    [Fact]
    public void DatasetName_IsDerivedFromCurrentSchemaVersion()
    {
        var source = File.ReadAllText(SchemaPath);

        Assert.Contains("BuildDefaultDatasetName", source);
        Assert.Contains("CurrentVersion", source);
        Assert.DoesNotContain("RevitCortex Live - v1\"", source);
        Assert.DoesNotContain("- v1\";", source);
    }
}

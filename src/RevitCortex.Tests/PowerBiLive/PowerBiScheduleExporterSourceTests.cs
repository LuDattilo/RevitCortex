using System.IO;
using Xunit;

namespace RevitCortex.Tests.PowerBiLive;

public class PowerBiScheduleExporterSourceTests
{
    private static readonly string SourcePath = Path.GetFullPath(
        Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "PowerBiLive", "PowerBiScheduleExporter.cs"));

    [Fact]
    public void Exporter_UsesElementCentricScheduleRows()
    {
        var source = File.ReadAllText(SourcePath);

        Assert.Contains("new FilteredElementCollector(doc, sch.Id)", source);
        Assert.Contains("[\"ElementId\"]", source);
        Assert.Contains("[\"UniqueId\"]", source);
        Assert.Contains("ResolveScheduleFieldParameter", source);
        Assert.Contains("ReadParameterValue", source);
    }
}

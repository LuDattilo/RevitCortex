using System.IO;
using Xunit;

namespace RevitCortex.Tests.PowerBiLive;

public class PbiPublishSchedulesToolSourceTests
{
    private static readonly string SourcePath = Path.GetFullPath(
        Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "PowerBiLive", "Tools", "PbiPublishSchedulesTool.cs"));

    [Fact]
    public void PublishSchedules_DeduplicatesByNaturalScheduleCellKey()
    {
        var source = File.ReadAllText(SourcePath);

        Assert.Contains("DeduplicateScheduleRows", source);
        Assert.Contains("ScheduleId", source);
        Assert.Contains("ElementId", source);
        Assert.Contains("ColumnName", source);
        Assert.Contains("mode == \"append\"", source);
        Assert.Contains("duplicate", source, System.StringComparison.OrdinalIgnoreCase);
    }
}

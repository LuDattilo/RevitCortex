using System.IO;
using Xunit;

namespace RevitCortex.Tests.Server;

public class ServerToolForwardingSourceTests
{
    private static string ReadServerTool(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", fileName));
        return File.ReadAllText(path);
    }

    private static string ReadPluginTool(params string[] segments)
    {
        var parts = new string[segments.Length + 4];
        parts[0] = "..";
        parts[1] = "..";
        parts[2] = "..";
        parts[3] = "..";
        for (var i = 0; i < segments.Length; i++)
            parts[i + 4] = segments[i];

        var path = Path.GetFullPath(Path.Combine(parts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void DuplicateView_WrapperForwardsViewIdsArray()
    {
        var source = ReadServerTool("ViewTools.cs");

        Assert.Contains("[\"viewIds\"] = new JArray(viewId)", source);
    }

    [Fact]
    public void GetLinkTransform_WrapperForwardsRuntimeInstanceId()
    {
        var source = ReadServerTool("LinkTools.cs");

        Assert.Contains("[\"instanceId\"] = linkInstanceId", source);
    }

    [Fact]
    public void ExportToExcel_WrapperUsesRuntimeKeysAndLimit()
    {
        var source = ReadServerTool("CreationTools.cs");

        Assert.Contains("string[]? categories", source);
        Assert.Contains("string? filePath", source);
        Assert.Contains("int? maxElements", source);
        Assert.Contains("p[\"categories\"]", source);
        Assert.Contains("p[\"filePath\"]", source);
        Assert.Contains("p[\"maxElements\"]", source);
    }

    [Fact]
    public void PluginTools_AcceptLegacyAliasesFromExistingClients()
    {
        var duplicateView = ReadPluginTool("RevitCortex.Tools", "Views", "DuplicateViewTool.cs");
        var linkTransform = ReadPluginTool("RevitCortex.Tools", "LinkedFiles", "GetLinkTransformTool.cs");
        var exportToExcel = ReadPluginTool("RevitCortex.Tools", "Elements", "ExportToExcelTool.cs");
        var duplicateSheet = ReadPluginTool("RevitCortex.Tools", "Sheets", "DuplicateSheetWithContentTool.cs");

        Assert.Contains("input[\"viewId\"]", duplicateView);
        Assert.Contains("input[\"linkInstanceId\"]", linkTransform);
        Assert.Contains("input[\"category\"]", exportToExcel);
        Assert.Contains("input[\"outputPath\"]", exportToExcel);
        Assert.Contains("input[\"newNumber\"]", duplicateSheet);
        Assert.Contains("input[\"newName\"]", duplicateSheet);
    }

    [Fact]
    public void FragileTools_ExposeDefensiveRuntimeGuards()
    {
        var ifcExport = ReadPluginTool("RevitCortex.Tools", "IFC", "IfcExportBasicTool.cs");
        var duplicateSheetWithViews = ReadPluginTool("RevitCortex.Tools", "Sheets", "DuplicateSheetWithViewsTool.cs");
        var linesPerViewCount = ReadPluginTool("RevitCortex.Tools", "Project", "LinesPerViewCountTool.cs");

        Assert.Contains("NormalizeIfcFileName", ifcExport);
        Assert.Contains("skippedSchedules", duplicateSheetWithViews);
        Assert.Contains("timeBudgetMs", linesPerViewCount);
    }
}

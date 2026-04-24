using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Server;

public class ServerToolContractTests
{
    private static string LoadSource(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
        return File.ReadAllText(path);
    }

    [Fact]
    public void GetCurrentViewElements_ExposesExplicitCategoryLists()
    {
        var source = LoadSource(@"src\RevitCortex.Server\Tools\ViewTools.cs");

        Assert.Contains("string[]? modelCategoryList = null", source);
        Assert.Contains("string[]? annotationCategoryList = null", source);
        Assert.Contains("string? categoryFilter = null", source);
        Assert.Contains("p[\"modelCategoryList\"] = new JArray(modelCategoryList);", source);
        Assert.Contains("p[\"annotationCategoryList\"] = new JArray(annotationCategoryList);", source);
        Assert.Contains("if (categoryFilter != null && modelCategoryList == null) p[\"modelCategoryList\"] = new JArray(categoryFilter);", source);
    }

    [Fact]
    public void GetScheduleData_ExposesMaxRows()
    {
        var source = LoadSource(@"src\RevitCortex.Server\Tools\ViewTools.cs");

        Assert.Contains("int? maxRows = null", source);
        Assert.Contains("if (maxRows != null) p[\"maxRows\"] = maxRows;", source);
    }

    [Fact]
    public void WorkflowModelAudit_ExposesStructuredAuditFlags()
    {
        var source = LoadSource(@"src\RevitCortex.Server\Tools\ProjectTools.cs");

        Assert.Contains("bool? includeWarnings = null", source);
        Assert.Contains("bool? includeFamilies = null", source);
        Assert.Contains("int? maxWarnings = null", source);
        Assert.Contains("if (includeWarnings != null) p[\"includeWarnings\"] = includeWarnings;", source);
        Assert.Contains("if (includeFamilies != null) p[\"includeFamilies\"] = includeFamilies;", source);
        Assert.Contains("if (maxWarnings != null) p[\"maxWarnings\"] = maxWarnings;", source);
    }
}

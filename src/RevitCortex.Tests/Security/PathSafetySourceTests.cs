using System.IO;
using Xunit;

namespace RevitCortex.Tests.Security;

/// <summary>
/// Source-text assertions that every tool accepting a caller-supplied file path
/// gates it through PathSafety.TryResolveSafe (ultrareview H25/H28/H36 wave).
/// These tools touch Revit types so they cannot be exercised in a unit test
/// without Revit; the source facts lock the contract and prevent a regression
/// back to unchecked Path/File/Directory use on raw caller input.
/// Link tools intentionally pass allowUnc: true — linking models from network
/// shares is a standard BIM workflow.
/// </summary>
public class PathSafetySourceTests
{
    private static string ReadTool(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", "RevitCortex.Tools" };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Theory]
    // Already hardened in earlier waves — keep them locked.
    [InlineData("Annotations", "ImportTableTool.cs")]
    [InlineData("Workflows", "WorkflowDataRoundtripTool.cs")]
    [InlineData("IFC", "IfcValidateRequestTool.cs")]
    // IFC export/import/mapping (strict: user-owned directories only).
    [InlineData("IFC", "IfcExportBasicTool.cs")]
    [InlineData("IFC", "IfcExportWithConfigurationTool.cs")]
    [InlineData("IFC", "IfcSetFamilyMappingFileTool.cs")]
    [InlineData("IFC", "IfcOpenOrImportTool.cs")]
    // Excel / families / project exports (strict).
    [InlineData("Elements", "ExportToExcelTool.cs")]
    [InlineData("Elements", "ExportFamiliesTool.cs")]
    [InlineData("Elements", "ImportFromExcelTool.cs")]
    [InlineData("Project", "BatchExportTool.cs")]
    [InlineData("Project", "ExportSharedParameterFileTool.cs")]
    // Link tools (UNC allowed).
    [InlineData("IFC", "IfcLinkTool.cs")]
    [InlineData("IFC", "IfcReloadLinkTool.cs")]
    [InlineData("LinkedFiles", "AddLinkedFileTool.cs")]
    [InlineData("LinkedFiles", "ReloadLinkedFileFromTool.cs")]
    public void PathAcceptingTool_CallsPathSafety(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.Contains("PathSafety.TryResolveSafe(", src);
    }

    [Theory]
    [InlineData("IFC", "IfcLinkTool.cs")]
    [InlineData("IFC", "IfcReloadLinkTool.cs")]
    [InlineData("LinkedFiles", "AddLinkedFileTool.cs")]
    [InlineData("LinkedFiles", "ReloadLinkedFileFromTool.cs")]
    public void LinkTool_AllowsNetworkPaths(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.Contains("allowUnc: true", src);
    }

    [Theory]
    [InlineData("IFC", "IfcExportBasicTool.cs")]
    [InlineData("IFC", "IfcExportWithConfigurationTool.cs")]
    [InlineData("Elements", "ExportToExcelTool.cs")]
    [InlineData("Elements", "ExportFamiliesTool.cs")]
    [InlineData("Project", "BatchExportTool.cs")]
    [InlineData("Project", "ExportSharedParameterFileTool.cs")]
    public void ExportTool_StaysStrict_NoUncAllowance(string folder, string file)
    {
        var src = ReadTool(folder, file);
        Assert.DoesNotContain("allowUnc: true", src);
    }
}

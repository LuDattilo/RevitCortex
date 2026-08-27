using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Tools.Elements;
using RiveTT.Tools.Project;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Input validation for the P4.1 tools (PLAN_CORRECTION.md) that does not need a
/// live Revit session: these checks run before either tool touches the Revit
/// Application/Document, exactly like open_document's existing checks.
/// </summary>
public class FamilyAndTemplateDocumentToolsTests
{
    private static CortexSession NewSession() => new(new SessionStore());

    [Fact]
    public void OpenFamily_Metadata()
    {
        var tool = new OpenFamilyTool();
        Assert.Equal("open_family", tool.Name);
        Assert.False(tool.RequiresDocument);
    }

    // OpenFamilyTool.Execute references Autodesk.Revit.UI types later in its body (it
    // activates the family in the Revit interface) — the JIT resolves the whole method,
    // RevitAPIUI included, before any branch runs, even the early filePath check below.
    [RequiresRevitApiFact]
    public void OpenFamily_MissingFilePath_IsRefused()
    {
        var tool = new OpenFamilyTool();
        var result = tool.Execute(new JObject(), NewSession());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
    }

    [RequiresRevitApiFact]
    public void OpenFamily_RejectsANonRfaPath()
    {
        var tool = new OpenFamilyTool();
        var input = new JObject { ["filePath"] = @"C:\families\door.rvt" };
        var result = tool.Execute(input, NewSession());

        Assert.False(result.Success);
        Assert.Contains(".rfa", result.Error!.Message);
    }

    [RequiresRevitApiFact]
    public void OpenFamily_RejectsARelativePath()
    {
        var tool = new OpenFamilyTool();
        var input = new JObject { ["filePath"] = "door.rfa" };
        var result = tool.Execute(input, NewSession());

        Assert.False(result.Success);
    }

    [Fact]
    public void OpenTemplate_Metadata()
    {
        var tool = new OpenTemplateTool();
        Assert.Equal("open_template", tool.Name);
        Assert.False(tool.RequiresDocument);
    }

    [RequiresRevitApiFact]
    public void OpenTemplate_RejectsANonRteExtension()
    {
        var tool = new OpenTemplateTool();
        var input = new JObject { ["filePath"] = @"C:\templates\arch.rvt" };
        var result = tool.Execute(input, NewSession());

        Assert.False(result.Success);
        Assert.Contains(".rte", result.Error!.Message);
    }

    [Fact]
    public void CloseDocument_Metadata()
    {
        var tool = new CloseDocumentTool();
        Assert.Equal("close_document", tool.Name);
        Assert.False(tool.RequiresDocument);
    }

    [RequiresRevitApiFact]
    public void CloseDocument_NoApplicationInSession_IsRefused()
    {
        // No RevitApplication in the session store — the exact state before any
        // Revit process has published one. Must not throw a null-reference.
        var tool = new CloseDocumentTool();
        var result = tool.Execute(new JObject(), NewSession());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public void EditFamily_Metadata()
    {
        var tool = new EditFamilyTool();
        Assert.Equal("edit_family", tool.Name);
        Assert.True(tool.RequiresDocument);
    }

    // EditFamilyTool.Execute only touches Autodesk.Revit.DB types (background edit, no
    // UI) — RevitAPI loads standalone via RevitApiBootstrap, unlike RevitAPIUI above.
    [RequiresRevitDbApiFact]
    public void EditFamily_NoActiveDocument_IsRefused()
    {
        var tool = new EditFamilyTool();
        var input = new JObject
        {
            ["familyName"] = "Door",
            ["changes"] = new JArray { new JObject { ["typeName"] = "900x2100" } }
        };
        var result = tool.Execute(input, NewSession());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
    }

    [RequiresRevitDbApiFact]
    public void EditFamily_NeitherFamilyIdNorFamilyName_IsRefused()
    {
        // No active document either, so RequiresDocument's own check fires first
        // (matching every other tool's convention) — still a clean refusal, not
        // a null-reference from reading familyId/familyName on a null document.
        var tool = new EditFamilyTool();
        var input = new JObject
        {
            ["changes"] = new JArray { new JObject { ["typeName"] = "900x2100" } }
        };
        var result = tool.Execute(input, NewSession());

        Assert.False(result.Success);
    }
}

using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop;

public class CrossAppSelectionToolTests
{
    private static CortexSession MakeSession() => new CortexSession(new SessionStore());

    [Fact]
    public void RejectsMissingMode()
    {
        var tool = new CrossAppSelectionTool();
        var result = tool.Execute(new JObject(), MakeSession());
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
        Assert.Contains("mode", result.Error.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnknownMode()
    {
        var tool = new CrossAppSelectionTool();
        var result = tool.Execute(JObject.Parse("{\"mode\":\"banana\"}"), MakeSession());
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public void ImportRejectsEmptyRefs()
    {
        var tool = new CrossAppSelectionTool();
        var result = tool.Execute(
            JObject.Parse("{\"mode\":\"import\",\"refs\":[]}"), MakeSession());
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public void ToolMetadataIsCorrect()
    {
        var tool = new CrossAppSelectionTool();
        Assert.Equal("cross_app_selection", tool.Name);
        Assert.Equal("Interop", tool.Category);
        Assert.True(tool.RequiresDocument);
    }

    [Fact]
    public void Import_ReadsRefsArray_AsCortexElementRef()
    {
        var tool = new CrossAppSelectionTool();
        var session = MakeSession();
        // No active document in session → the body fails fast on doc-missing,
        // BUT only AFTER input validation succeeds. We assert it gets past
        // input validation (i.e., a non-empty refs array no longer triggers
        // the "refs required" error).
        var json = JObject.Parse(@"{
            ""mode"": ""import"",
            ""refs"": [{
                ""sourceFile"": ""Strutture.rvt"",
                ""revitUniqueId"": ""abc-123""
            }]
        }");
        var result = tool.Execute(json, session);
        Assert.False(result.Success);
        // Document-missing error, not refs-empty error.
        Assert.Contains("active", result.Error!.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}

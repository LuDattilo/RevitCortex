using Newtonsoft.Json.Linq;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterTests
{
    private CortexRouter CreateRouter(out CortexSession session, FakeAnalyzer? analyzer = null)
    {
        var store = new SessionStore();
        session = new CortexSession(store);
        var an = analyzer ?? new FakeAnalyzer();
        return new CortexRouter(session, an);
    }

    [Fact]
    public void Route_UnknownTool_ReturnsInvalidInput()
    {
        var router = CreateRouter(out _);
        var result = router.Route("nonexistent", new JObject());
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.InvalidInput, result.Error!.Code);
        Assert.Contains("not found", result.Error.Message);
    }

    [Fact]
    public void Route_RequiresDocument_ButNoDocOpen_Fails()
    {
        var router = CreateRouter(out _);
        var tool = new FakeTool { Name = "needs_doc", RequiresDocument = true };
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("needs_doc", new JObject());
        Assert.False(result.Success);
        Assert.Contains("No document", result.Error!.Message);
    }

    [Fact]
    public void Route_DynamicTool_NotEnabled_Fails()
    {
        var router = CreateRouter(out _);
        var tool = new FakeTool { Name = "get_worksets", IsDynamic = true };
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("get_worksets", new JObject());
        Assert.False(result.Success);
        Assert.Contains("not available", result.Error!.Message);
    }

    [Fact]
    public void Route_ValidTool_ExecutesSuccessfully()
    {
        var router = CreateRouter(out _);
        var tool = new FakeTool { Name = "say_hello" };
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("say_hello", new JObject());
        Assert.True(result.Success);
    }

    [Fact]
    public void OnDocumentChanged_UpdatesCapabilities()
    {
        var analyzer = new FakeAnalyzer { HasWorksets = true };
        var router = CreateRouter(out var session, analyzer);

        router.OnDocumentChanged(new object());

        Assert.True(session.Capabilities.HasWorksets);
        Assert.True(session.Capabilities.IsToolEnabled("get_worksets"));
    }

    [Fact]
    public void OnDocumentChanged_PropagatesLocale()
    {
        var router = CreateRouter(out var session);

        router.OnDocumentChanged(new object(), "it");

        Assert.Equal("it", session.DetectedLocale);
    }

    [Fact]
    public void OnDocumentChanged_DefaultsToEnglish_WhenLocaleNull()
    {
        var router = CreateRouter(out var session);

        router.OnDocumentChanged(new object());

        Assert.Equal("en", session.DetectedLocale);
    }

    [Fact]
    public void GetAvailableToolNames_ExcludesDisabledDynamicTools()
    {
        var router = CreateRouter(out _);
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;

        tools["always_on"] = new FakeTool { Name = "always_on", IsDynamic = false };
        tools["workset_tool"] = new FakeTool { Name = "workset_tool", IsDynamic = true };

        var available = router.GetAvailableToolNames();
        Assert.Contains("always_on", available);
        Assert.DoesNotContain("workset_tool", available);
    }
}

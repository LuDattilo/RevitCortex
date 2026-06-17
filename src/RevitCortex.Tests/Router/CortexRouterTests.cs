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

    [Fact]
    public void Route_ResetsApproveAllAfterToolExecution()
    {
        var router = CreateRouter(out var session);
        session.ApproveAll = true;

        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools["say_hello"] = new FakeTool { Name = "say_hello" };

        var result = router.Route("say_hello", new JObject());

        Assert.True(result.Success);
        Assert.False(session.ApproveAll);
    }

    [Fact]
    public void Route_DoesNotResetAutoModeAfterToolExecution()
    {
        // "Auto" mode must persist across tool calls until the user clicks Stop Auto
        // or the document is reinitialized — unlike "Yes to All" (ApproveAll), which
        // is per-batch. The router's post-execution reset must NOT clear AutoMode,
        // otherwise every subsequent destructive op re-prompts (v1.0.27 live bug).
        var router = CreateRouter(out var session);
        session.AutoMode = true;

        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools["say_hello"] = new FakeTool { Name = "say_hello" };

        var result = router.Route("say_hello", new JObject());

        Assert.True(result.Success);
        Assert.True(session.AutoMode); // must survive the tool call
    }

    [Fact]
    public void OnDocumentChanged_WhenAutoModeWasOn_ResetsAndNotifiesUi()
    {
        // A document open/switch/server-start calls Reinitialize, which clears
        // AutoMode. Core can't reach the UI, so the router must fire the UI
        // notification — otherwise the floating "Auto mode ON" window lingers
        // while confirmations have silently resumed (the reported desync).
        var router = CreateRouter(out var session);
        session.AutoMode = true;

        int notifyCount = 0;
        bool lastActive = true;
        Action<bool> handler = active => { notifyCount++; lastActive = active; };
        RevitCortex.Plugin.UI.ConfirmationHelper.AutoModeChanged += handler;
        try
        {
            router.OnDocumentChanged(new object(), "en");
        }
        finally
        {
            RevitCortex.Plugin.UI.ConfirmationHelper.AutoModeChanged -= handler;
        }

        Assert.False(session.AutoMode);   // reset by Reinitialize
        Assert.Equal(1, notifyCount);     // UI notified exactly once
        Assert.False(lastActive);         // notified that Auto is now OFF
    }

    [Fact]
    public void OnDocumentChanged_WhenAutoModeWasOff_DoesNotNotify()
    {
        // No spurious notification when Auto was already off — avoids needless
        // window churn on every routine document event.
        var router = CreateRouter(out var session);
        Assert.False(session.AutoMode);

        int notifyCount = 0;
        Action<bool> handler = _ => notifyCount++;
        RevitCortex.Plugin.UI.ConfirmationHelper.AutoModeChanged += handler;
        try
        {
            router.OnDocumentChanged(new object(), "en");
        }
        finally
        {
            RevitCortex.Plugin.UI.ConfirmationHelper.AutoModeChanged -= handler;
        }

        Assert.Equal(0, notifyCount);
    }
}

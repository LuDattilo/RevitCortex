using Newtonsoft.Json.Linq;
using RiveTT.Core.Discovery;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Plugin;
using RiveTT.Core.Tools;
using Xunit;

namespace RiveTT.Tests.Router;

public class CortexRouterTests
{
    private CortexRouter CreateRouter(out CortexSession session, FakeAnalyzer? analyzer = null)
    {
        var store = new SessionStore();
        session = new CortexSession(store);
        var an = analyzer ?? new FakeAnalyzer();
        // An explicit temp-file logger, not the default %LOCALAPPDATA%\RiveTT\audit.jsonl:
        // this suite runs on every `dotnet test` and was otherwise writing real audit
        // entries on the dev machine, masking whether the real execution path logs at all.
        var auditPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rc-audit-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        return new CortexRouter(session, an, new AuditLogger(auditPath));
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
        var tools = (System.Collections.Generic.Dictionary<string, RiveTT.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("needs_doc", new JObject());
        Assert.False(result.Success);
        Assert.Contains("No document", result.Error!.Message);
    }

    [Fact]
    public void Route_DynamicTool_NotEnabled_Fails()
    {
        var router = CreateRouter(out _);
        var tool = new FakeTool { Name = "list_worksets", IsDynamic = true };
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RiveTT.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("list_worksets", new JObject());
        Assert.False(result.Success);
        Assert.Contains("not available", result.Error!.Message);
    }

    // A successful Route() reaches EnrichResult, whose GetActiveRevitVersion() casts to
    // Autodesk.Revit.DB.Document — the JIT resolves that type eagerly even on a null cast,
    // so RevitAPI must be loadable for the method to run at all.
    [RequiresRevitDbApiFact]
    public void Route_ValidTool_ExecutesSuccessfully()
    {
        var router = CreateRouter(out _);
        var tool = new FakeTool { Name = "ping_revit" };
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RiveTT.Core.Tools.ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;

        var result = router.Route("ping_revit", new JObject());
        Assert.True(result.Success);
    }

    [RequiresRevitDbApiFact]
    public void Route_EnrichesEverySuccessWithExecutionContract()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new FakeTool { Name = "write_fake" });

        var result = router.Route("write_fake", new JObject { ["dryRun"] = true });

        Assert.True(result.Success);
        var data = Assert.IsType<JObject>(result.Data);
        Assert.Equal("RiveTT", data["execution"]!["connector"]!.Value<string>());
        // No real Document in this fake-tool test, so the router falls back to
        // "unknown" rather than a hardcoded year — revitVersion is read from the
        // active document's Application.VersionNumber (2026 or 2027), not a literal.
        Assert.Equal("unknown", data["execution"]!["revitVersion"]!.Value<string>());
        Assert.Equal("automatic", data["execution"]!["mode"]!.Value<string>());
        Assert.False(data["mutated"]!.Value<bool>());
    }

    [Fact]
    public void Route_NormalizesTransactionFailuresForAgents()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new TransactionFailingTool());

        var result = router.Route("transaction_failing", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.TransactionFailed, result.Error!.Code);
        Assert.True((bool)result.Error.Context!["rolledBack"]);
        Assert.NotNull(result.Error.Context["warnings"]);
        Assert.NotNull(result.Error.Context["failedElementIds"]);
        Assert.NotNull(result.Error.Context["repairHints"]);
    }

    [Fact]
    public void OnDocumentChanged_UpdatesCapabilities()
    {
        var analyzer = new FakeAnalyzer { HasWorksets = true };
        var router = CreateRouter(out var session, analyzer);

        router.OnDocumentChanged(new object());

        Assert.True(session.Capabilities.HasWorksets);
        Assert.True(session.Capabilities.IsToolEnabled("list_worksets"));
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
        var tools = (System.Collections.Generic.Dictionary<string, RiveTT.Core.Tools.ICortexTool>)field.GetValue(router)!;

        tools["always_on"] = new FakeTool { Name = "always_on", IsDynamic = false };
        tools["workset_tool"] = new FakeTool { Name = "workset_tool", IsDynamic = true };

        var available = router.GetAvailableToolNames();
        Assert.Contains("always_on", available);
        Assert.DoesNotContain("workset_tool", available);
    }

    private sealed class TransactionFailingTool : ICortexTool
    {
        public string Name => "transaction_failing";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "test";
        public CortexResult<object> Execute(JObject input, CortexSession session)
            => CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "Revit rejected the transaction", "Repair constraints");
    }

}

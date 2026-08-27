using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Plugin;
using RiveTT.Tools.Meta;
using Xunit;

namespace RiveTT.Tests.Router;

/// <summary>
/// The ribbon write lock. These tests pin the property that makes it a lock and
/// not a suggestion: while it is closed, nothing that could write runs, and
/// nothing reachable through the pipe can open it.
/// </summary>
public class WriteAccessGateTests
{
    private static CortexRouter CreateRouter(out CortexSession session, bool writesAllowed)
    {
        session = new CortexSession(new SessionStore());
        session.WriteAccess.Set(writesAllowed, "test");
        // Explicit temp-file logger: without it this suite writes real entries
        // to %LOCALAPPDATA%\RiveTT\audit.jsonl on every dotnet test run.
        var auditPath = Path.Combine(Path.GetTempPath(), "rc-audit-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        var router = new CortexRouter(session, new FakeAnalyzer(), new AuditLogger(auditPath));
        router.RegisterTool(new FakeTool { Name = "create_thing" });
        router.RegisterTool(new FakeTool { Name = "get_thing" });
        return router;
    }

    [Fact]
    public void ReadOnly_WriteTool_IsRefusedWithPermissionDenied()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("create_thing", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains("read-only", result.Error.Message);
        // The refusal has to say where the switch is; an agent cannot guess a
        // ribbon panel from an error code.
        Assert.Contains("RiveTT", result.Error.Suggestion!);
        Assert.Equal(false, result.Error.Context!["writesAllowed"]);
        Assert.Equal(false, result.Error.Context!["modelChanged"]);
    }

    [Fact]
    public void ReadOnly_WriteTool_RefusalNamesThePublicToolWhenGiven()
    {
        // create_wall etc. route through a shared generic internal tool
        // (e.g. create_line_based_element): the refusal must name what the
        // caller actually invoked, not the internal handler — see P1.6 in
        // PLAN_CORRECTION.md.
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("create_thing", new JObject(), publicToolName: "create_wall");

        Assert.False(result.Success);
        Assert.Contains("'create_wall'", result.Error!.Message);
        Assert.DoesNotContain("create_thing", result.Error.Message);
    }

    [Fact]
    public void ReadOnly_DryRunDoesNotBypassTheLock()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("create_thing", new JObject { ["dryRun"] = true });

        // A preview is a tool's own promise, not a permission boundary. Trusting
        // it would make the lock only as strong as the weakest of ~250 tools.
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    // Route() succeeds here (get_thing is read-only) and reaches EnrichResult, whose
    // GetActiveRevitVersion() casts to Autodesk.Revit.DB.Document — the JIT resolves that
    // type eagerly even though the cast target is null in this test, so the assembly must
    // be loadable for the method to run at all.
    [RequiresRevitDbApiFact]
    public void ReadOnly_ReadToolStillAnswers()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("get_thing", new JObject());

        Assert.True(result.Success);
    }

    [RequiresRevitDbApiFact]
    public void ReadOnly_IsReportedInTheExecutionBlock()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("get_thing", new JObject());

        var execution = (JObject)((JObject)result.Data!)["execution"]!;
        Assert.False(execution["writesAllowed"]!.Value<bool>());
        Assert.True(execution["toolReadOnly"]!.Value<bool>());
    }

    [RequiresRevitDbApiFact]
    public void WritesAllowed_WriteToolRuns()
    {
        var router = CreateRouter(out _, writesAllowed: true);

        var result = router.Route("create_thing", new JObject());

        Assert.True(result.Success);
        var execution = (JObject)((JObject)result.Data!)["execution"]!;
        Assert.True(execution["writesAllowed"]!.Value<bool>());
    }

    [Fact]
    public void Lock_SurvivesDocumentReinitialization()
    {
        var router = CreateRouter(out var session, writesAllowed: false);

        // Opening, closing or saving-as a document must not hand write access
        // back: the lock describes the session, not the document.
        session.Reinitialize(new Core.Discovery.DocumentCapabilities(), "fr");

        Assert.False(session.WriteAccess.WritesAllowed);
        Assert.False(router.Route("create_thing", new JObject()).Success);
    }

    [Fact]
    public void Policy_ReportsWhoChangedItAndOnlyFlipsOnce()
    {
        var session = new CortexSession(new SessionStore());

        Assert.True(session.WriteAccess.Set(false, "startup"));
        Assert.Equal("startup", session.WriteAccess.ChangedBy);
        // Re-selecting the current mode is a no-op, so a second ribbon click on
        // the same button reports nothing to the user.
        Assert.False(session.WriteAccess.Set(false, "ribbon"));
        Assert.True(session.WriteAccess.Set(true, "ribbon"));
        Assert.Equal("ribbon", session.WriteAccess.ChangedBy);
    }

    [Fact]
    public void NoPublishedToolWritesToThePolicy()
    {
        // The guarantee is structural, so it is asserted on the source of the
        // whole tool catalogue rather than on one router instance: no tool may
        // call Set on the policy. A tool that could would be the first thing an
        // agent reaches for after a refusal, and the lock would mean nothing.
        var toolsRoot = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "RiveTT.Tools"));
        var offenders = Directory
            .EnumerateFiles(toolsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("obj", "")) &&
                           File.ReadAllText(file).Contains("WriteAccess.Set("))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.Empty(offenders);
    }

    // GetServerCapabilitiesTool.GetActiveRevitVersion() casts to Document too.
    [RequiresRevitDbApiFact]
    public void CapabilityContract_AnnouncesTheLockAndItsState()
    {
        var session = new CortexSession(new SessionStore());
        session.WriteAccess.Set(false, "startup");

        var result = new GetServerCapabilitiesTool().Execute(new JObject(), session);

        var payload = JObject.FromObject(result.Data!);
        Assert.True(payload["readOnlyModeExists"]!.Value<bool>());
        Assert.False(payload["writesAllowed"]!.Value<bool>());
        Assert.True(payload["readOnlyMode"]!["active"]!.Value<bool>());
        Assert.False(payload["readOnlyMode"]!["toolsCanUnlock"]!.Value<bool>());
        Assert.Equal("startup", payload["readOnlyMode"]!["changedBy"]!.Value<string>());
    }
}

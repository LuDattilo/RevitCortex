using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Plugin;
using Xunit;

namespace RiveTT.Tests.Router;

public class CortexRouterExceptionTests
{
    private class ThrowingTool : ICortexTool
    {
        public string Name => "throwing_tool";
        public string Category => "Test";
        public string Description => "Throws for exception-capture tests.";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public CortexResult<object> Execute(JObject input, CortexSession session)
            => throw new System.InvalidOperationException("kaboom");
    }

    private static CortexRouter CreateRouterWith(ICortexTool tool, AuditLogger audit)
    {
        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new FakeAnalyzer(), audit);
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;
        return router;
    }

    [Fact]
    public void Route_ToolThrows_ReturnsStructuredUnknown_AndAudits()
    {
        var auditPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rc-audit-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var router = CreateRouterWith(new ThrowingTool(), new AuditLogger(auditPath));

            var result = router.Route("throwing_tool", new JObject());

            Assert.False(result.Success);
            Assert.Equal(CortexErrorCode.Unknown, result.Error!.Code);
            Assert.Contains("Unhandled exception", result.Error.Message);

            var audit = System.IO.File.ReadAllText(auditPath);
            Assert.Contains("throwing_tool", audit);
            Assert.Contains("\"result\":\"fail\"", audit);
        }
        finally
        {
            try { System.IO.File.Delete(auditPath); } catch { }
        }
    }
}

using Newtonsoft.Json.Linq;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Dynamo
{
    /// <summary>
    /// Router-level tests for the Dynamo dynamic-tool gate. Every OTHER Dynamo test calls
    /// tool.Execute() directly, which bypasses the router's IsDynamic gate — the exact seam
    /// that left dynamo_get_status / dynamo_run_graph unreachable (Issue #1). These tests
    /// drive the real CortexRouter so a regression in the capability gate is caught.
    ///
    /// The live DocumentAnalyzer.Analyze path (which probes for an installed Dynamo and calls
    /// EnableTool) needs a real Revit Document, so it can't run headless here — that wiring is
    /// covered by the live Task 16 checklist. Here we stand in a fake analyzer that enables the
    /// same tool names, exercising the router gate that consumes those capabilities.
    /// </summary>
    public class DynamoCapabilityGatingTests
    {
        /// <summary>Analyzer that mimics DocumentAnalyzer when Dynamo IS present: enables the two dynamic Dynamo tools.</summary>
        private sealed class DynamoPresentAnalyzer : IDocumentAnalyzer
        {
            public bool DynamoPresent { get; set; }

            public void Analyze(object document, DocumentCapabilities capabilities)
            {
                if (DynamoPresent)
                {
                    capabilities.EnableTool("dynamo_get_status");
                    capabilities.EnableTool("dynamo_run_graph");
                }
            }
        }

        private sealed class StubTool : ICortexTool
        {
            public string Name { get; set; } = "stub";
            public string Category => "Dynamo";
            public bool RequiresDocument { get; set; }
            public bool IsDynamic { get; set; }
            public string Description => "stub";
            public CortexResult<object> Execute(JObject input, CortexSession session)
                => CortexResult<object>.Ok(new { called = true, name = Name });
        }

        private static void Register(CortexRouter router, ICortexTool tool)
        {
            var field = typeof(CortexRouter).GetField("_tools",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
            tools[tool.Name] = tool;
        }

        [Fact]
        public void DynamoDynamicTools_Blocked_WhenDynamoAbsent()
        {
            var session = new CortexSession(new SessionStore());
            var router = new CortexRouter(session, new DynamoPresentAnalyzer { DynamoPresent = false });
            Register(router, new StubTool { Name = "dynamo_get_status", IsDynamic = true });

            // No document / no capabilities enabled → dynamic tool is invisible and blocked.
            router.OnDocumentChanged(new object());

            Assert.DoesNotContain("dynamo_get_status", router.GetAvailableToolNames());

            var result = router.Route("dynamo_get_status", new JObject());
            Assert.False(result.Success);
            Assert.Contains("not available", result.Error!.Message);
        }

        [Fact]
        public void DynamoDynamicTools_Reachable_WhenDynamoPresent()
        {
            var session = new CortexSession(new SessionStore());
            var router = new CortexRouter(session, new DynamoPresentAnalyzer { DynamoPresent = true });
            Register(router, new StubTool { Name = "dynamo_get_status", IsDynamic = true });

            router.OnDocumentChanged(new object());

            // Capability layer flipped the gate: caps.IsToolEnabled is true...
            Assert.True(session.Capabilities.IsToolEnabled("dynamo_get_status"));
            // ...the router exposes it...
            Assert.Contains("dynamo_get_status", router.GetAvailableToolNames());
            // ...and Route now invokes it instead of returning "not available".
            var result = router.Route("dynamo_get_status", new JObject());
            Assert.True(result.Success);
        }

        [Fact]
        public void EnableTool_FlipsIsToolEnabled_ForBothDynamoTools()
        {
            var caps = new DocumentCapabilities();
            Assert.False(caps.IsToolEnabled("dynamo_get_status"));
            Assert.False(caps.IsToolEnabled("dynamo_run_graph"));

            caps.EnableTool("dynamo_get_status");
            caps.EnableTool("dynamo_run_graph");

            Assert.True(caps.IsToolEnabled("dynamo_get_status"));
            Assert.True(caps.IsToolEnabled("dynamo_run_graph"));
        }

        [Fact]
        public void StaticDynamoTools_NeedNoCapability()
        {
            // The generate/list Dynamo tools are IsDynamic=false, so the router exposes them
            // regardless of capabilities. Registered but never EnableTool'd → still available.
            var session = new CortexSession(new SessionStore());
            var router = new CortexRouter(session, new DynamoPresentAnalyzer { DynamoPresent = false });
            Register(router, new StubTool { Name = "dynamo_generate_graph", IsDynamic = false });

            router.OnDocumentChanged(new object());

            Assert.Contains("dynamo_generate_graph", router.GetAvailableToolNames());
        }
    }
}

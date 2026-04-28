using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterCacheTests
{
    /// <summary>Counts Execute() calls so tests can prove cache hit/miss behavior.</summary>
    private class CountingTool : ICortexTool
    {
        public string Name { get; set; } = "get_phases";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "counts calls";
        public int ExecuteCallCount { get; private set; }
        public CortexResult<object> NextResult { get; set; } =
            CortexResult<object>.Ok(new { phases = new[] { "Existing", "New" } });

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            ExecuteCallCount++;
            return NextResult;
        }
    }

    /// <summary>CountingTool that opts into caching at a chosen scope.</summary>
    private class CachingCountingTool : CountingTool, ICacheableTool
    {
        public CacheScope CacheScope { get; set; } = CacheScope.Document;
    }

    private static CortexRouter CreateRouter(out CortexSession session)
    {
        var store = new SessionStore();
        session = new CortexSession(store);
        return new CortexRouter(session, new FakeAnalyzer());
    }

    private static void Register(CortexRouter router, ICortexTool tool)
    {
        var field = typeof(CortexRouter).GetField("_tools",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tools = (Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;
    }

    [Fact]
    public void Route_ToolWithoutCacheableInterface_AlwaysCallsExecute()
    {
        var router = CreateRouter(out _);
        var tool = new CountingTool { Name = "get_phases" }; // not ICacheableTool
        Register(router, tool);

        router.Route("get_phases", new JObject());
        router.Route("get_phases", new JObject());
        router.Route("get_phases", new JObject());

        Assert.Equal(3, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_SessionCacheableTool_ExecutesOnceForSameInput()
    {
        var router = CreateRouter(out _);
        var tool = new CachingCountingTool
        {
            Name = "get_project_info",
            CacheScope = CacheScope.Session,
        };
        Register(router, tool);

        var input = new JObject { ["x"] = 1 };
        router.Route("get_project_info", input);
        router.Route("get_project_info", input);
        router.Route("get_project_info", input);

        Assert.Equal(1, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_DocumentCacheableTool_ReExecutesAfterDocumentVersionBump()
    {
        var router = CreateRouter(out var session);
        var tool = new CachingCountingTool
        {
            Name = "get_phases",
            CacheScope = CacheScope.Document,
        };
        Register(router, tool);

        var input = new JObject { ["a"] = "b" };

        router.Route("get_phases", input);
        router.Route("get_phases", input); // hit
        Assert.Equal(1, tool.ExecuteCallCount);

        session.BumpDocumentVersion();
        session.Cache.InvalidateScope(CacheScope.Document);

        router.Route("get_phases", input); // miss after invalidation
        Assert.Equal(2, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_DifferentInputs_DoNotShareCacheEntry()
    {
        var router = CreateRouter(out _);
        var tool = new CachingCountingTool
        {
            Name = "get_warnings",
            CacheScope = CacheScope.Document,
        };
        Register(router, tool);

        router.Route("get_warnings", new JObject { ["severity"] = "high" });
        router.Route("get_warnings", new JObject { ["severity"] = "low" });
        router.Route("get_warnings", new JObject { ["severity"] = "high" }); // hit

        Assert.Equal(2, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_FailedResult_IsNotCached()
    {
        var router = CreateRouter(out _);
        var tool = new CachingCountingTool
        {
            Name = "get_phases",
            CacheScope = CacheScope.Document,
            NextResult = CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput, "boom"),
        };
        Register(router, tool);

        router.Route("get_phases", new JObject());
        router.Route("get_phases", new JObject());
        router.Route("get_phases", new JObject());

        Assert.Equal(3, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_DocumentCacheable_SameInputTwice_OnlyExecutesOnce()
    {
        var router = CreateRouter(out _);
        var tool = new CachingCountingTool
        {
            Name = "get_linked_file_instances",
            CacheScope = CacheScope.Document,
        };
        Register(router, tool);

        var input = new JObject { ["k"] = "v" };
        router.Route("get_linked_file_instances", input);
        router.Route("get_linked_file_instances", input);

        Assert.Equal(1, tool.ExecuteCallCount);
    }

    [Fact]
    public void Route_HashIgnoresKeyOrder()
    {
        var router = CreateRouter(out _);
        var tool = new CachingCountingTool
        {
            Name = "get_phases",
            CacheScope = CacheScope.Document,
        };
        Register(router, tool);

        var a = new JObject { ["alpha"] = 1, ["beta"] = 2 };
        var b = new JObject { ["beta"] = 2, ["alpha"] = 1 };

        router.Route("get_phases", a);
        router.Route("get_phases", b); // same canonical content → cache hit

        Assert.Equal(1, tool.ExecuteCallCount);
    }
}

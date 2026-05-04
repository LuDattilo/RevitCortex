using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using RevitCortex.Tests.Router;
using RevitCortex.Tools.LinkedFiles;
using Xunit;

namespace RevitCortex.Tests.LinkedFiles;

public class GetCoordinationModelsToolTests
{
    [Fact]
    public void Metadata_DescribesReadOnlyLinkedFilesTool()
    {
        var tool = new GetCoordinationModelsTool();

        Assert.Equal("get_coordination_models", tool.Name);
        Assert.Equal("LinkedFiles", tool.Category);
        Assert.True(tool.RequiresDocument);
        Assert.False(tool.IsDynamic);
    }

    [Fact]
    public void Metadata_OptsIntoDocumentScopedCaching()
    {
        var tool = new GetCoordinationModelsTool();

        var cacheable = Assert.IsAssignableFrom<ICacheableTool>(tool);
        Assert.Equal(CacheScope.Document, cacheable.CacheScope);
    }

    [Fact]
    public void CacheKey_VariesWithNameFilterIncludeInstancesAndMaxInstances()
    {
        var router = CreateRouter();
        var tool = new CachingCountingTool
        {
            Name = "get_coordination_models",
            CacheScope = CacheScope.Document,
        };
        Register(router, tool);

        // Each input shape should miss the cache the first time.
        router.Route("get_coordination_models",
            new JObject { ["nameFilter"] = "campus", ["includeInstances"] = true, ["maxInstances"] = 100 });
        router.Route("get_coordination_models",
            new JObject { ["nameFilter"] = "north", ["includeInstances"] = true, ["maxInstances"] = 100 });
        router.Route("get_coordination_models",
            new JObject { ["nameFilter"] = "campus", ["includeInstances"] = false, ["maxInstances"] = 100 });
        router.Route("get_coordination_models",
            new JObject { ["nameFilter"] = "campus", ["includeInstances"] = true, ["maxInstances"] = 25 });

        // Re-issuing the very first input should hit the cache (no extra Execute).
        router.Route("get_coordination_models",
            new JObject { ["nameFilter"] = "campus", ["includeInstances"] = true, ["maxInstances"] = 100 });

        Assert.Equal(4, tool.ExecuteCallCount);
    }

    private static CortexRouter CreateRouter()
    {
        var store = new SessionStore();
        var session = new CortexSession(store);
        return new CortexRouter(session, new FakeAnalyzer());
    }

    private static void Register(CortexRouter router, ICortexTool tool)
    {
        var field = typeof(CortexRouter).GetField("_tools",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tools = (Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;
    }

    private class CachingCountingTool : ICortexTool, ICacheableTool
    {
        public string Name { get; set; } = "get_coordination_models";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "counts calls";
        public CacheScope CacheScope { get; set; } = CacheScope.Document;
        public int ExecuteCallCount { get; private set; }

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            ExecuteCallCount++;
            return CortexResult<object>.Ok(new { ok = true });
        }
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(500, 250)]
    public void NormalizeMaxInstances_UsesDefaultAndCap(int? rawValue, int expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.NormalizeMaxInstances(rawValue));
    }

    [Theory]
    [InlineData(null, "Coordination A", true)]
    [InlineData("", "Coordination A", true)]
    [InlineData("coord", "Coordination A", true)]
    [InlineData("MODEL", "Coordination Model", true)]
    [InlineData("navis", "Coordination Model", false)]
    public void MatchesNameFilter_IsCaseInsensitive(string? filter, string candidate, bool expected)
    {
        Assert.Equal(expected, GetCoordinationModelsTool.MatchesNameFilter(filter, candidate));
    }

    [Fact]
    public void MatchesAnyNameFilter_MatchesInstanceNames()
    {
        var modelAndTypeNames = new[] { "Campus Model", "CM Type 01" };
        var instanceNames = new[] { "North Wing NWC", "South Wing NWC" };

        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("south", modelAndTypeNames, instanceNames));
        Assert.False(GetCoordinationModelsTool.MatchesAnyNameFilter("east", modelAndTypeNames, instanceNames));
    }

    [Fact]
    public void MatchesAnyNameFilter_MatchesModelAndTypeNames()
    {
        var modelAndTypeNames = new[] { "Campus Model", "CM Type 01" };
        var instanceNames = new[] { "North Wing NWC" };

        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("campus", modelAndTypeNames, instanceNames));
        Assert.True(GetCoordinationModelsTool.MatchesAnyNameFilter("type 01", modelAndTypeNames, instanceNames));
    }
}

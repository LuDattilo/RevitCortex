using System.Collections.Generic;
using System.Threading.Tasks;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Caching;

public class ToolResultCacheTests
{
    private static CortexResult<object> Ok(string payload) =>
        CortexResult<object>.Ok(payload);

    [Fact]
    public void TryGet_OnEmptyCache_ReturnsFalse()
    {
        var cache = new ToolResultCache();

        var hit = cache.TryGet("get_phases", "abc", CacheScope.Document, 1, out var result);

        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void Set_ThenTryGet_SameKey_ReturnsCachedResult()
    {
        var cache = new ToolResultCache();
        var stored = Ok("phase-list-v1");

        cache.Set("get_phases", "abc", CacheScope.Document, 5, stored);
        var hit = cache.TryGet("get_phases", "abc", CacheScope.Document, 5, out var result);

        Assert.True(hit);
        Assert.Same(stored, result);
    }

    [Fact]
    public void TryGet_DocumentScope_StaleVersion_ReturnsFalse()
    {
        var cache = new ToolResultCache();
        cache.Set("get_phases", "abc", CacheScope.Document, 5, Ok("v1"));

        var hit = cache.TryGet("get_phases", "abc", CacheScope.Document, 6, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryGet_SessionScope_IgnoresVersionDrift()
    {
        var cache = new ToolResultCache();
        cache.Set("get_project_info", "abc", CacheScope.Session, 1, Ok("project"));

        var hit = cache.TryGet("get_project_info", "abc", CacheScope.Session, 999, out var result);

        Assert.True(hit);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryGet_DifferentParamHash_ReturnsFalse()
    {
        var cache = new ToolResultCache();
        cache.Set("get_phases", "abc", CacheScope.Document, 1, Ok("v1"));

        var hit = cache.TryGet("get_phases", "xyz", CacheScope.Document, 1, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryGet_DifferentScopeFromStored_ReturnsFalse()
    {
        var cache = new ToolResultCache();
        cache.Set("get_phases", "abc", CacheScope.Document, 1, Ok("v1"));

        // Caller asks for Session, stored as Document → must miss
        var hit = cache.TryGet("get_phases", "abc", CacheScope.Session, 1, out _);

        Assert.False(hit);
    }

    [Fact]
    public void InvalidateScope_Document_RemovesDocumentEntries_KeepsSession()
    {
        var cache = new ToolResultCache();
        cache.Set("get_phases", "k", CacheScope.Document, 1, Ok("doc"));
        cache.Set("get_project_info", "k", CacheScope.Session, 1, Ok("session"));
        cache.Set("get_warnings", "k", CacheScope.Transaction, 1, Ok("tx"));

        cache.InvalidateScope(CacheScope.Document);

        Assert.False(cache.TryGet("get_phases", "k", CacheScope.Document, 1, out _));
        Assert.True(cache.TryGet("get_project_info", "k", CacheScope.Session, 1, out _));
        Assert.True(cache.TryGet("get_warnings", "k", CacheScope.Transaction, 1, out _));
    }

    [Fact]
    public void InvalidateScope_Transaction_RemovesOnlyTransaction()
    {
        var cache = new ToolResultCache();
        cache.Set("a", "k", CacheScope.Document, 1, Ok("doc"));
        cache.Set("b", "k", CacheScope.Transaction, 1, Ok("tx"));
        cache.Set("c", "k", CacheScope.Session, 1, Ok("session"));

        cache.InvalidateScope(CacheScope.Transaction);

        Assert.True(cache.TryGet("a", "k", CacheScope.Document, 1, out _));
        Assert.False(cache.TryGet("b", "k", CacheScope.Transaction, 1, out _));
        Assert.True(cache.TryGet("c", "k", CacheScope.Session, 1, out _));
    }

    [Fact]
    public void InvalidateAll_ClearsEverything()
    {
        var cache = new ToolResultCache();
        cache.Set("a", "k", CacheScope.Document, 1, Ok("doc"));
        cache.Set("b", "k", CacheScope.Session, 1, Ok("session"));
        cache.Set("c", "k", CacheScope.Transaction, 1, Ok("tx"));

        cache.InvalidateAll();

        Assert.False(cache.TryGet("a", "k", CacheScope.Document, 1, out _));
        Assert.False(cache.TryGet("b", "k", CacheScope.Session, 1, out _));
        Assert.False(cache.TryGet("c", "k", CacheScope.Transaction, 1, out _));
    }

    [Fact]
    public void GetStats_ReportsHitsMissesAndPerToolCounts()
    {
        var cache = new ToolResultCache();
        cache.Set("get_phases", "k", CacheScope.Document, 1, Ok("v"));

        // 2 hits
        cache.TryGet("get_phases", "k", CacheScope.Document, 1, out _);
        cache.TryGet("get_phases", "k", CacheScope.Document, 1, out _);
        // 1 miss
        cache.TryGet("get_phases", "missing", CacheScope.Document, 1, out _);
        // 1 miss on different tool
        cache.TryGet("get_warnings", "k", CacheScope.Document, 1, out _);

        var stats = cache.GetStats();

        Assert.Equal(1, stats.EntryCount);
        Assert.Equal(2, stats.TotalHits);
        Assert.Equal(2, stats.TotalMisses);
        Assert.True(stats.PerTool.ContainsKey("get_phases"));
        Assert.Equal(2, stats.PerTool["get_phases"].Hits);
        Assert.Equal(1, stats.PerTool["get_phases"].Misses);
        Assert.Equal(1, stats.PerTool["get_phases"].Entries);
    }

    [Fact]
    public async Task ConcurrentSetAndGet_DoesNotThrow()
    {
        var cache = new ToolResultCache();
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var n = i;
            tasks.Add(Task.Run(() =>
            {
                cache.Set($"tool{n % 5}", $"k{n}", CacheScope.Document, 1, Ok($"v{n}"));
            }));
            tasks.Add(Task.Run(() =>
            {
                cache.TryGet($"tool{n % 5}", $"k{n}", CacheScope.Document, 1, out _);
            }));
            tasks.Add(Task.Run(() => cache.GetStats()));
        }

        await Task.WhenAll(tasks);

        var stats = cache.GetStats();
        Assert.True(stats.EntryCount > 0);
    }
}

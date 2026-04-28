using System.Collections.Generic;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Caching;

/// <summary>
/// Tests for the cache-invalidation logic. The actual Revit-event subscription
/// lives in Plugin.Caching.DocumentChangeWatcher and is a thin forwarding
/// shell — its only job is to call into <see cref="CacheInvalidator"/>, which
/// is what we test here.
/// </summary>
public class DocumentChangeWatcherTests
{
    private class RecordingCache : IToolResultCache
    {
        public List<CacheScope> InvalidatedScopes { get; } = new();
        public int InvalidateAllCount { get; private set; }

        public bool TryGet(string toolName, string paramHash, CacheScope scope,
            long currentDocVersion, out CortexResult<object> result)
        {
            result = null!;
            return false;
        }

        public void Set(string toolName, string paramHash, CacheScope scope,
            long currentDocVersion, CortexResult<object> result) { }

        public void InvalidateScope(CacheScope scope) => InvalidatedScopes.Add(scope);
        public void InvalidateAll() => InvalidateAllCount++;
        public CacheStats GetStats() => new CacheStats();
    }

    private static (CortexSession session, RecordingCache cache) NewSession()
    {
        var cache = new RecordingCache();
        var session = new CortexSession(new SessionStore(), cache);
        return (session, cache);
    }

    [Fact]
    public void OnDocumentChanged_InvalidatesDocumentAndTransaction_BumpsVersion()
    {
        var (session, cache) = NewSession();
        var inv = new CacheInvalidator(session);
        var v0 = session.DocumentVersion;

        inv.OnDocumentChanged();

        Assert.Contains(CacheScope.Document, cache.InvalidatedScopes);
        Assert.Contains(CacheScope.Transaction, cache.InvalidatedScopes);
        Assert.DoesNotContain(CacheScope.Session, cache.InvalidatedScopes);
        Assert.True(session.DocumentVersion > v0);
    }

    [Fact]
    public void OnDocumentSaved_InvalidatesTransactionOnly_DoesNotBumpVersion()
    {
        var (session, cache) = NewSession();
        var inv = new CacheInvalidator(session);
        var v0 = session.DocumentVersion;

        inv.OnDocumentSaved();

        Assert.Equal(new[] { CacheScope.Transaction }, cache.InvalidatedScopes);
        Assert.Equal(v0, session.DocumentVersion);
    }

    [Fact]
    public void OnDocumentSynchronized_InvalidatesTransactionOnly()
    {
        var (session, cache) = NewSession();
        var inv = new CacheInvalidator(session);

        inv.OnDocumentSynchronized();

        Assert.Equal(new[] { CacheScope.Transaction }, cache.InvalidatedScopes);
    }

    [Fact]
    public void DocumentChanged_BumpsVersion_StaleEntriesMissOnNextLookup()
    {
        // End-to-end: real ToolResultCache + CacheInvalidator together.
        var session = new CortexSession(new SessionStore(), new ToolResultCache());
        var inv = new CacheInvalidator(session);

        session.Cache.Set("get_phases", "h", CacheScope.Document,
            session.DocumentVersion, CortexResult<object>.Ok("v1"));
        Assert.True(session.Cache.TryGet("get_phases", "h", CacheScope.Document,
            session.DocumentVersion, out _));

        inv.OnDocumentChanged();

        Assert.False(session.Cache.TryGet("get_phases", "h", CacheScope.Document,
            session.DocumentVersion, out _));
    }
}

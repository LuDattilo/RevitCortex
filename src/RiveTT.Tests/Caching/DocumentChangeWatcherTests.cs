using System.Collections.Generic;
using RiveTT.Core.Caching;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using Xunit;

namespace RiveTT.Tests.Caching;

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

        public bool TryGet(string toolName, string paramHash, CacheScope scope,
            long currentDocVersion, out CortexResult<object> result, out long estimatedBytes)
        {
            estimatedBytes = 0;
            return TryGet(toolName, paramHash, scope, currentDocVersion, out result);
        }

        public void Set(string toolName, string paramHash, CacheScope scope,
            long currentDocVersion, CortexResult<object> result, long? knownBytes = null) { }

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
    public void OnDocumentSaved_InvalidatesTransactionAndDocument_DoesNotBumpVersion()
    {
        var (session, cache) = NewSession();
        var inv = new CacheInvalidator(session);
        var v0 = session.DocumentVersion;

        inv.OnDocumentSaved();

        // Document scope must go too: it holds document identity (path, title), and a
        // Save As left get_project_info answering with the pre-save path.
        Assert.Contains(CacheScope.Transaction, cache.InvalidatedScopes);
        Assert.Contains(CacheScope.Document, cache.InvalidatedScopes);
        Assert.DoesNotContain(CacheScope.Session, cache.InvalidatedScopes);
        Assert.Equal(v0, session.DocumentVersion);
    }

    [Fact]
    public void OnActiveDocumentReplaced_InvalidatesEverything_AndBumpsVersion()
    {
        var (session, cache) = NewSession();
        var inv = new CacheInvalidator(session);
        var v0 = session.DocumentVersion;

        inv.OnActiveDocumentReplaced();

        // Save As changes which file the session describes, so even Session-scope
        // entries ("immutable for the session") are about the wrong document now.
        Assert.Equal(1, cache.InvalidateAllCount);
        Assert.True(session.DocumentVersion > v0);
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

        session.Cache.Set("list_phases", "h", CacheScope.Document,
            session.DocumentVersion, CortexResult<object>.Ok("v1"));
        Assert.True(session.Cache.TryGet("list_phases", "h", CacheScope.Document,
            session.DocumentVersion, out _));

        inv.OnDocumentChanged();

        Assert.False(session.Cache.TryGet("list_phases", "h", CacheScope.Document,
            session.DocumentVersion, out _));
    }
}

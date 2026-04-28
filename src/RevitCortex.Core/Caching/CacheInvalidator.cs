using RevitCortex.Core.Session;

namespace RevitCortex.Core.Caching;

/// <summary>
/// Pure-logic cache invalidation triggers, decoupled from Revit event types so
/// it can be unit-tested without the Revit runtime. Plugin-side
/// <c>DocumentChangeWatcher</c> hooks Revit events and forwards them here.
/// </summary>
public class CacheInvalidator
{
    private readonly CortexSession _session;

    public CacheInvalidator(CortexSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Model state changed (any DocumentChanged event from Revit). Bumps the
    /// session's DocumentVersion and drops Document + Transaction entries.
    /// Session entries are preserved.
    /// </summary>
    public void OnDocumentChanged()
    {
        _session.BumpDocumentVersion();
        _session.Cache.InvalidateScope(CacheScope.Document);
        _session.Cache.InvalidateScope(CacheScope.Transaction);
    }

    /// <summary>
    /// Document persisted (no model change). Drops only Transaction entries.
    /// </summary>
    public void OnDocumentSaved()
    {
        _session.Cache.InvalidateScope(CacheScope.Transaction);
    }

    /// <summary>
    /// Sync-with-central completed. Same effect as Save for cache purposes.
    /// </summary>
    public void OnDocumentSynchronized()
    {
        _session.Cache.InvalidateScope(CacheScope.Transaction);
    }
}

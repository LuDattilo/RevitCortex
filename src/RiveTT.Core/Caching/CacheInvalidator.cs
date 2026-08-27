using RiveTT.Core.Session;

namespace RiveTT.Core.Caching;

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
    /// Document persisted. Drops Transaction AND Document entries.
    ///
    /// Document scope used to survive a save, which was wrong for anything holding
    /// document identity: after Save As, get_project_info replied from cache with
    /// the OLD path in 0 ms, so a caller checking the result of its own Save As saw
    /// the previous file and concluded the save had failed.
    /// </summary>
    public void OnDocumentSaved()
    {
        _session.Cache.InvalidateScope(CacheScope.Transaction);
        _session.Cache.InvalidateScope(CacheScope.Document);
    }

    /// <summary>
    /// Document saved under a new path (Save As), or the active document changed.
    /// Everything cached describes the previous file — including Session-scope
    /// entries, which are only immutable for as long as the document is the same.
    /// </summary>
    public void OnActiveDocumentReplaced()
    {
        _session.BumpDocumentVersion();
        _session.Cache.InvalidateAll();
    }

    /// <summary>
    /// Sync-with-central completed. Same effect as Save for cache purposes.
    /// </summary>
    public void OnDocumentSynchronized()
    {
        _session.Cache.InvalidateScope(CacheScope.Transaction);
    }
}

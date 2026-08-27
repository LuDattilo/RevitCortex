using System;
using System.Threading;
using RiveTT.Core.Caching;
using RiveTT.Core.Discovery;
using RiveTT.Core.Security;

namespace RiveTT.Core.Session;

/// <summary>
/// Facade passed to every tool. Provides access to shared state,
/// document capabilities, and detected locale. Does NOT hold a
/// direct Revit Document reference — that lives in the Plugin layer.
/// Core has no Revit dependency.
/// </summary>
public class CortexSession
{
    public ISessionStore Store { get; }
    public DocumentCapabilities Capabilities { get; private set; }
    public string DetectedLocale { get; private set; }

    /// <summary>
    /// The ribbon write lock. Consulted by the router before any tool that is not
    /// classified read-only, and reported in every response as
    /// execution.writesAllowed. Defaults to allowed so that hosts without a
    /// ribbon (tests, the pipe alone) behave as before; the Revit plugin locks it
    /// explicitly on startup.
    /// </summary>
    public WriteAccessPolicy WriteAccess { get; } = new WriteAccessPolicy();

    /// <summary>
    /// Tool-result cache. Always non-null. Plugin wires invalidation to Revit
    /// document events; in tests a default cache is created automatically.
    /// </summary>
    public IToolResultCache Cache { get; }

    /// <summary>
    /// Monotonic counter, bumped on each Revit DocumentChanged. Read by the
    /// router when consulting <see cref="Cache"/>; bumped by the Plugin's
    /// DocumentChangeWatcher. Tests can bump it directly via <see cref="BumpDocumentVersion"/>.
    /// </summary>
    public long DocumentVersion => Interlocked.Read(ref _documentVersion);
    private long _documentVersion;

    /// <summary>
    /// Atomically increment <see cref="DocumentVersion"/>. Returns the new value.
    /// </summary>
    public long BumpDocumentVersion() => Interlocked.Increment(ref _documentVersion);


    public CortexSession(ISessionStore store)
        : this(store, new ToolResultCache())
    {
    }

    public CortexSession(ISessionStore store, IToolResultCache cache)
    {
        Store = store;
        Cache = cache;
        Capabilities = new DocumentCapabilities();
        DetectedLocale = "en";
    }

    public void Reinitialize(DocumentCapabilities capabilities, string locale)
    {
        // WriteAccess is NOT touched here: closing or switching a document must
        // not silently hand back write permission a human had taken away.
        Store.Clear();
        Capabilities = capabilities;
        DetectedLocale = locale;

        // Switching/reopening a document invalidates everything that's not
        // session-immutable. Session entries (e.g. project_info if we cached it
        // for the SAME doc) would be stale here too — be conservative and
        // drop them all on document boundary.
        Cache.InvalidateAll();
        BumpDocumentVersion();
    }

    /// <summary>
    /// RiveTT has one always-on automatic mode. Existing tools call this
    /// compatibility method, but it deliberately never creates UI or blocks an
    /// operation. Transactions, audit logging and the code sandbox remain active.
    /// </summary>
    public bool RequestConfirmation(
        string action,
        int elementCount,
        string? description = null,
        bool critical = false)
    {
        return true;
    }
}

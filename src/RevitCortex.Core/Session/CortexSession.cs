using System;
using System.Threading;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Discovery;

namespace RevitCortex.Core.Session;

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

    /// <summary>
    /// Confirmation callback for destructive operations.
    /// Set by Plugin layer to show TaskDialog. Tools call this before
    /// destructive actions. Returns true to proceed, false to cancel.
    /// Parameters: (actionVerb, elementCount, description) → bool? (null = "Yes to All" was clicked).
    /// If null callback, operation proceeds without confirmation.
    /// </summary>
    public Func<string, int, string?, bool?>? ConfirmAction { get; set; }

    /// <summary>
    /// When true, all subsequent confirmations are auto-approved until timeout.
    /// Set by "Yes to All" in the confirmation dialog. Expires after 120 seconds.
    /// The flag+timestamp pair must be read/written as a unit, otherwise a
    /// reader on a different thread can see the flag flipped to true while
    /// the timestamp is still the default DateTime.MinValue — which makes
    /// <c>(now - timestamp).TotalSeconds</c> huge and the check misfires.
    /// </summary>
    public bool ApproveAll
    {
        get
        {
            lock (_approveAllLock)
            {
                return _approveAll && (DateTime.UtcNow - _approveAllTimestamp).TotalSeconds < 120;
            }
        }
        set
        {
            lock (_approveAllLock)
            {
                _approveAll = value;
                if (value) _approveAllTimestamp = DateTime.UtcNow;
            }
        }
    }
    private readonly object _approveAllLock = new();
    private bool _approveAll;
    private DateTime _approveAllTimestamp;

    /// <summary>
    /// When true, all subsequent confirmations are auto-approved. Set by "Auto"
    /// in the confirmation dialog. Cleared by the user (Stop Auto / closing the
    /// Auto mode window), or on document close (Reinitialize). Unlike ApproveAll
    /// (a fixed 120 s wall-clock window), AutoMode has no timeout and continues
    /// until an explicit stop path.
    /// </summary>
    public bool AutoMode
    {
        get { lock (_approveAllLock) { return _autoMode; } }
        set { lock (_approveAllLock) { _autoMode = value; } }
    }
    private bool _autoMode;

    /// <summary>
    /// Raised every time a destructive operation is auto-approved because
    /// AutoMode is on. Core stays Revit-agnostic: UI layers can use this for
    /// status updates without changing the lifetime of Auto mode.
    /// </summary>
    public event Action? AutoModeActivity;

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
        Store.Clear();
        Capabilities = capabilities;
        DetectedLocale = locale;

        // Switching/reopening a document invalidates everything that's not
        // session-immutable. Session entries (e.g. project_info if we cached it
        // for the SAME doc) would be stale here too — be conservative and
        // drop them all on document boundary.
        Cache.InvalidateAll();
        BumpDocumentVersion();
        AutoMode = false;
    }

    /// <summary>
    /// Ask user to confirm a destructive operation. Returns true if confirmed or no callback set.
    /// If "Yes to All" was previously clicked, auto-approves for 120 s.
    /// If "Auto" mode is active, auto-approves indefinitely until the user clicks Stop Auto.
    /// Critical confirmations ignore automatic approval modes and require a real callback.
    /// </summary>
    /// <param name="action">Action verb: "delete", "rename", "replace compound structure", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional detailed description of what will happen.</param>
    /// <param name="critical">If true, bypasses ApproveAll and fails closed when no callback is available.</param>
    public bool RequestConfirmation(
        string action,
        int elementCount,
        string? description = null,
        bool critical = false)
    {
        if (elementCount <= 0) return true;
        if (!critical && AutoMode)
        {
            // Auto-approved by Auto mode. Signal activity so the UI keeps the
            // Auto mode window alive through this burst of operations.
            AutoModeActivity?.Invoke();
            return true;
        }
        if (!critical && ApproveAll) return true;
        if (critical && ConfirmAction == null) return false;

        var result = ConfirmAction?.Invoke(action, elementCount, description);
        if (result == null)
        {
            // null = "Yes to All" was clicked. Critical confirmations may
            // proceed for this explicit click, but must not arm future approval.
            if (!critical) ApproveAll = true;
            return true;
        }
        if (result == false) return false;

        // Check for Auto sentinel: ConfirmAction returns false with a special
        // convention would be complex — instead ConfirmationHelper sets AutoMode
        // directly on the session. Check again after the dialog.
        if (!critical && AutoMode) return true;

        return result.Value;
    }

    /// <summary>
    /// Resets both transient confirmation modes. Use this only for explicit stop
    /// or session-boundary paths; the router clears ApproveAll directly after
    /// each tool so AutoMode can remain active.
    /// </summary>
    public void ResetApproveAll()
    {
        ApproveAll = false;
        AutoMode = false;
    }
}

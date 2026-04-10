using System;
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
    /// </summary>
    public bool ApproveAll
    {
        get => _approveAll && (DateTime.UtcNow - _approveAllTimestamp).TotalSeconds < 120;
        set
        {
            _approveAll = value;
            if (value) _approveAllTimestamp = DateTime.UtcNow;
        }
    }
    private bool _approveAll;
    private DateTime _approveAllTimestamp;

    public CortexSession(ISessionStore store)
    {
        Store = store;
        Capabilities = new DocumentCapabilities();
        DetectedLocale = "en";
    }

    public void Reinitialize(DocumentCapabilities capabilities, string locale)
    {
        Store.Clear();
        Capabilities = capabilities;
        DetectedLocale = locale;
    }

    /// <summary>
    /// Ask user to confirm a destructive operation. Returns true if confirmed or no callback set.
    /// If "Yes to All" was previously clicked, auto-approves without showing dialog.
    /// </summary>
    /// <param name="action">Action verb: "delete", "rename", "replace compound structure", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional detailed description of what will happen.</param>
    public bool RequestConfirmation(string action, int elementCount, string? description = null)
    {
        if (elementCount <= 0) return true;
        if (ApproveAll) return true;

        var result = ConfirmAction?.Invoke(action, elementCount, description);
        if (result == null)
        {
            // null = "Yes to All" was clicked
            ApproveAll = true;
            return true;
        }
        return result.Value;
    }

    /// <summary>
    /// Resets the "Approve All" state. Called when a batch operation completes.
    /// </summary>
    public void ResetApproveAll()
    {
        ApproveAll = false;
    }
}

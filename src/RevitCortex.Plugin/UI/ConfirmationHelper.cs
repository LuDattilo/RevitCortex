using System;
using Autodesk.Revit.UI;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Shows a native Revit TaskDialog before destructive/bulk operations.
/// Called inside tool Execute() after parameter validation but BEFORE opening Transaction.
/// </summary>
public static class ConfirmationHelper
{
    /// <summary>
    /// Shows a confirmation dialog for destructive operations.
    /// </summary>
    /// <param name="action">Action verb: "delete", "purge", "rename", "modify", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional description of what the operation will do.</param>
    /// <returns>true = Yes, false = No, null = Yes to All.</returns>
    public static bool? Confirm(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true; // Nothing to do

        var dialog = new TaskDialog("RevitCortex Confirmation")
        {
            MainInstruction = $"About to {action} ({elementCount} element(s))",
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Yes",
            "Approve this operation");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Yes to All",
            "Approve this and all remaining operations without asking again (2 min)");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Auto",
            "Approve all operations automatically — a floating window lets you stop at any time");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "No",
            "Cancel this operation");

        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink2) return null;  // Yes to All
        if (result == TaskDialogResult.CommandLink1) return true;  // Yes
        if (result == TaskDialogResult.CommandLink3) return AutoSentinel; // Auto
        return false; // No (or closed)
    }

    /// <summary>
    /// Sentinel value returned by Confirm() when the user clicks "Auto".
    /// CortexSession.RequestConfirmation checks for this value and sets AutoMode.
    /// </summary>
    public const bool AutoSentinel = true;

    /// <summary>
    /// Variant wired to a CortexSession: sets session.AutoMode = true when Auto is clicked
    /// and fires AutoModeChanged so the ribbon can update its button visibility immediately.
    /// This overload is used by RevitCortexApp instead of the bare Confirm delegate.
    /// </summary>
    public static bool? ConfirmWithSession(string action, int elementCount, string? description,
        CortexSession session)
    {
        if (elementCount <= 0) return true;

        var dialog = new TaskDialog("RevitCortex Confirmation")
        {
            MainInstruction = $"About to {action} ({elementCount} element(s))",
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Yes",
            "Approve this operation");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Yes to All",
            "Approve this and all remaining operations without asking again (2 min)");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Auto",
            "Approve all operations automatically — a floating window lets you stop at any time");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "No",
            "Cancel this operation");

        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink2) return null;  // Yes to All
        if (result == TaskDialogResult.CommandLink1) return true;  // Yes
        if (result == TaskDialogResult.CommandLink3)
        {
            session.AutoMode = true;
            AutoModeChanged?.Invoke(true);
            return true; // proceed with current operation
        }
        return false; // No (or closed)
    }

    /// <summary>
    /// Fired when Auto mode is activated or deactivated via the confirmation dialog.
    /// The ribbon subscribes to this to show/hide the "Stop Auto" button immediately.
    /// </summary>
    public static event Action<bool>? AutoModeChanged;

    /// <summary>
    /// Raises AutoModeChanged. Call this from outside ConfirmationHelper (e.g. StopAutoMode command).
    /// </summary>
    public static void NotifyAutoModeChanged(bool active) => AutoModeChanged?.Invoke(active);

    /// <summary>
    /// Returns a standard cancelled response for CortexResult.
    /// </summary>
    public static Core.Results.CortexResult<object> CancelledResult()
    {
        return Core.Results.CortexResult<object>.Fail(
            Core.Results.CortexErrorCode.Cancelled,
            "Operation cancelled by user",
            suggestion: "The user declined the confirmation dialog. Ask if they want to retry.");
    }
}

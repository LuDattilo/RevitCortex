using Autodesk.Revit.UI;

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
            "Approve this and all remaining operations without asking again");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "No",
            "Cancel this operation");

        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink2) return null;  // Yes to All
        if (result == TaskDialogResult.CommandLink1) return true;  // Yes
        return false; // No (or closed)
    }

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

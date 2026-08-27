using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RiveTT.Tools.Project;

/// <summary>
/// Auto-answers the modal dialogs Revit raises while opening a document.
///
/// Opening a real project routinely stops on a task dialog — "Revit could not
/// find or read N references" is the common one, raised when a CAD/DWF link has
/// moved. That dialog is modal on the UI thread, which is the same thread the
/// ExternalEvent handler runs on: nobody is there to click it, the pipe waits,
/// and the open only completes if a human intervenes. Observed exactly that when
/// opening the sandbox model.
///
/// Every dialog raised during the open is therefore answered here, and every
/// answer is REPORTED in the response. Clicking a dialog on the caller's behalf
/// is a decision; it must never be invisible.
/// </summary>
internal sealed class OpenDialogAutoAnswer : IDisposable
{
    // Revit TaskDialogResult values. CommandLink2 is "the second link", which on
    // the unresolved-references dialog is "ignore and continue opening".
    private const int ResultOk = 1;
    private const int ResultCancel = 2;
    private const int ResultCommandLink2 = 1002;

    private readonly UIApplication? _uiApplication;
    private readonly List<object> _answered = new();

    internal OpenDialogAutoAnswer(UIApplication? uiApplication)
    {
        _uiApplication = uiApplication;
        if (_uiApplication != null)
            _uiApplication.DialogBoxShowing += OnDialogBoxShowing;
    }

    internal IReadOnlyList<object> Answered => _answered;

    internal string[] Warnings => _answered.Count == 0
        ? Array.Empty<string>()
        : new[]
        {
            _answered.Count + " Revit dialog(s) were answered automatically during the open — see " +
            "dismissedDialogs. Anything that needed a real decision (missing links, an upgrade) should " +
            "be reviewed in Revit."
        };

    private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs args)
    {
        var dialogId = args.DialogId ?? "";
        var taskDialog = args as TaskDialogShowingEventArgs;
        var message = taskDialog?.Message ?? "";

        var result = ChooseResult(dialogId, message, taskDialog != null, out var reason);
        try
        {
            args.OverrideResult(result);
        }
        catch
        {
            // A dialog that refuses the override is left to Revit; recording it
            // still tells the caller where the open stopped.
            reason += " (override refused by Revit)";
        }

        _answered.Add(new
        {
            dialogId,
            isTaskDialog = taskDialog != null,
            answeredWith = result,
            reason,
            message = message.Length > 200 ? message.Substring(0, 200) : message
        });
    }

    /// <summary>
    /// Known dialogs get their documented answer; anything else is cancelled,
    /// which is the least destructive way to free the UI thread.
    /// </summary>
    private static int ChooseResult(string dialogId, string message, bool isTaskDialog, out string reason)
    {
        var haystack = (dialogId + " " + message).ToLowerInvariant();

        // Matched on the dialog id first (language-independent) and on the message
        // second, because the message is localized.
        if (haystack.Contains("unresolved") || haystack.Contains("referenc"))
        {
            reason = "unresolved references: ignore and continue opening the project";
            return ResultCommandLink2;
        }

        if (!isTaskDialog)
        {
            reason = "plain message box: acknowledged";
            return ResultOk;
        }

        reason = "unknown task dialog: cancelled to keep the UI thread free";
        return ResultCancel;
    }

    public void Dispose()
    {
        if (_uiApplication == null) return;
        try { _uiApplication.DialogBoxShowing -= OnDialogBoxShowing; } catch { }
    }
}

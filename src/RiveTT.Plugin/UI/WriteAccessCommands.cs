using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RiveTT.Core.Hosting;

namespace RiveTT.Plugin.UI;

/// <summary>
/// Keeps the panel usable with no document open. Revit disables an external
/// command by default when zero documents are loaded, which would have hidden
/// the write lock exactly when the session starts.
/// </summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}

[Transaction(TransactionMode.Manual)]
public sealed class LockWritesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonWriteAccess.Apply(writesAllowed: false);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class AllowWritesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonWriteAccess.Apply(writesAllowed: true);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ShowStatusCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var app = RiveTTApp.Instance;
        var policy = app?.Session?.WriteAccess;
        var auditPath = CortexEnvironment.Current.AuditLogPath;
        var document = commandData?.Application?.ActiveUIDocument?.Document;

        var version = typeof(ShowStatusCommand).Assembly.GetName().Version?.ToString() ?? "inconnue";
        var writeState = policy == null
            ? "inconnu"
            : policy.WritesAllowed ? "écriture autorisée" : "lecture seule";
        var since = policy == null
            ? string.Empty
            : $" (depuis {policy.ChangedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)}, " +
              $"origine : {policy.ChangedBy})";

        var dialog = new TaskDialog("RiveTT")
        {
            MainInstruction = $"Mode : {writeState}",
            MainContent =
                $"Version : {version}\n" +
                $"Canal nommé : {(app?.IsServiceRunning == true ? "actif" : "inactif")}\n" +
                $"Outils publiés : {app?.Router?.TotalToolCount.ToString(CultureInfo.CurrentCulture) ?? "0"}\n" +
                $"Document actif : {document?.Title ?? "aucun"}\n" +
                $"Mode d'écriture{since}\n\n" +
                "Le mode se change avec les boutons Lecture seule / Écriture de ce panneau. " +
                "Aucun outil MCP ne peut le faire à votre place.",
            CommonButtons = TaskDialogCommonButtons.Close,
            DefaultButton = TaskDialogResult.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
            "Ouvrir le journal d'audit", auditPath);

        if (dialog.Show() == TaskDialogResult.CommandLink1)
            RevealInExplorer(auditPath);

        return Result.Succeeded;
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            // The log is created on the first tool call, so it may not exist yet:
            // fall back to its folder rather than failing on a missing file.
            var target = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            TaskDialog.Show("RiveTT", $"Ouverture impossible : {exception.Message}");
        }
    }
}

internal static class RibbonWriteAccess
{
    /// <summary>
    /// Applies the toggle. Clicking the mode that is already current is a no-op,
    /// so re-clicking never produces a dialog or an audit entry.
    /// </summary>
    public static void Apply(bool writesAllowed)
    {
        var policy = RiveTTApp.Instance?.Session?.WriteAccess;
        if (policy == null)
        {
            TaskDialog.Show("RiveTT",
                "Le connecteur n'est pas démarré : le mode ne peut pas être changé.");
            return;
        }

        if (!policy.Set(writesAllowed, "ribbon")) return;

        System.Diagnostics.Trace.WriteLine(
            $"[RiveTT] Write access set to {writesAllowed} from the ribbon.");
    }
}

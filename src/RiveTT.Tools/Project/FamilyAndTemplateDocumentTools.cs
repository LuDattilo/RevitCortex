using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Opens a .rfa family file and activates it in Revit, for direct visual editing
/// (as opposed to load_family, which loads a family INTO the active project).
///
/// Document.EditFamily does NOT deadlock from this connector's ExternalEvent
/// dispatcher — a documentation claim that made this tool unreachable, corrected
/// as part of P4.1 in PLAN_CORRECTION.md. All four document-opening paths (a
/// background .rfa, EditFamily on a project family, an activated .rfa, an
/// activated project family) were measured working on 26/08/2026 (see PLAN_CORRECTION.md
/// Annex A).
/// </summary>
[ToolSafety(false, false)]
public sealed class OpenFamilyTool : ICortexTool
{
    public string Name => "open_family";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Opens a .rfa family file and makes it the active document in Revit, for visual editing (type " +
        "parameters, geometry). The active document CHANGES: every later tool call targets the family until " +
        "you switch back with open_document. The family stays open — call close_document when done, or it " +
        "accumulates for the rest of the session. To load a family INTO the current project instead, use " +
        "load_family.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>()
                       ?? input["path"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required and was not provided",
                suggestion: "Pass filePath as an absolute .rfa path.");

        if (!Path.IsPathFullyQualified(filePath) ||
            !filePath!.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"filePath must be an absolute path ending in .rfa (received: {filePath})");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"File not found: {filePath}");

        var currentDocument = session.Store.Get<object>("activeDocument") as Document;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: would open and activate '{Path.GetFileName(filePath)}' as a family " +
                          "document. This changes the active document.",
                filePath,
                fileSizeBytes = new FileInfo(filePath).Length,
                currentDocument = currentDocument?.PathName,
                currentDocumentHasUnsavedChanges = currentDocument?.IsModified ?? false,
                warnings = currentDocument?.IsModified == true
                    ? new[] { "The current document has unsaved changes. Save it first: switching documents does not save it." }
                    : Array.Empty<string>()
            });
        }

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        if (uiApplication == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No UIApplication in session, so no document can be activated",
                suggestion: "Activate any view in Revit once, then retry.");

        var returnToPath = currentDocument?.PathName;

        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
        try
        {
            uiApplication.OpenAndActivateDocument(filePath);
            var opened = uiApplication.ActiveUIDocument?.Document;

            return CortexResult<object>.Ok(new
            {
                message = $"Opened and activated the family '{opened?.Title ?? Path.GetFileName(filePath)}'. " +
                          "The active document has changed — every later tool call targets this family until " +
                          "you switch back." +
                          (dialogs.Answered.Count > 0
                              ? $" {dialogs.Answered.Count} Revit dialog(s) were answered automatically."
                              : ""),
                path = opened?.PathName ?? filePath,
                title = opened?.Title,
                isFamilyDocument = opened?.IsFamilyDocument ?? true,
                returnToPath,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not open and activate the family: {exception.Message}",
                suggestion: "Close any open dialog in Revit and retry.");
        }
    }
}

/// <summary>
/// Opens a .rte template file FOR EDITING — as opposed to create_document, which
/// only reads a template to seed a new project, and never edits the template
/// itself.
/// </summary>
[ToolSafety(false, false)]
public sealed class OpenTemplateTool : ICortexTool
{
    public string Name => "open_template";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Opens a .rte template file and makes it the active document in Revit, to edit the TEMPLATE itself " +
        "(levels, types, view templates that ship with every new project). To start a new PROJECT from a " +
        "template instead, use create_document — that reads the template without touching it. The active " +
        "document changes: every later tool call targets the template until you switch back.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var filePath = input["filePath"]?.Value<string>()
                       ?? input["path"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required and was not provided",
                suggestion: "Pass filePath as an absolute .rte path.");

        if (!Path.IsPathFullyQualified(filePath) ||
            !filePath!.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"filePath must be an absolute path ending in .rte (received: {filePath})");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"File not found: {filePath}");

        var currentDocument = session.Store.Get<object>("activeDocument") as Document;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: would open and activate '{Path.GetFileName(filePath)}' as an editable " +
                          "template document. This changes the active document.",
                filePath,
                fileSizeBytes = new FileInfo(filePath).Length,
                currentDocument = currentDocument?.PathName,
                currentDocumentHasUnsavedChanges = currentDocument?.IsModified ?? false,
                warnings = currentDocument?.IsModified == true
                    ? new[] { "The current document has unsaved changes. Save it first: switching documents does not save it." }
                    : Array.Empty<string>()
            });
        }

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        if (uiApplication == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No UIApplication in session, so no document can be activated",
                suggestion: "Activate any view in Revit once, then retry.");

        var returnToPath = currentDocument?.PathName;

        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
        try
        {
            uiApplication.OpenAndActivateDocument(filePath);
            var opened = uiApplication.ActiveUIDocument?.Document;

            return CortexResult<object>.Ok(new
            {
                message = $"Opened and activated the template '{opened?.Title ?? Path.GetFileName(filePath)}'. " +
                          "Changes made here and saved (save_document) modify the template file itself." +
                          (dialogs.Answered.Count > 0
                              ? $" {dialogs.Answered.Count} Revit dialog(s) were answered automatically."
                              : ""),
                path = opened?.PathName ?? filePath,
                title = opened?.Title,
                returnToPath,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not open and activate the template: {exception.Message}",
                suggestion: "Close any open dialog in Revit and retry.");
        }
    }
}

/// <summary>
/// Closes an open document (project, family, or template) — without this,
/// open_family/open_template leave documents open for the rest of the Revit
/// session (measured: three residual family documents by the end of one
/// campaign, PLAN_CORRECTION.md Annex A.5).
///
/// Document.Close(false) throws "The active document may not be closed from the
/// API" when called on the ACTIVE document (measured 27/08/2026, PLAN_CORRECTION.md
/// P1.4 addendum) — this is a real Revit API constraint, not a bug to route
/// around with a background thread. When the target is active, this tool
/// activates another already-open document first (the same two-step sequence
/// that measurement used), then closes the now-inactive target. When the target
/// is the ONLY open document, there is nothing to activate instead, and the tool
/// refuses rather than guess.
/// </summary>
[ToolSafety(false, true)]
public sealed class CloseDocumentTool : ICortexTool
{
    public string Name => "close_document";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Closes an open document (project, family, or template). Defaults to the active document; pass " +
        "filePath to close a different one that is open in the background. saveModified controls whether " +
        "unsaved changes are saved first (default: false, changes are discarded). Closing the ACTIVE document " +
        "requires another open document to switch to first — if none is open, the call is refused rather than " +
        "guessed at.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        var filePath = input["filePath"]?.Value<string>() ?? input["path"]?.Value<string>();
        var saveModified = input["saveModified"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        var activeDocument = uiApplication?.ActiveUIDocument?.Document;

        Document? target;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            target = activeDocument;
            if (target == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "filePath was not provided and there is no active document to close",
                    suggestion: "Pass filePath to close a specific open document.");
        }
        else
        {
            target = FindOpenDocument(application, filePath!);
            if (target == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"No open document matches: {filePath}",
                    suggestion: "The path must match an already-open document exactly. " +
                                "openDocuments in a dryRun call lists what is currently open.");
        }

        var isActive = activeDocument != null && ReferenceEquals(activeDocument, target);
        var swapCandidate = isActive ? FindSwapCandidate(application, target!) : null;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = !isActive
                    ? $"DryRun: would close '{target!.Title}' directly (not the active document)."
                    : swapCandidate != null
                        ? $"DryRun: '{target!.Title}' is the active document — would activate " +
                          $"'{swapCandidate.Title}' first, then close it."
                        : $"DryRun: '{target!.Title}' is the active document and the ONLY open document — " +
                          "cannot close it via the API. Open another document first.",
                path = target.PathName,
                title = target.Title,
                isActive,
                hasUnsavedChanges = target.IsModified,
                saveModified,
                wouldSwapToPath = swapCandidate?.PathName,
                canClose = !isActive || swapCandidate != null,
                openDocuments = DocumentLifecycleSupport.DescribeOpenDocuments(application)
            });
        }

        if (isActive && swapCandidate == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"'{target!.Title}' is the active document and the only one open. Revit's API refuses to " +
                "close the active document directly (The active document may not be closed from the API).",
                suggestion: "Open or activate another document first (open_document, open_family, or " +
                            "open_template), then retry.");

        try
        {
            var closedTitle = target!.Title;
            var closedPath = target.PathName;
            var swappedToPath = (string?)null;

            if (isActive)
            {
                using var dialogs = new OpenDialogAutoAnswer(uiApplication);
                uiApplication!.OpenAndActivateDocument(swapCandidate!.PathName);
                swappedToPath = swapCandidate.PathName;
            }

            var closed = target.Close(saveModified);
            if (!closed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit refused to close '{closedTitle}' (Document.Close returned false).",
                    suggestion: saveModified
                        ? "Check the document can be saved to its current path."
                        : "The document may have unsaved changes; pass saveModified=true to save first.");

            return CortexResult<object>.Ok(new
            {
                message = swappedToPath != null
                    ? $"Activated '{swapCandidate!.Title}', then closed '{closedTitle}'."
                    : $"Closed '{closedTitle}'.",
                closedPath,
                closedTitle,
                savedBeforeClosing = saveModified,
                swappedToPath
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not close the document: {exception.Message}");
        }
    }

    private static Document? FindOpenDocument(Application application, string filePath)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (string.Equals(document.PathName, filePath, StringComparison.OrdinalIgnoreCase))
                    return document;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Another open document with a real path to reactivate — a document never
    /// saved (empty PathName, e.g. one just opened via EditFamily and not yet
    /// SaveAs'd) cannot be re-activated by path, per PLAN_CORRECTION.md Annex A.2.
    /// </summary>
    private static Document? FindSwapCandidate(Application application, Document excluding)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (ReferenceEquals(document, excluding)) continue;
                if (string.IsNullOrEmpty(document.PathName)) continue;
                return document;
            }
        }
        catch { }
        return null;
    }
}

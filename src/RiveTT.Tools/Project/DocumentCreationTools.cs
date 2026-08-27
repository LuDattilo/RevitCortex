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
/// Document lifecycle: create a project from a template, and open/activate a
/// project file.
///
/// Why these exist now: the connector documented both as impossible because
/// "UIApplication.OpenAndActivateDocument cannot run inside an API event
/// handler". That restriction is real for API *event* handlers (Idling,
/// DocumentChanged) but not for an ExternalEvent handler, which is precisely
/// the context every RiveTT tool runs in. Autodesk's own guidance (Arnošt
/// Löbel, via The Building Coder) is that switching to an External Event is
/// "both supported and safe" for open-and-activate. Application.NewProjectDocument
/// needs no UI at all: it returns an in-memory document that is saved to disk.
///
/// Consequence for callers: "new project" no longer has to mean
/// save_as_document, which duplicates the open model with all its history.
/// </summary>
internal static class DocumentLifecycleSupport
{
    internal static Application? ResolveApplication(CortexSession session)
    {
        if (session.Store.Get<object>("uiApplication") is UIApplication uiApplication)
            return uiApplication.Application;
        return (session.Store.Get<object>("activeDocument") as Document)?.Application;
    }

    internal static UIApplication? ResolveUiApplication(CortexSession session)
        => session.Store.Get<object>("uiApplication") as UIApplication;

    internal static bool IsWritableDirectory(string directory)
        => DocumentLifecyclePreview.IsWritableDirectory(directory);

    internal static List<object> DescribeOpenDocuments(Application application)
    {
        var documents = new List<object>();
        try
        {
            foreach (Document document in application.Documents)
            {
                if (document.IsFamilyDocument) continue;
                documents.Add(new
                {
                    title = document.Title,
                    path = document.PathName,
                    isModified = document.IsModified
                });
            }
        }
        catch
        {
            // Enumerating open documents is best-effort context, never the answer.
        }

        return documents;
    }
}

/// <summary>Creates a new project document from a Revit template (.rte).</summary>
[ToolSafety(false, false)]
public sealed class CreateDocumentTool : ICortexTool
{
    public string Name => "create_document";
    public string Category => "Documents";
    // A new project must be creatable even with nothing open.
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Creates a NEW empty project from a Revit template (.rte) and saves it to targetPath. This is the " +
        "real 'new project': unlike save_as_document it does not duplicate the open model. Omit templatePath " +
        "to use the Revit default project template. The document is created in memory, saved, then closed; " +
        "set activate=true to open it in Revit afterwards.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        // Captured up front: creating the document ends with Close(false), which
        // fires DocumentClosing and clears the session store. Resolving the
        // UIApplication after that point found nothing and activation failed on a
        // file that had just been created successfully.
        var uiApplicationAtEntry = DocumentLifecycleSupport.ResolveUiApplication(session);

        var targetPath = input["targetPath"]?.Value<string>()
                         ?? input["filePath"]?.Value<string>()
                         ?? input["path"]?.Value<string>();
        var templatePath = input["templatePath"]?.Value<string>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;
        var activate = input["activate"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrWhiteSpace(targetPath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "targetPath is required and was not provided",
                suggestion: "Pass targetPath as an absolute .rvt path, e.g. " +
                            "{\"targetPath\": \"C:\\\\Projets\\\\T2.rvt\", \"dryRun\": false}.");

        if (!Path.IsPathFullyQualified(targetPath) ||
            !targetPath!.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"targetPath must be an absolute path ending in .rvt (received: {targetPath})");

        var defaultTemplate = SafeDefaultTemplate(application);
        var resolvedTemplate = string.IsNullOrWhiteSpace(templatePath) ? defaultTemplate : templatePath;

        if (string.IsNullOrWhiteSpace(resolvedTemplate))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No template available: templatePath was not provided and Revit has no default project template configured.",
                suggestion: "Pass templatePath explicitly, e.g. " +
                            "C:\\ProgramData\\Autodesk\\RVT <year>\\Templates\\French\\Modele-architecture.rte " +
                            "(<year> is 2026 or 2027, matching the running Revit).");

        var targetDirectory = Path.GetDirectoryName(targetPath) ?? "";
        var targetExists = File.Exists(targetPath);

        var blockers = new List<string>();
        if (!File.Exists(resolvedTemplate))
            blockers.Add($"Template not found: {resolvedTemplate}");
        if (targetExists && !overwrite)
            blockers.Add($"Target already exists and overwrite=false: {targetPath}");
        if (!Directory.Exists(targetDirectory))
            blockers.Add($"Target directory does not exist: {targetDirectory}");
        else if (!DocumentLifecycleSupport.IsWritableDirectory(targetDirectory))
            blockers.Add($"Target directory is not writable: {targetDirectory}");

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = blockers.Count == 0
                    ? $"DryRun: would create '{Path.GetFileName(targetPath)}' from template " +
                      $"'{Path.GetFileName(resolvedTemplate)}'."
                    : $"DryRun: creation would fail ({blockers.Count} blocker(s)).",
                templatePath = resolvedTemplate,
                templateIsDefault = string.IsNullOrWhiteSpace(templatePath),
                defaultProjectTemplate = defaultTemplate,
                targetPath,
                targetExists,
                overwrite,
                activate,
                blockers,
                openDocuments = DocumentLifecycleSupport.DescribeOpenDocuments(application)
            });
        }

        if (blockers.Count > 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Cannot create the document: {string.Join("; ", blockers)}");

        Document? created = null;
        try
        {
            created = application.NewProjectDocument(resolvedTemplate);
            if (created == null)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    $"Revit returned no document for template '{resolvedTemplate}'");

            created.SaveAs(targetPath, new SaveAsOptions { OverwriteExistingFile = overwrite });

            var levelCount = new FilteredElementCollector(created).OfClass(typeof(Level)).GetElementCount();
            var title = created.Title;

            // Close the in-memory copy before activating: the same path cannot be
            // opened twice, and leaving it open would pin the file.
            created.Close(false);
            created = null;

            var activated = false;
            string? activationError = null;
            IReadOnlyList<object> dismissedDialogs = Array.Empty<object>();
            if (activate)
            {
                var uiApplication = uiApplicationAtEntry
                                    ?? DocumentLifecycleSupport.ResolveUiApplication(session);
                if (uiApplication == null)
                {
                    activationError = "No UIApplication in session; the file was created but not opened.";
                }
                else
                {
                    try
                    {
                        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
                        uiApplication.OpenAndActivateDocument(targetPath);
                        activated = true;
                        dismissedDialogs = dialogs.Answered;
                    }
                    catch (Exception exception)
                    {
                        activationError = exception.Message;
                    }
                }
            }

            return CortexResult<object>.Ok(new
            {
                message = activated
                    ? $"Created '{title}' from '{Path.GetFileName(resolvedTemplate)}' and activated it in Revit."
                    : $"Created '{title}' from '{Path.GetFileName(resolvedTemplate)}'. " +
                      (activate
                          ? "Activation failed; open it manually in Revit."
                          : "It is NOT open in Revit — open it manually, or call open_document."),
                path = targetPath,
                title,
                templatePath = resolvedTemplate,
                levelCount,
                fileSizeBytes = new FileInfo(targetPath).Length,
                activated,
                activationError,
                dismissedDialogs
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not create the document: {exception.Message}",
                suggestion: "Check that the template path is a valid .rte for the running Revit version and that " +
                            "no other process holds the template or the target file.");
        }
        finally
        {
            // A half-created in-memory document must never be left open: it would
            // hold a file lock for the rest of the Revit session.
            try { created?.Close(false); } catch { }
        }
    }

    private static string? SafeDefaultTemplate(Application application)
    {
        try
        {
            var template = application.DefaultProjectTemplate;
            return string.IsNullOrWhiteSpace(template) ? null : template;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Opens and activates an existing project file in Revit.</summary>
[ToolSafety(false, false)]
public sealed class OpenDocumentTool : ICortexTool
{
    public string Name => "open_document";
    public string Category => "Documents";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public string Description =>
        "Opens a .rvt file and makes it the active document in Revit. Every subsequent tool call then targets " +
        "that document, and all caches are flushed. Set detachFromCentral=true for a workshared model. " +
        "Supported from this connector's ExternalEvent context — it is the API *event* handlers (Idling, " +
        "DocumentChanged) that cannot switch documents.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        var filePath = input["filePath"]?.Value<string>()
                       ?? input["path"]?.Value<string>()
                       ?? input["targetPath"]?.Value<string>();
        var detach = input["detachFromCentral"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filePath is required and was not provided",
                suggestion: "Pass filePath as an absolute .rvt path.");

        if (!Path.IsPathFullyQualified(filePath) ||
            !filePath!.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"filePath must be an absolute path ending in .rvt (received: {filePath})");

        if (!File.Exists(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"File not found: {filePath}");

        var currentDocument = session.Store.Get<object>("activeDocument") as Document;
        var alreadyActive = currentDocument != null &&
                            string.Equals(currentDocument.PathName, filePath, StringComparison.OrdinalIgnoreCase);

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = alreadyActive
                    ? "DryRun: this file is already the active document."
                    : $"DryRun: would open and activate '{Path.GetFileName(filePath)}'.",
                filePath,
                fileSizeBytes = new FileInfo(filePath).Length,
                alreadyActive,
                currentDocument = currentDocument?.PathName,
                currentDocumentHasUnsavedChanges = currentDocument?.IsModified ?? false,
                detachFromCentral = detach,
                openDocuments = application == null
                    ? new List<object>()
                    : DocumentLifecycleSupport.DescribeOpenDocuments(application),
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

        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
        try
        {
            if (detach)
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                var options = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
                };
                uiApplication.OpenAndActivateDocument(modelPath, options, false);
            }
            else
            {
                uiApplication.OpenAndActivateDocument(filePath);
            }

            var opened = uiApplication.ActiveUIDocument?.Document;
            return CortexResult<object>.Ok(new
            {
                message = $"Opened and activated '{Path.GetFileName(filePath)}'. All caches were flushed." +
                          (dialogs.Answered.Count > 0
                              ? $" {dialogs.Answered.Count} Revit dialog(s) were answered automatically."
                              : ""),
                path = opened?.PathName ?? filePath,
                title = opened?.Title,
                detachedFromCentral = detach,
                isWorkshared = opened?.IsWorkshared ?? false,
                cachesInvalidated = true,
                dismissedDialogs = dialogs.Answered,
                warnings = dialogs.Warnings
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not open and activate the document: {exception.Message}",
                suggestion: "Revit refuses to switch documents while another operation is in progress, and a " +
                            "workshared central model needs detachFromCentral=true. Close any open dialog in " +
                            "Revit and retry.");
        }
    }
}

using System;
using System.Reflection;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RiveTT.Core.Discovery;
using RiveTT.Core.Hosting;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Plugin.Caching;
using RiveTT.Plugin.Communication;
using RiveTT.Plugin.Discovery;
using RiveTT.Plugin.Threading;
using RiveTT.Plugin.UI;

namespace RiveTT.Plugin;

/// <summary>
/// RiveTT is always available while Revit runs. It has no ribbon START switch
/// (the pipe opens with Revit), no TCP listener, and no confirmation dialog per
/// call. What the ribbon does carry is the write lock: the session starts in
/// read-only mode and only a human, from that panel, hands write access over.
/// </summary>
public sealed class RiveTTApp : IExternalApplication
{
    private RevitNamedPipeService? _pipeService;
    private CortexRouter? _router;
    private CortexSession? _session;
    private DocumentChangeWatcher? _cacheWatcher;
    private UIApplication? _uiApplication;

    public static RiveTTApp? Instance { get; private set; }
    public bool IsServiceRunning => _pipeService?.IsRunning == true;
    public UIApplication? UiApplication => _uiApplication;
    public CortexRouter? Router => _router;
    public CortexSession? Session => _session;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;
        try
        {
            _session = new CortexSession(new SessionStore());

            // Read-only until a human says otherwise. The connector loads with
            // Revit and asks nothing, so the safe default is the one that cannot
            // touch a model on its own: the ribbon toggle is the only way out of
            // it, and no tool can reach it.
            _session.WriteAccess.Set(writesAllowed: false, origin: "startup");
            var auditLogger = new AuditLogger(CortexEnvironment.Current.AuditLogPath);
            _router = new CortexRouter(_session, new DocumentAnalyzer(), auditLogger: auditLogger);

            var toolsAssembly = LoadToolsAssembly();
            if (toolsAssembly == null)
            {
                System.Diagnostics.Trace.WriteLine("[RiveTT] Tools assembly could not be loaded.");
                return Result.Failed;
            }
            _router.RegisterToolsFromAssembly(toolsAssembly);

            var executionHandler = new ToolExecutionHandler(auditLogger);
            _router.SetDispatcher(new RevitThreadDispatcher(
                executionHandler, ExternalEvent.Create(executionHandler)));

            // No license gate, read-only profile, confirmation callback, TCP port,
            // or manual start action. The local pipe becomes ready immediately.
            _pipeService = new RevitNamedPipeService(_router);
            _pipeService.Start();

            // The panel is a display and a switch. If it cannot be built, the
            // connector must still serve: the pipe is the product, the ribbon is
            // the courtesy.
            try
            {
                CortexRibbon.Build(application);
            }
            catch (Exception ribbonException)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RiveTT] Ribbon panel not created: {ribbonException.Message}");
            }

            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;
            _cacheWatcher = new DocumentChangeWatcher(_session);
            _cacheWatcher.Attach(application.ControlledApplication);
            application.Idling += OnIdling;

            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Ready through local pipe '{_pipeService.PipeName}'. " +
                $"{_router.TotalToolCount} tools registered. " +
                $"Writes {(_session.WriteAccess.WritesAllowed ? "allowed" : "locked")}.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RiveTT] Startup failed: {ex}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _pipeService?.Dispose();
            _pipeService = null;
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.Idling -= OnIdling;
            if (_uiApplication != null)
                _uiApplication.ViewActivated -= OnViewActivated;
            _cacheWatcher?.Dispose();
            _cacheWatcher = null;
            CleanupTempScripts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RiveTT] Shutdown error: {ex.Message}");
        }
        return Result.Succeeded;
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs args) => BindDocument(args.Document);

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        // The pipe remains available. Calls made with no project open receive a
        // structured error until the next document is activated.
        _session?.Reinitialize(new DocumentCapabilities(), "en");

        // Reinitialize clears the whole session store, UIApplication included — but
        // that object is process-scoped, not document state. Losing it left the
        // connector unable to reach Revit at all: create_document closes the
        // in-memory document it just saved, which fires this event, and every later
        // create_document/open_document then failed with "No Revit application
        // context is available yet" with no way to recover but opening a file by
        // hand.
        if (_uiApplication != null)
        {
            _session?.Store.Set("uiApplication", _uiApplication);

            // Another project may still be open: rebind to it rather than leaving
            // the session document-less.
            var remaining = _uiApplication.ActiveUIDocument?.Document;
            if (remaining != null && !ReferenceEquals(remaining, args.Document))
                BindDocument(remaining);
        }
    }

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_uiApplication != null) return;
        _uiApplication = sender as UIApplication;
        if (_uiApplication == null) return;

        _uiApplication.ViewActivated += OnViewActivated;
        _session?.Store.Set("uiApplication", _uiApplication);
        BindDocument(_uiApplication.ActiveUIDocument?.Document);
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        var document = e.CurrentActiveView?.Document;
        if (document != null && _session?.Store.Get<object>("activeDocument") != document)
            BindDocument(document);
    }

    private void BindDocument(Autodesk.Revit.DB.Document? document)
    {
        if (document == null || _router == null) return;
        _router.OnDocumentChanged(document, LocaleDetector.Detect(document));
        if (_uiApplication != null)
            _session?.Store.Set("uiApplication", _uiApplication);
    }

    private static void CleanupTempScripts()
    {
        var scriptsFolder = CortexEnvironment.Current.ScriptsFolder;
        if (!System.IO.Directory.Exists(scriptsFolder)) return;
        foreach (var file in System.IO.Directory.GetFiles(scriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new System.IO.StreamReader(file);
                if ((reader.ReadLine() ?? "").TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    System.IO.File.Delete(file);
            }
            catch { }
        }
    }

    private static Assembly? LoadToolsAssembly()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            return Assembly.LoadFrom(System.IO.Path.Combine(directory, "RiveTT.Tools.dll"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RiveTT] Could not load tools: {ex.Message}");
            return null;
        }
    }
}

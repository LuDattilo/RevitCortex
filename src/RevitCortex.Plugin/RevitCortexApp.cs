using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.Communication;
using RevitCortex.Plugin.Discovery;
using RevitCortex.Plugin.Threading;
using RevitCortex.Plugin.UI;
using System;
using System.Reflection;

namespace RevitCortex.Plugin;

public class RevitCortexApp : IExternalApplication
{
    private SocketService? _socketService;
    private CortexRouter? _router;
    private CortexSession? _session;
    private UIApplication? _uiApplication;

    public static RevitCortexApp? Instance { get; private set; }
    public bool IsServiceRunning => _socketService?.IsRunning ?? false;
    public UIApplication? UiApplication => _uiApplication;
    public CortexRouter? Router => _router;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;

        try
        {
            // Register dockable pane (must happen before Revit main window is shown)
            try
            {
                application.RegisterDockablePane(
                    CortexDockablePaneProvider.PaneId,
                    "RevitCortex",
                    new CortexDockablePaneProvider());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Panel registration skipped: {ex.Message}");
            }

            // Create ribbon panel
            CreateRibbonPanel(application);

            // Initialize session, router, and tools
            var store = new SessionStore();
            _session = new CortexSession(store);
            _session.ConfirmAction = ConfirmationHelper.Confirm;
            var analyzer = new DocumentAnalyzer();

            _router = new CortexRouter(_session, analyzer);

            var toolsAssembly = LoadToolsAssembly();
            if (toolsAssembly != null)
            {
                _router.RegisterToolsFromAssembly(toolsAssembly);
            }

            // Create thread dispatcher for Revit main thread execution
            var executionHandler = new ToolExecutionHandler();
            var externalEvent = ExternalEvent.Create(executionHandler);
            var dispatcher = new RevitThreadDispatcher(executionHandler, externalEvent);
            _router.SetDispatcher(dispatcher);

            // Load disabled tools from settings
            LoadDisabledTools();

            // Create socket service but do NOT start automatically
            _socketService = new SocketService(_router);

            // Listen for document events
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;

            // Capture UIApplication when Revit is idle (needed for chat panel chips)
            application.Idling += OnIdling;

            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Started. {_router.TotalToolCount} tools registered.");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Startup failed: {ex}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _socketService?.Stop();
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.Idling -= OnIdling;
            if (_uiApplication != null)
                _uiApplication.ViewActivated -= OnViewActivated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Shutdown error: {ex.Message}");
        }
        return Result.Succeeded;
    }

    public void StartService(Document? activeDocument = null)
    {
        if (_socketService != null && !_socketService.IsRunning)
        {
            // Initialize session with active document if available
            if (activeDocument != null && _router != null)
            {
                var locale = LocaleDetector.Detect(activeDocument);
                _router.OnDocumentChanged(activeDocument, locale);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized with document: {activeDocument.Title}, locale: {locale}");
            }

            _socketService.Start();
        }
    }

    public void StopService()
    {
        _socketService?.Stop();
    }

    private void CreateRibbonPanel(UIControlledApplication application)
    {
        RibbonPanel panel = application.CreateRibbonPanel("RevitCortex");
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;

        // Connection toggle button
        var connectBtn = new PushButtonData(
            "ID_CORTEX_TOGGLE", "Cortex\r\nSwitch",
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleConnection");
        connectBtn.ToolTip = "Start / Stop RevitCortex server";
        connectBtn.Image = IconFactory.CreateConnectionIcon(16);
        connectBtn.LargeImage = IconFactory.CreateConnectionIcon(32);
        panel.AddItem(connectBtn);

        // Panel toggle button
        var panelBtn = new PushButtonData(
            "ID_CORTEX_PANEL", "Chat\r\nPanel",
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleCortexPanel");
        panelBtn.ToolTip = "Show / Hide RevitCortex chat panel";
        panelBtn.Image = IconFactory.CreatePanelIcon(16);
        panelBtn.LargeImage = IconFactory.CreatePanelIcon(32);
        panel.AddItem(panelBtn);

        // Settings button
        var settingsBtn = new PushButtonData(
            "ID_CORTEX_SETTINGS", "Settings",
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenSettings");
        settingsBtn.ToolTip = "RevitCortex settings";
        settingsBtn.Image = IconFactory.CreateSettingsIcon(16);
        settingsBtn.LargeImage = IconFactory.CreateSettingsIcon(32);
        panel.AddItem(settingsBtn);
    }

    private void OnDocumentOpened(object sender, DocumentOpenedEventArgs args)
    {
        var doc = args.Document;
        if (doc == null) return;

        var locale = LocaleDetector.Detect(doc);
        _router!.OnDocumentChanged(doc, locale);

        System.Diagnostics.Trace.WriteLine(
            $"[RevitCortex] Document opened. Locale: {locale}, " +
            $"Capabilities: {_router!.GetAvailableToolNames().Count} tools available");
    }

    private void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (_uiApplication != null) return;
        _uiApplication = sender as UIApplication;

        // Hook ViewActivated to detect document switches
        if (_uiApplication != null)
        {
            _uiApplication.ViewActivated += OnViewActivated;

            // If a document is already open, initialize the session now
            var doc = _uiApplication.ActiveUIDocument?.Document;
            if (doc != null && _router != null &&
                _session?.Store.Get<object>("activeDocument") == null)
            {
                var locale = LocaleDetector.Detect(doc);
                _router.OnDocumentChanged(doc, locale);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized from Idling: {doc.Title}, locale: {locale}");
            }
        }
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        var doc = e.CurrentActiveView?.Document;
        if (doc == null || _router == null) return;

        // Only update if document changed
        var currentDoc = _session?.Store.Get<object>("activeDocument");
        if (currentDoc != doc)
        {
            var locale = LocaleDetector.Detect(doc);
            _router.OnDocumentChanged(doc, locale);
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Document switched: {doc.Title}, locale: {locale}");
        }
    }

    private void LoadDisabledTools()
    {
        try
        {
            string settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".revitcortex", "settings.json");
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var disabled = settings?["DisabledTools"]?
                    .ToObject<string[]>() ?? Array.Empty<string>();
                _router!.SetDisabledTools(disabled);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load disabled tools: {ex.Message}");
        }
    }

    private Assembly? LoadToolsAssembly()
    {
        try
        {
            var pluginDir = System.IO.Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            var toolsPath = System.IO.Path.Combine(pluginDir, "RevitCortex.Tools.dll");
            return Assembly.LoadFrom(toolsPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load Tools assembly: {ex.Message}");
            return null;
        }
    }
}

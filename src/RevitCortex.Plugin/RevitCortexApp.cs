using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.Caching;
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
    private DocumentChangeWatcher? _cacheWatcher;
    private UIApplication? _uiApplication;
    private int _port = 8080;
    private Autodesk.Revit.UI.PushButton? _connectButton;
    private bool _updateNotificationShown;

    public static RevitCortexApp? Instance { get; private set; }

    /// <summary>
    /// Fired on the calling thread whenever the server starts, stops, or crashes.
    /// Subscribers must marshal to the UI thread themselves if needed.
    /// </summary>
    public event Action? ServiceStateChanged;

    /// <summary>
    /// Returns true only if the socket service flag is set AND a live TCP
    /// connection to localhost:port succeeds. This catches cases where the
    /// listener thread died unexpectedly while the flag remained true.
    /// </summary>
    public bool IsServiceRunning
    {
        get
        {
            if (_socketService?.IsRunning != true) return false;
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                probe.Connect("127.0.0.1", _port);
                return true;
            }
            catch
            {
                // Listener is gone — sync internal flag so next call is fast
                _socketService.Stop();
                return false;
            }
        }
    }

    public int Port => _port;
    public UIApplication? UiApplication => _uiApplication;
    public CortexRouter? Router => _router;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;

        try
        {
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

            // Load disabled tools, read-only mode, and port from settings
            LoadDisabledTools();
            LoadReadOnlyMode();
            LoadPort();

            // Fire-and-forget update check against the public metadata repo.
            // When an update is found, the UpdateAvailable event wakes up the
            // notification window (or OnIdling shows it if Revit is already idle).
            RevitCortex.Plugin.Updates.UpdateChecker.UpdateAvailable += OnUpdateAvailable;
            RevitCortex.Plugin.Updates.UpdateChecker.CheckInBackground();

            // Create socket service but do NOT start automatically
            _socketService = new SocketService(_router, _port);

            // Listen for document events
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;

            // Subscribe the tool-result cache to model-change events. The watcher
            // bumps DocumentVersion and drops Document/Transaction entries so
            // cached reads can never outlive the model state they describe.
            _cacheWatcher = new DocumentChangeWatcher(_session);
            _cacheWatcher.Attach(application.ControlledApplication);

            // Capture UIApplication when Revit is idle (needed for ViewActivated hook)
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
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.Idling -= OnIdling;
            if (_uiApplication != null)
                _uiApplication.ViewActivated -= OnViewActivated;

            _cacheWatcher?.Dispose();
            _cacheWatcher = null;

            // Delete all TEMP scripts generated during this session
            CleanupTempScripts();
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
                // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
                if (_uiApplication != null)
                    _session?.Store.Set("uiApplication", _uiApplication);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized with document: {activeDocument.Title}, locale: {locale}");
            }

            _socketService.Start();
            UpdateConnectionButtonIcon();
            ServiceStateChanged?.Invoke();
        }
    }

    public void StopService()
    {
        _socketService?.Stop();
        UpdateConnectionButtonIcon();
        ServiceStateChanged?.Invoke();
    }

    private void UpdateConnectionButtonIcon()
    {
        if (_connectButton == null) return;
        bool active = IsServiceRunning;
        _connectButton.Image = IconFactory.CreateConnectionIcon(16, active);
        _connectButton.LargeImage = IconFactory.CreateConnectionIcon(32, active);
        _connectButton.ToolTip = active
            ? $"RevitCortex running on port {_port} — click to stop"
            : "Start RevitCortex server";
    }

    private void CreateRibbonPanel(UIControlledApplication application)
    {
        RibbonPanel panel = application.CreateRibbonPanel("RevitCortex");
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;

        // Connection toggle button
        var connectBtnData = new PushButtonData(
            "ID_CORTEX_TOGGLE", "Cortex\r\nSwitch",
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleConnection");
        connectBtnData.ToolTip = "Start RevitCortex server";
        connectBtnData.Image = IconFactory.CreateConnectionIcon(16, false);
        connectBtnData.LargeImage = IconFactory.CreateConnectionIcon(32, false);
        _connectButton = panel.AddItem(connectBtnData) as Autodesk.Revit.UI.PushButton;

        // Settings button
        var settingsBtn = new PushButtonData(
            "ID_CORTEX_SETTINGS", "Settings",
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenSettings");
        settingsBtn.ToolTip = "RevitCortex settings";
        settingsBtn.Image = IconFactory.CreateSettingsIcon(16);
        settingsBtn.LargeImage = IconFactory.CreateSettingsIcon(32);
        panel.AddItem(settingsBtn);

        // Send support report button
        var supportBtn = new PushButtonData(
            "ID_CORTEX_SUPPORT", "Send log\r\nto support",
            assemblyLocation, "RevitCortex.Plugin.Commands.SendSupportReport");
        supportBtn.ToolTip = "Send a bug report to RevitCortex support";
        supportBtn.LongDescription =
            "Collects recent audit logs, token-usage log, settings, and the most recent " +
            "Revit journal into a ZIP on the desktop, then opens a pre-filled Outlook " +
            "message addressed to support. Add a short description of the problem " +
            "and click Send. No personal data is sent beyond what's in the logs.";
        supportBtn.Image = IconFactory.CreateSupportIcon(16);
        supportBtn.LargeImage = IconFactory.CreateSupportIcon(32);
        panel.AddItem(supportBtn);
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs args)
    {
        var doc = args.Document;
        if (doc == null) return;

        var locale = LocaleDetector.Detect(doc);
        _router!.OnDocumentChanged(doc, locale);

        // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
        if (_uiApplication != null)
            _session?.Store.Set("uiApplication", _uiApplication);

        System.Diagnostics.Trace.WriteLine(
            $"[RevitCortex] Document opened. Locale: {locale}, " +
            $"Capabilities: {_router!.GetAvailableToolNames().Count} tools available");
    }

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        try
        {
            // Stop the TCP server to prevent stale commands reaching a different document
            if (_socketService != null && _socketService.IsRunning)
            {
                _socketService.Stop();
                UpdateConnectionButtonIcon();
                ServiceStateChanged?.Invoke();
                System.Diagnostics.Trace.WriteLine(
                    "[RevitCortex] Server stopped: document closing");
            }

            // Clear session state (store, capabilities, locale)
            _session?.Reinitialize(new Core.Discovery.DocumentCapabilities(), "en");

            System.Diagnostics.Trace.WriteLine(
                "[RevitCortex] Session reset: document closing");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Error on document closing: {ex.Message}");
        }
    }

    private void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (_uiApplication != null) return;
        _uiApplication = sender as UIApplication;

        // Hook ViewActivated to detect document switches
        if (_uiApplication != null)
        {
            _uiApplication.ViewActivated += OnViewActivated;

            // Store UIApplication in session for send_code_to_revit
            _session?.Store.Set("uiApplication", _uiApplication);

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

            // If the update check already completed before Idling fired, show now.
            if (RevitCortex.Plugin.Updates.UpdateChecker.Latest?.HasUpdate == true)
                ShowUpdateNotification();
        }
    }

    /// <summary>
    /// Called on a background thread when the update check finds a newer version.
    /// Marshals to the UI thread to show the notification window.
    /// </summary>
    private void OnUpdateAvailable()
    {
        // If Revit isn't idle yet, OnIdling will handle it.
        if (_uiApplication == null) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            (System.Action)ShowUpdateNotification);
    }

    private void ShowUpdateNotification()
    {
        if (_updateNotificationShown) return;
        _updateNotificationShown = true;

        try
        {
            var win = new UI.UpdateNotificationWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not show update notification: {ex.Message}");
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
            // Re-store UIApplication after session reinitialize (needed by send_code_to_revit)
            if (_uiApplication != null)
                _session?.Store.Set("uiApplication", _uiApplication);
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Document switched: {doc.Title}, locale: {locale}");
        }
    }

    private void LoadPort()
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
                var port = settings?["Port"]?.ToObject<int>();
                if (port.HasValue && port.Value > 0 && port.Value <= 65535)
                {
                    _port = port.Value;
                    System.Diagnostics.Trace.WriteLine(
                        $"[RevitCortex] Port configured: {_port}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load port setting: {ex.Message}");
        }
    }

    private void LoadReadOnlyMode()
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
                var readOnly = settings?["ReadOnlyMode"]?.ToObject<bool>() ?? false;
                _router!.ReadOnlyMode = readOnly;
                if (readOnly)
                    System.Diagnostics.Trace.WriteLine("[RevitCortex] Read-only mode is ON");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load read-only setting: {ex.Message}");
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

    private static void CleanupTempScripts()
    {
        var scriptsFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcortex", "scripts");
        if (!System.IO.Directory.Exists(scriptsFolder)) return;
        foreach (var file in System.IO.Directory.GetFiles(scriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new System.IO.StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    System.IO.File.Delete(file);
            }
            catch { }
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

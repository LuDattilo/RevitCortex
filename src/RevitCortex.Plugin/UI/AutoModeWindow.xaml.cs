using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Non-modal floating window shown while Auto mode is active. Replaces the
/// former ribbon "Stop Auto" button. Appears top-center above Revit and offers
/// a single "Stop Auto" control. Closing it (X) is equivalent to clicking
/// Stop Auto. Deactivation is surfaced via <see cref="StopRequested"/>, which
/// the host (RevitCortexApp) wires to turn Auto mode off.
/// </summary>
public partial class AutoModeWindow : Window
{
    /// <summary>
    /// Raised once when Auto mode should stop: the user clicked Stop or closed
    /// the window. NOT raised when the host closes the window programmatically
    /// via <see cref="CloseFromHost"/>.
    /// </summary>
    public event Action? StopRequested;

    // Guards against double-firing: the Stop button click closes the window,
    // which would otherwise also fire StopRequested via the Closing handler.
    private bool _stopHandled;

    // Set when the host closes the window because Auto mode was turned off
    // elsewhere (Stop command, document close). Suppresses StopRequested so we
    // don't loop back into deactivation.
    private bool _closingFromHost;
    private readonly IntPtr _ownerHandle;
    private bool _ownerAttached;

    public AutoModeWindow()
        : this(Process.GetCurrentProcess().MainWindowHandle)
    {
    }

    public AutoModeWindow(IntPtr ownerHandle)
    {
        _ownerHandle = ownerHandle;
        InitializeComponent();
        AttachOwner();
    }

    /// <summary>
    /// Called (on the UI thread) each time an operation is auto-approved while
    /// Auto mode is on. Auto mode intentionally has no inactivity timeout: it
    /// remains active until the user stops it or the host closes it.
    /// </summary>
    public void RegisterActivity()
    {
        // Reserved for future status updates; do not stop Auto mode here.
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Parent to Revit so the window stays above Revit, but not above other
        // foreground applications. This is intentionally not a global Topmost window.
        AttachOwner();

        // Top-center of the primary work area with a small top margin.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 24;
    }

    private void AttachOwner()
    {
        if (_ownerAttached || _ownerHandle == IntPtr.Zero) return;

        try
        {
            new WindowInteropHelper(this).Owner = _ownerHandle;
            _ownerAttached = true;
        }
        catch { /* non-critical */ }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        RequestStop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // X / Alt-F4 path: treat as Stop, unless the host is closing us.
        if (!_closingFromHost)
            RequestStop();
        base.OnClosing(e);
    }

    private void RequestStop()
    {
        if (_stopHandled) return;
        _stopHandled = true;
        StopRequested?.Invoke();
    }

    /// <summary>
    /// Closes the window without raising <see cref="StopRequested"/>. Called by
    /// the host when Auto mode is turned off from somewhere other than this
    /// window (e.g. a future Stop command, or document close/reinitialize).
    /// </summary>
    public void CloseFromHost()
    {
        _closingFromHost = true;
        Close();
    }
}

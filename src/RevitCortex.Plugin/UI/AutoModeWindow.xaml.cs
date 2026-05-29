using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Non-modal floating window shown while Auto mode is active. Replaces the
/// former ribbon "Stop Auto" button. Appears top-center above Revit and offers
/// a single "Stop Auto" control. Closing it (X) is equivalent to clicking
/// Stop Auto. Deactivation is surfaced via <see cref="StopRequested"/>, which
/// the host (RevitCortexApp) wires to turn Auto mode off.
///
/// The window also closes itself after a burst of operations ends: an
/// inactivity timer (reset on each <see cref="RegisterActivity"/>) elapses when
/// no auto-approved operation has arrived for <see cref="InactivitySeconds"/>,
/// at which point it behaves exactly like the user clicking Stop Auto.
/// </summary>
public partial class AutoModeWindow : Window
{
    /// <summary>Seconds of no operations before Auto mode auto-stops.</summary>
    public const int InactivitySeconds = 8;

    /// <summary>
    /// Raised once when Auto mode should stop — the user clicked Stop, closed the
    /// window, or the inactivity timer elapsed. NOT raised when the host closes
    /// the window programmatically via <see cref="CloseFromHost"/>.
    /// </summary>
    public event Action? StopRequested;

    // Guards against double-firing: the Stop button click closes the window,
    // which would otherwise also fire StopRequested via the Closing handler.
    private bool _stopHandled;

    // Set when the host closes the window because Auto mode was turned off
    // elsewhere (Stop command, document close). Suppresses StopRequested so we
    // don't loop back into deactivation.
    private bool _closingFromHost;

    private readonly DispatcherTimer _inactivityTimer;

    public AutoModeWindow()
    {
        InitializeComponent();
        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(InactivitySeconds)
        };
        _inactivityTimer.Tick += OnInactivityElapsed;
    }

    /// <summary>
    /// Called (on the UI thread) each time an operation is auto-approved while
    /// Auto mode is on. Restarts the inactivity countdown so the window stays
    /// open during a burst and closes shortly after the last operation.
    /// </summary>
    public void RegisterActivity()
    {
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void OnInactivityElapsed(object? sender, EventArgs e)
    {
        // No operations for InactivitySeconds — treat the burst as finished.
        _inactivityTimer.Stop();
        RequestStop();
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Parent to the Revit main window so it tracks Revit's lifetime.
        try
        {
            var handle = Process.GetCurrentProcess().MainWindowHandle;
            if (handle != IntPtr.Zero)
                new WindowInteropHelper(this).Owner = handle;
        }
        catch { /* non-critical */ }

        // Top-center of the primary work area with a small top margin.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 24;

        // Start the countdown immediately: if no operation follows the initial
        // "Auto" click, the window still auto-dismisses.
        RegisterActivity();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        RequestStop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _inactivityTimer.Stop();
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

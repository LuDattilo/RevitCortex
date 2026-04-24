using RevitCortex.Plugin.Updates;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Non-modal floating notification shown at Revit startup when a newer
/// RevitCortex version is available. Owns the full download → install flow
/// so the user never has to open Settings.
///
/// States: Idle → Downloading → ConfirmInstall → Installing/Error
/// </summary>
public partial class UpdateNotificationWindow : Window
{
    private enum NotifState { Idle, Downloading, ConfirmInstall, Installing, Error }

    private NotifState _state = NotifState.Idle;
    private DispatcherTimer? _pollTimer;

    public UpdateNotificationWindow()
    {
        InitializeComponent();
        Refresh();
    }

    // ── Startup ──────────────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Parent this window to the Revit main window so it stays in front
        // of Revit but not above other applications.
        try
        {
            var handle = Process.GetCurrentProcess().MainWindowHandle;
            if (handle != IntPtr.Zero)
                new WindowInteropHelper(this).Owner = handle;
        }
        catch { /* non-critical */ }

        // Position bottom-right of the primary screen with a small margin.
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top  = area.Bottom - ActualHeight - 20;
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var info = UpdateChecker.Latest;
        if (info == null) { Close(); return; }

        VersionBadge.Text = $"v{info.RemoteVersion}";

        switch (_state)
        {
            case NotifState.Idle:
                NotifTitle.Text  = $"RevitCortex {info.RemoteVersion} disponibile";
                NotifDetail.Text = string.IsNullOrWhiteSpace(info.Changelog)
                    ? $"Versione corrente: {UpdateChecker.CurrentVersion}"
                    : info.Changelog;
                ProgressGrid.Visibility  = Visibility.Collapsed;
                ConfirmBorder.Visibility = Visibility.Collapsed;
                SetPrimary("Aggiorna ora",    "#FFB300", "#FF8F00");
                SetSecondary("Più tardi", visible: true);
                break;

            case NotifState.Downloading:
                var (recv, total) = UpdateChecker.DownloadProgress;
                string prog = total > 0
                    ? $"{recv / 1_048_576.0:F0} / {total / 1_048_576.0:F0} MB"
                    : $"{recv / 1_048_576.0:F0} MB scaricati…";
                double pct = total > 0 ? recv * 100.0 / total : 0;

                NotifTitle.Text = $"Download in corso… {prog}";
                NotifDetail.Text = string.Empty;
                ProgressGrid.Visibility  = Visibility.Visible;
                ProgressBar.Value        = pct;
                ProgressText.Text        = prog;
                ConfirmBorder.Visibility = Visibility.Collapsed;
                SetPrimary("Annulla", "#9E9E9E", "#757575");
                SetSecondary(null, visible: false);
                break;

            case NotifState.ConfirmInstall:
                NotifTitle.Text  = "Pronto per l'installazione";
                NotifDetail.Text = "Leggi l'avviso qui sotto prima di continuare.";
                ProgressGrid.Visibility  = Visibility.Collapsed;
                ConfirmBorder.Visibility = Visibility.Visible;
                SetPrimary("Installa ora e chiudi Revit", "#388E3C", "#2E7D32");
                SetSecondary("Annulla", visible: true);
                break;

            case NotifState.Installing:
                NotifTitle.Text  = "Installazione avviata";
                NotifDetail.Text = "Al termine riavvia Revit.";
                ProgressGrid.Visibility  = Visibility.Collapsed;
                ConfirmBorder.Visibility = Visibility.Collapsed;
                SetPrimary("Chiudi Revit ora", "#00796B", "#004D40");
                SetSecondary(null, visible: false);
                break;

            case NotifState.Error:
                NotifTitle.Text  = "Download fallito";
                NotifDetail.Text = UpdateChecker.DownloadError ?? "Errore sconosciuto";
                ProgressGrid.Visibility  = Visibility.Collapsed;
                ConfirmBorder.Visibility = Visibility.Collapsed;
                SetPrimary("Riprova", "#E53935", "#B71C1C");
                SetSecondary("Più tardi", visible: true);
                break;
        }

        // Re-measure so SizeToContent recalculates height.
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Top = area.Bottom - ActualHeight - 20;
    }

    private void SetPrimary(string label, string bg, string border)
    {
        PrimaryButton.Content     = label;
        PrimaryButton.Background  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        PrimaryButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
    }

    private void SetSecondary(string? label, bool visible)
    {
        SecondaryButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (label != null) SecondaryButton.Content = label;
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case NotifState.Idle:
            case NotifState.Error:
                UpdateChecker.ResetDownload();
                UpdateChecker.StartDownloadAsync();
                _state = NotifState.Downloading;
                StartPolling();
                Refresh();
                break;

            case NotifState.Downloading:
                UpdateChecker.CancelDownload();
                _state = NotifState.Idle;
                StopPolling();
                Refresh();
                break;

            case NotifState.ConfirmInstall:
                StopPolling();
                UpdateChecker.LaunchInstaller();
                _state = NotifState.Installing;
                Refresh();
                // Give the installer a moment to launch, then close Revit.
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    try { Application.Current?.Shutdown(); } catch { }
                };
                t.Start();
                break;

            case NotifState.Installing:
                try { Application.Current?.Shutdown(); } catch { }
                break;
        }
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case NotifState.ConfirmInstall:
                // Go back to idle — user changed mind
                _state = NotifState.Idle;
                Refresh();
                break;

            default:
                // "Più tardi" — dismiss; Settings page still shows the banner.
                StopPolling();
                Close();
                break;
        }
    }

    // ── Download polling ─────────────────────────────────────────────────────

    private void StartPolling()
    {
        if (_pollTimer?.IsEnabled == true) return;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        switch (UpdateChecker.State)
        {
            case UpdateChecker.DownloadState.Downloading:
                Refresh();
                break;

            case UpdateChecker.DownloadState.Ready:
                StopPolling();
                _state = NotifState.ConfirmInstall;
                Refresh();
                break;

            case UpdateChecker.DownloadState.Error:
                StopPolling();
                _state = NotifState.Error;
                Refresh();
                break;
        }
    }
}

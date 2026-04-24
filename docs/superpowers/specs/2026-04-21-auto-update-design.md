# Auto-Update Flow — Design Spec
**Date:** 2026-04-21  
**Status:** Approved  
**Scope:** Automate download + install of RevitCortex updates from within Revit

---

## Problem

The existing update banner (shown in Settings → General) only opens the browser at the download URL. The user must manually download the zip (~75 MB), extract it, and run `install.ps1`. This is a 5-step manual process that most users skip.

## Goal

Reduce the update flow to **2 user actions**: click "Download & Install" → approve UAC prompt. Everything else is handled by the plugin.

---

## Approach: In-process download + RunAs launcher

The plugin downloads the zip in the foreground (user-triggered), extracts it to `%TEMP%\revitcortex-update\`, then launches `install.ps1` with `Process.Start(..., Verb = RunAs)`. One UAC prompt, then Revit prompts to restart.

No background downloads. No bandwidth used without user consent.

---

## State Machine

```
IDLE ──[click "Download & Install"]──► DOWNLOADING ──[100%]──► READY ──[click "Install ora"]──► INSTALLING ──► DONE
                                               │
                                        [error/cancel]
                                               ▼
                                             ERROR ──[click "Riprova"]──► DOWNLOADING
                                                   ──[click "Scarica manualmente"]──► opens browser
```

States stored in `UpdateChecker.DownloadState` (static, survives Settings page navigation).

---

## Components

### New: `Updates/UpdateDownloader.cs`
- `DownloadAsync(url, destPath, IProgress<(long,long)>, CancellationToken)` — streams zip with `HttpClient`, reports `(bytesReceived, totalBytes)` progress
- `ExtractAsync(zipPath, destDir)` — extracts to `%TEMP%\revitcortex-update\{version}\`
- Verifies zip is non-empty before extraction
- Returns `UpdateDownloadResult { Success, ErrorMessage, ExtractedPath }`

### Modified: `Updates/UpdateChecker.cs`
Add:
```csharp
public enum DownloadState { Idle, Downloading, Ready, Installing, Done, Error }
public static DownloadState State { get; private set; } = DownloadState.Idle;
public static string? DownloadError { get; private set; }
public static string? ExtractedPath { get; private set; }
public static (long Received, long Total) DownloadProgress { get; private set; }
private static CancellationTokenSource? _cts;

public static void StartDownloadAsync()  // triggers UpdateDownloader, updates State
public static void CancelDownload()      // cancels _cts, resets State to Idle
public static void LaunchInstaller()     // Process.Start(install.ps1, RunAs), State → Installing
```
`CheckInBackground()` unchanged — manifest fetch is independent of download state.

### Modified: `UI/GeneralSettingsPage.xaml`
Replace the single "Download" `<Button>` in `UpdateBanner` with:
- `<Button x:Name="UpdateActionButton">` — label changes per state
- `<ProgressBar x:Name="UpdateProgress">` — visible only in DOWNLOADING state
- `<TextBlock x:Name="UpdateProgressText">` — "38 / 75 MB"

### Modified: `UI/GeneralSettingsPage.xaml.cs`
- `RefreshUpdateBanner()` reads `UpdateChecker.State` and renders the correct UI state
- `UpdateAction_Click()` dispatches to `StartDownload()`, `CancelDownload()`, `LaunchInstaller()`, or `CloseRevit()` based on current state
- `DispatcherTimer` (1s interval, 10 ticks max) already polls for `UpdateChecker.Latest` — leave it unchanged
- Add a **separate** `_downloadTimer` (250ms interval) started by `StartDownload()` and stopped when state leaves DOWNLOADING; updates progress bar and text on the UI thread via `Dispatcher.InvokeAsync`

---

## UI per stato

| State | Banner color | Button label | Extra |
|---|---|---|---|
| IDLE | Amber (#FFF8E1) | "Download & Install" | — |
| DOWNLOADING | Blue (#E3F2FD) | "Annulla" | Progress bar + "38 / 75 MB" |
| READY | Green (#E8F5E9) | "Install ora" | "Approva il prompt UAC" |
| INSTALLING | Green | disabled | — |
| DONE | Teal (#E0F2F1) | "Chiudi Revit" | "Riavvia per completare" |
| ERROR | Red (#FFEBEE) | "Riprova" + "Scarica manualmente" | ErrorMessage |

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Timeout HTTP (>30s) | State → ERROR, messaggio generico |
| Cancellazione utente | State → IDLE, banner torna ad IDLE |
| Zip vuoto / corrotto | State → ERROR, messaggio specifico |
| UAC negato dall'utente | State rimane READY silenziosamente |
| Spazio disco insufficiente | State → ERROR, messaggio con percorso temp |
| `install.ps1` non trovato nell'estratto | State → ERROR |

Tutti gli errori loggati via `Trace.WriteLine("[RevitCortex] ...")` — mai dialogs bloccanti.

---

## Testing

### Unit test — `UpdateDownloaderTests.cs`
- Mock `HttpMessageHandler` che restituisce un zip valido
- Verifica: progress events riportati correttamente, `Success = true`, file estratto esiste
- Verifica: timeout → `Success = false`, `ErrorMessage` non vuoto
- Verifica: cancellazione → `OperationCanceledException` gestita, nessun file residuo

### Manual test
- Impostare temporaneamente `ManifestUrl` a `http://localhost:9999/latest.json` con versione `99.0.0` e `downloadUrl` puntante a una copia locale del zip
- Verificare tutti e 6 gli stati del banner aprendo Settings

---

## Out of scope

- Badge sul ribbon Revit (può essere aggiunto separatamente)
- Changelog espanso nel banner
- Download background silenzioso
- Rollback automatico in caso di installazione fallita

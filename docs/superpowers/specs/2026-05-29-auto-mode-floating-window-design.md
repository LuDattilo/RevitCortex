# Auto Mode Floating Window + Persistence Fix — Design

**Date:** 2026-05-29
**Status:** Approved (pending spec review)
**Affects:** RevitCortex Plugin (UI + router), targets R23→R27

## Problem

Auto mode (shipped in v1.0.27) is broken in two ways, found during live testing on Snowdon Towers:

1. **Persistence bug.** Clicking "Auto" in the confirmation dialog suppresses confirmations for exactly one operation, then the dialog reappears on every subsequent destructive operation. Root cause: `CortexRouter` runs `_session.ResetApproveAll()` in a `finally` after every tool, and `ResetApproveAll()` clears **both** `ApproveAll` and `AutoMode`. `ApproveAll` ("Yes to All") is correctly per-batch; `AutoMode` ("Auto") must persist until the user stops it or the document changes.

2. **Wrong stop affordance.** "Stop Auto" is currently a ribbon button. The user wants it as a dedicated non-modal floating window that appears while Auto mode is active.

## Goals

- Auto mode persists across tool calls until explicitly stopped or the document is reinitialized.
- A non-modal floating window appears when Auto mode activates, stays visible while active, and offers a "Stop Auto" control.
- Closing the window (X) or clicking "Stop Auto" deactivates Auto mode.
- The ribbon "Stop Auto" button is removed.

## Non-goals (YAGNI)

- No persisted window position across activations.
- No countdown/auto-close on the confirmation dialog (the dialog must NOT reappear at all while Auto is on).
- No settings toggle for the window — it is intrinsic to Auto mode.

## Behavior

1. User triggers a destructive operation → TaskDialog appears with four command links: **Yes / Yes to All / Auto / No**.
2. User clicks **Auto** → dialog closes, the current operation proceeds, and a small floating window appears top-center, above Revit (`Topmost=true`).
3. The window shows: a title ("RevitCortex — Auto mode ON"), one explanatory line ("Destructive operations are auto-approved"), and a **Stop Auto** button.
4. From now on **no confirmation dialog appears** — operations proceed silently.
5. User stops Auto two equivalent ways: clicks **Stop Auto**, or closes the window with the **X**. Both set `AutoMode = false`, close the window, and restore confirmation prompts on the next destructive operation.
6. Auto mode also deactivates (and the window closes) automatically on document close/switch (`Reinitialize`).

## Architecture

### New component: `AutoModeWindow`
- Files: `src/RevitCortex.Plugin/UI/AutoModeWindow.xaml` (+ `.xaml.cs`).
- Non-modal WPF `Window`: `Topmost=true`, `ShowInTaskbar=false`, compact `WindowStyle`, `WindowStartupLocation` top-center, `ResizeMode=NoResize`.
- Shown with `Show()` (not `ShowDialog()`), mirroring the existing `UpdateNotificationWindow` pattern.
- Contains a "Stop Auto" button. Both the button click and the window `Closed`/closing event invoke a single callback that deactivates Auto. Guard against re-entrancy: the programmatic `Close()` (when Auto is turned off elsewhere) must not re-trigger the deactivation callback.

### Lifecycle ownership: `RevitCortexApp.OnAutoModeChanged(bool active)`
- Currently enables/disables the ribbon button. Rewire to manage the window:
  - `active == true` → if `_autoModeWindow == null`, create + `Show()`, store reference.
  - `active == false` → if `_autoModeWindow != null`, `Close()` (suppressing the close→deactivate loop) + null the reference.
- Marshal to the UI thread with `Dispatcher.BeginInvoke` (same as `OnUpdateAvailable`) so it is safe even if called from a non-UI path.
- Single instance: at most one window at a time.

### Existing hook reused: `ConfirmationHelper.AutoModeChanged`
- Already fired when "Auto" is clicked (`ConfirmWithSession`), and by `StopAutoMode.NotifyAutoModeChanged(false)`.
- `OnAutoModeChanged` is already subscribed to it. No new events introduced.
- Stop Auto button / window-close → reuse `StopAutoMode` logic: `session.AutoMode = false; ConfirmationHelper.NotifyAutoModeChanged(false);`

### Router fix (already applied)
- `CortexRouter.cs` `finally`: replace `_session.ResetApproveAll()` with `_session.ApproveAll = false;`. Preserves per-batch reset of "Yes to All" without clearing `AutoMode`.

### Reinitialize notification
- `CortexSession.Reinitialize` already sets `AutoMode = false` but does not notify the UI (Core has no reference to `ConfirmationHelper`). The Plugin must fire the notification on document boundary so the window closes. Implement in `RevitCortexApp.OnDocumentClosing`: after `_session.Reinitialize(...)`, call `ConfirmationHelper.NotifyAutoModeChanged(false)`.

### Ribbon removal
- Delete the `stopAutoBtn` block in `CreateRibbonPanel` (the `ID_CORTEX_STOP_AUTO` PushButtonData) and the `_stopAutoButton` field. `OnAutoModeChanged` no longer touches the ribbon.
- Keep the `StopAutoMode` IExternalCommand class only if its deactivation logic is reused; otherwise the window calls the session directly. (Decision: keep a small shared helper for "deactivate Auto" to avoid duplicating the two-line logic.)

## Cross-target compatibility (net48 + net8)

- WPF `Window`, `Topmost`, `Dispatcher.BeginInvoke` are all available on net48 and net8 — no net8-only APIs.
- Must build BOTH `Debug R24` (net48) and `Debug R25` (net8) before commit. R23→R27 for release.

## Testing

- **Unit (Core/Router)** — already green (223 tests, 1 skipped):
  - `Route_DoesNotResetAutoModeAfterToolExecution` — AutoMode survives a tool call.
  - `Route_ResetsApproveAllAfterToolExecution` — ApproveAll still resets per tool.
  - `CortexSessionConfirmationTests` — AutoMode no-timeout, reset on Reinitialize/ResetApproveAll, interaction with ApproveAll/RequestConfirmation.
- **WPF window** — not unit-testable (UI, pulls RevitAPIUI). Verified live on Revit after deploy: click Auto → window appears, run several destructive ops → no dialogs, click Stop Auto / close X → dialog returns; close document → window closes.

## Rollout

1. Implement window + rewire + Reinitialize notification + ribbon removal.
2. Build R24 + R25 (verify), run unit suite.
3. Deploy R25 locally (Revit closed) → live verification on Snowdon Towers.
4. If verified: release v1.0.28 (router fix + window) via `release.ps1`, all targets R23→R27.

## Risks

- **Re-entrancy on close**: programmatic `Close()` firing the deactivation callback → infinite loop or double-deactivation. Mitigation: a `_closingProgrammatically` guard flag in the window.
- **Window outlives document**: mitigated by the Reinitialize notification.
- **Multiple activations**: guarded by single-instance check in `OnAutoModeChanged`.

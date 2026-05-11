# Power BI Live — Phase 0 + Phase 1 — Implementation Spec

**Date:** 2026-05-11  
**Status:** Implemented and partially validated  
**Author:** RevitCortex AI session (Luigi Dattilo, GPA Ingegneria Srl)  
**Review target:** Independent code review against this spec

---

## 1. Scope

This spec covers the **Power BI Live** integration for RevitCortex: a direct push-dataset pipeline from Autodesk Revit to Power BI Service, without intermediate files (no CSV, no OneDrive).

**In scope:**
- Phase 0: MSAL authentication, workspace discovery
- Phase 1: Dataset management, element publish, schedule publish, binding persistence, sign-out, binding inspection

**Out of scope (Phase 2, not yet implemented):**
- Selection publishing (real-time `pbi_publish_selection`)
- Selection watcher (auto-update on Revit `SelectionChanged` event)
- `pbi_bind_document` (explicit binding override tool)

---

## 2. Architecture

```
Claude Desktop
    │  MCP stdio (JSON-RPC)
    ▼
RevitCortex.Server  (C#, net8, win-x64, self-contained:false)
    │  TCP socket :27015 (local)
    ▼
RevitCortex.Plugin  (Revit add-in, net8-windows / net48)
    │  Revit API (main thread)
    │  HTTP REST (background thread via RunWithoutContext)
    ▼
Power BI REST API  (api.powerbi.com)
```

**Key constraint:** Revit runs all plugin code on its UI thread. Any blocking call (HTTP, MSAL) must be offloaded. The pattern used throughout is `RunWithoutContext<T>`: a dedicated `Thread` with `SynchronizationContext = null`, running `Task.GetAwaiter().GetResult()` — this prevents MSAL and HttpClient continuations from trying to marshal back to the WPF/Revit dispatcher, which would deadlock.

---

## 3. Authentication (Phase 0)

### 3.1 MSAL public-client flow

- **Library:** Microsoft.Identity.Client (MSAL.NET)
- **Flow:** Device-code (no browser popup inside Revit)
- **Client:** Public client — no client secret, no client assertion
- **Scopes:** `https://analysis.windows.net/powerbi/api/.default`
- **Tenant:** `53372e72-8a4d-4a86-8745-257d91a1aafc` (GPA Ingegneria Srl, single-tenant)
- **ClientId:** `05d231e9-d720-4c54-8ecd-93a85dbef40b` (custom app registration on GPA Entra)

### 3.2 Token cache

- Stored at `%LOCALAPPDATA%\.revitcortex\msal_cache.bin`
- Encrypted with DPAPI (CurrentUser scope) — bound to Windows user + machine
- Survives Revit restarts; MSAL auto-renews via refresh token
- File deleted by `SignOutAsync()`

### 3.3 Token refresh logic (`PowerBiAuthService.TryAcquireSilentAsync`)

```
1. GetAccountsAsync() → if empty → NotSignedIn
2. AcquireTokenSilent(scopes, account) → get cached result
3. If ExpiresOn - UtcNow < 5 minutes → WithForceRefresh(true) to proactively renew
4. catch MsalUiRequiredException → NotSignedIn
```

Proactive refresh (step 3) prevents a race where `pbi_check_auth` reports valid but the token expires before the HTTP publish completes.

### 3.4 Device-code flow (`PbiCheckAuthTool` with `signIn=true`)

- Runs on a background `Thread` (fire-and-forget pattern via `PowerBiAuthFlowState`)
- Returns immediately to Claude with `{state:"Starting"}` — does NOT block the Revit main thread
- Caller polls with `pbi_check_auth(signIn=false)` to read `userCode` + `verificationUrl`
- User opens `https://microsoft.com/devicelogin`, enters code, completes login in browser
- On completion, `PowerBiAuthFlowState` transitions to `Completed`

### 3.5 App registration requirements (Entra ID)

- Platform: Mobile and desktop / InstalledClient
- Redirect URIs: `http://localhost`, `https://login.microsoftonline.com/common/oauth2/nativeclient`
- API permissions (delegated): `Dataset.ReadWrite.All`, `Report.Read.All`, `Workspace.Read.All`
- Admin consent granted for GPA Ingegneria Srl
- **Manifest:** `allowPublicClient: true` (must be explicit, not null — null causes AADSTS7000218)

### 3.6 Settings file (`~/.revitcortex/powerbi-live.json`)

```json
{
  "ClientId": "05d231e9-d720-4c54-8ecd-93a85dbef40b",
  "TenantId": "53372e72-8a4d-4a86-8745-257d91a1aafc",
  "AllowExternalWrites": true,
  "SelectionDebounceMs": 1000,
  "ProjectBindings": { ... }
}
```

`AllowExternalWrites: false` blocks all push tools with `PermissionDenied`. Default is `false` — must be explicitly set to `true` to enable publishing.

---

## 4. Tools — Phase 0

### `pbi_check_auth`
- **Input:** `signIn: bool = false`
- **Output:** `{signedIn, username, tokenExpiresOn, tokenLifetimeMinutes, allowExternalWrites, ...}`
- **Behavior:** Silent token check. If `signIn=true` and not signed in, starts device-code flow in background.

### `pbi_list_workspaces`
- **Input:** none
- **Output:** `[{id, name, type, isReadOnly}]`
- **Behavior:** Calls `GET /v1.0/myorg/groups`. Requires signed-in token.

### `pbi_sign_out`
- **Input:** none
- **Output:** `{signedOut: true, previousAccount, message}`
- **Behavior:** Calls `SignOutAsync()` — removes accounts from MSAL cache and deletes `msal_cache.bin`.

---

## 5. Tools — Phase 1

### 5.1 `pbi_list_datasets`
- **Input:** `workspaceId: string`
- **Output:** `[{id, name, addRowsAPIEnabled, isRefreshable, ...}]`
- **Behavior:** Calls `GET /v1.0/myorg/groups/{groupId}/datasets`. Filters to push-capable datasets.

### 5.2 `pbi_create_dataset`
- **Input:** `workspaceId: string, datasetName?: string, tables?: string[]`
- **Output:** `{datasetId, datasetName, tables, created: bool}`
- **Behavior:** Idempotent — checks `GetDatasetByNameAsync` first, returns existing id if found. Default tables: Metadata, Elements, Selection.
- **Schema version:** `PowerBiDatasetSchema.CurrentVersion = "1.0"`

### 5.3 `pbi_publish_elements`
- **Input:** `workspaceId?, datasetId?, datasetName?, mode?, categoryFilter?, maxElements?`
- **Threading:** Revit snapshot on main thread → HTTP on background thread (`RunWithoutContext`)
- **Modes:**
  - `replace` (default): DELETE rows from Elements + Metadata tables, then POST snapshot
  - `append`: POST snapshot without deleting (accumulative)
  - `create`: same as replace, errors if dataset not found (no auto-create)
- **Auto-create:** In `replace` mode, if dataset not found by name → creates automatically
- **Binding resolution:** `workspaceId`/`datasetId`/`datasetName` resolved from `ProjectBindings[docKey]` if not supplied
- **Binding save:** After successful publish, writes/updates `ProjectBindings[docKey]`
- **Output:** `{success, workspaceId, datasetId, datasetName, rowCount, batchCount, durationMs, warnings}`

### 5.4 `pbi_publish_schedules`
- **Input:** `workspaceId?, datasetId?, datasetName?, scheduleIds?, mode?, maxRowsPerSchedule?`
- **Threading:** same as `pbi_publish_elements`
- **Modes:** `replace` (default), `append`
- **No auto-create:** dataset must exist; use `pbi_create_dataset` or `pbi_publish_elements` (which auto-creates) first
- **Long-form schema:** one row per cell: `{ScheduleId, ScheduleName, RowIndex, ColumnName, ValueString, ValueNumber, _ExportRunId, _ExportedAt}`
- **Skips:** template schedules, titleblock revision schedules
- **Binding:** same resolution + save logic as `pbi_publish_elements`
- **Output:** `{success, workspaceId, datasetId, datasetName, scheduleCount, rowCount, batchCount, durationMs, warnings}`

### 5.5 `pbi_get_binding`
- **Input:** none
- **Output:** `{bound, docKey, projectName, workspaceId?, datasetId?, datasetName?, updatedAtUtc?}`
- **Behavior:** Read-only. Returns current binding for active document, or `{bound: false}` with tip.

---

## 6. Dataset Schema (v1.0)

Defined in `PowerBiDatasetSchema.cs`. All tables use `addRowsAPIEnabled: true`.

### Metadata table
| Column | Type |
|--------|------|
| ExportRunId | string |
| ExportedAt | datetime |
| SchemaVersion | string |
| ProjectName | string |
| ProjectNumber | string |
| RevitVersion | string |
| ElementCount | int64 |
| ScheduleCount | int64 |

### Elements table (29 columns)
Key columns: `ElementId` (int64), `UniqueId`, `Category`, `Family`, `Type`, `Level`, `Phase`, `Area`, `Volume`, `Length`, `IsStructural`, `_ExportRunId`, `_ExportedAt`, + geometry/parameter columns.

### Schedules table (long-form)
| Column | Type |
|--------|------|
| ScheduleId | int64 |
| ScheduleName | string |
| RowIndex | int64 |
| ColumnName | string |
| ValueString | string |
| ValueNumber | double |
| _ExportRunId | string |
| _ExportedAt | datetime |

### Selection table
| Column | Type |
|--------|------|
| ElementId | int64 |
| UniqueId | string |
| Category | string |
| SelectedAt | datetime |

*(Phase 2 — not yet populated)*

---

## 7. ProjectBindings

### 7.1 Document key algorithm (`ProjectDocumentKey.Compute`)

Priority order (most stable to least):
1. `cloud:<cloudPath.ToString()>` — BIM360/ACC cloud model
2. `projuid:<ProjectInformation.UniqueId>` — stable across Save As
3. `pathhash:<SHA256(normalized path)[0:16]>` — local file fallback
4. `tmp:<Guid>` — last resort (no persistence across sessions)

### 7.2 Binding schema
```json
{
  "WorkspaceId": "be4fd664-...",
  "DatasetId": "abc123...",
  "DatasetName": "RevitCortex Live - Snowdon Towers - v1",
  "ProjectName": "Snowdon Towers",
  "DocumentGuid": "633c39f7-...",
  "LastPathHash": "a1b2c3d4e5f60001",
  "SchemaVersion": "1.0",
  "UpdatedAtUtc": "2026-05-11T10:00:00.000Z"
}
```

Stored in `~/.revitcortex/powerbi-live.json` under `ProjectBindings[docKey]`. Updated atomically (full file rewrite) after every successful publish.

---

## 8. PowerBiServiceClient

Minimal REST wrapper over `HttpClient`. All methods are `async Task<T>`.

| Method | Endpoint |
|--------|----------|
| `ListWorkspacesAsync` | `GET /v1.0/myorg/groups` |
| `ListDatasetsAsync` | `GET /v1.0/myorg/groups/{gid}/datasets` |
| `GetDatasetByNameAsync` | list + filter by name |
| `CreatePushDatasetAsync` | `POST /v1.0/myorg/groups/{gid}/datasets?defaultRetentionPolicy=None` |
| `DeleteRowsAsync` | `DELETE /v1.0/myorg/groups/{gid}/datasets/{did}/tables/{table}/rows` |
| `PostRowsAsync` | `POST /v1.0/myorg/groups/{gid}/datasets/{did}/tables/{table}/rows` (10k rows/batch) |

Error handling: `PowerBiApiException` wraps HTTP status codes. Tools catch `401`, `403`, `404` with actionable `suggestion` messages.

---

## 9. Threading Contract

All tools follow the same pattern:

```
Execute() called on Revit main thread
    │
    ├─ TryAcquireSilentAsync()     → RunWithoutContext (MSAL, network-free from cache)
    ├─ Revit API snapshot          → main thread (safe)
    │    PowerBiElementExporter / PowerBiScheduleExporter
    │    Returns plain List<Dictionary<string,object?>> (no Revit objects)
    │
    └─ PublishAsync()              → RunWithoutContext (HTTP to Power BI API)
         No Revit API calls here
```

`RunWithoutContext<T>`: creates a dedicated `Thread`, sets `SynchronizationContext = null`, runs factory task synchronously, re-throws captured exceptions via `ExceptionDispatchInfo`.

---

## 10. Cross-Target Compatibility

Plugin targets both `net48` (Revit 2023/2024) and `net8-windows` (Revit 2025/2026/2027).

`ElementId` access guarded with:
```csharp
#if REVIT2024_OR_GREATER
    id.Value        // long
#else
    id.IntegerValue // int
#endif
```

No `record` types, no `init` accessors, no `Dictionary.GetValueOrDefault()` in PowerBiLive code — all net48-compatible.

---

## 11. Security

- **No secrets stored:** public MSAL client, no client secret
- **Token never logged:** `PbiCheckAuthTool` returns `tokenExpiresOn` and `tokenLifetimeMinutes` but never the raw `AccessToken`. Audit log records `input_summary` only (no token values).
- **DPAPI encryption:** token cache file bound to Windows user + machine
- **`AllowExternalWrites` gate:** must be `true` in settings for any push to proceed
- **Read-only mode:** RevitCortex global `readOnlyMode` does not automatically block `pbi_` tools — controlled separately by `AllowExternalWrites`

---

## 12. Known Issues / Open Items

| # | Issue | Severity | Status |
|---|-------|----------|--------|
| 1 | `allowExternalWrites` defaults to `false` — easy to miss, publish silently fails | Medium | Known, user sets manually |
| 2 | `pbi_publish_schedules` not yet tested end-to-end | Medium | Pending test |
| 3 | `pbi_sign_out` not yet tested end-to-end | Low | Pending test |
| 4 | Binding auto-resolve not yet tested (requires prior successful publish) | Medium | Pending test |
| 5 | Selection table (Phase 2) is created in dataset but never populated | Low | By design |
| 6 | PBI Desktop crashes with large push datasets (~10k rows) | Medium | Known PBI limitation — use Service |

---

## 13. File Map

```
src/RevitCortex.Plugin/PowerBiLive/
├── PowerBiAuthService.cs          MSAL public-client, device-code, cache DPAPI
├── PowerBiAuthFlowState.cs        Thread-safe state machine for async device-code
├── PowerBiDatasetSchema.cs        Schema v1.0, table names, BuildCreateDatasetBody
├── PowerBiElementExporter.cs      Revit→DTO snapshot (Elements + Metadata rows)
├── PowerBiScheduleExporter.cs     Revit→DTO snapshot (Schedules long-form rows)
├── PowerBiServiceClient.cs        HTTP REST wrapper (HttpClient)
├── PowerBiSettings.cs             Settings, ProjectBinding, ProjectDocumentKey
└── Tools/
    ├── PbiCheckAuthTool.cs
    ├── PbiListWorkspacesTool.cs
    ├── PbiListDatasetsTool.cs
    ├── PbiCreateDatasetTool.cs
    ├── PbiPublishElementsTool.cs
    ├── PbiPublishSchedulesTool.cs
    ├── PbiGetBindingTool.cs
    └── PbiSignOutTool.cs

src/RevitCortex.Server/Tools/ElementTools.cs
    └── MCP wrappers for all 8 pbi_* tools
```

---

## 14. Validated Test Results (2026-05-11)

| Test | Result |
|------|--------|
| `pbi_check_auth(signIn=true)` — device-code flow | ✅ Returns in ~20ms, no Revit freeze |
| `pbi_list_workspaces` — lists GPA workspaces | ✅ 4 workspaces returned |
| `pbi_publish_elements` — Snowdon Towers, 5-category filter | ✅ 1191 rows, 29 columns in PBI Service |
| `pbi_publish_elements` — real model, filtered | ✅ Dataset created, data visible in PBI Desktop |
| `pbi_get_binding` — no binding (cold start) | ✅ Returns `bound:false` with tip |
| `pbi_get_binding` — after deploy (new tool) | ✅ Tool responds correctly |
| `pbi_publish_elements` — binding save after publish | 🔲 Pending (deploy done, test pending) |
| `pbi_publish_schedules` | 🔲 Pending first test |
| `pbi_sign_out` | 🔲 Pending first test |
| Binding auto-resolve (no params) | 🔲 Pending first test |

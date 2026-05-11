# Power BI Live Phase 2A — `pbi_publish_selection` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `pbi_publish_selection` — a tool that snapshots the current Revit selection and pushes it to the `Selection` table in the bound Power BI push dataset.

**Architecture:** One new Plugin tool file (`PbiPublishSelectionTool.cs`) following the exact Phase 1 pattern: snapshot on Revit main thread → plain DTOs → HTTP DELETE+POST on background thread via `PowerBiToolHelper.RunWithoutContext`. One new MCP wrapper in `ElementTools.cs`. The `Selection` table already exists in the dataset schema — no schema changes needed.

**Tech Stack:** C# / .NET 4.8 (R23/R24) + .NET 8 (R25/R26/R27), Autodesk Revit API, Power BI Push Dataset REST API, MSAL.NET, Newtonsoft.Json.

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiPublishSelectionTool.cs` | **Create** | ICortexTool implementation for `pbi_publish_selection` |
| `src/RevitCortex.Server/Tools/ElementTools.cs` | **Modify** (add ~20 lines) | MCP wrapper for `pbi_publish_selection` |
| `tool-schemas.txt` | **Modify** (add 1 line) | Compact schema entry |
| `WORKFLOWS.md` | **Modify** (add section) | Phase 2A workflow |
| `docs/USER_GUIDE.md` | **Modify** (update table row) | PBI Live tool list |

---

## Task 1: Create `PbiPublishSelectionTool.cs`

**Files:**
- Create: `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiPublishSelectionTool.cs`

- [ ] **Step 1: Create the tool file**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Publishes the current Revit selection to the Power BI Selection table.
///
/// Threading contract: same as PbiPublishElementsTool.
///   Step 1: snapshot selected ElementIds on the Revit main thread.
///   Step 2: HTTP DELETE + POST on a background thread.
///
/// Inputs:
///   workspaceId   (string, optional): Power BI group GUID. Resolved from binding if omitted.
///   datasetId     (string, optional): Existing dataset id. Resolved from binding if omitted.
///   datasetName   (string, optional): Dataset name used for lookup. Default from binding or doc name.
///   clearIfEmpty  (bool, optional):   If true, DELETE existing rows even when nothing is selected.
///                                     Default: false (returns warning without touching PBI).
/// </summary>
public class PbiPublishSelectionTool : ICortexTool
{
    public string Name => "pbi_publish_selection";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Publishes the current Revit selection to the Power BI Selection table. " +
        "Each call replaces the previous snapshot (DELETE then POST). " +
        "workspaceId and datasetId can be omitted when a ProjectBinding exists.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // ── Inputs ────────────────────────────────────────────────────────────
        var workspaceId = input["workspaceId"]?.Value<string>();
        var datasetId   = input["datasetId"]?.Value<string>();
        var datasetName = input["datasetName"]?.Value<string>();
        var clearIfEmpty = input["clearIfEmpty"]?.Value<bool>() ?? false;

        // ── Authenticate ──────────────────────────────────────────────────────
        var settings = PowerBiSettings.Load();
        var writeCheck = PowerBiToolHelper.CheckExternalWritesAllowed(settings);
        if (writeCheck != null) return writeCheck;

        var auth = new PowerBiAuthService(settings);
        AuthState authState;
        try { authState = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync()); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Silent token acquisition failed: {ex.Message}",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }

        if (!authState.IsSignedIn || string.IsNullOrEmpty(authState.AccessToken))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true.");

        // ── Snapshot selection on the main thread ─────────────────────────────
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

        // Resolve workspaceId / datasetId / datasetName from ProjectBindings
        var docKey = ProjectDocumentKey.Compute(doc);
        var existingBinding = settings.GetBinding(docKey);

        if (string.IsNullOrWhiteSpace(workspaceId) && existingBinding != null)
            workspaceId = existingBinding.WorkspaceId;
        if (string.IsNullOrWhiteSpace(datasetId) && existingBinding != null)
            datasetId = existingBinding.DatasetId;
        if (string.IsNullOrWhiteSpace(datasetName))
        {
            if (existingBinding != null && !string.IsNullOrWhiteSpace(existingBinding.DatasetName))
                datasetName = existingBinding.DatasetName;
            else
            {
                string projectName = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                datasetName = string.IsNullOrWhiteSpace(projectName)
                    ? "RevitCortex Live - v1"
                    : $"RevitCortex Live - {projectName} - v1";
            }
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces, or run pbi_publish_elements first to create a ProjectBinding.");

        // Read current selection
        List<Dictionary<string, object?>> selectionRows;
        try
        {
            var uiDoc = new UIDocument(doc);
            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count == 0 && !clearIfEmpty)
            {
                return CortexResult<object>.Ok(new
                {
                    success = true,
                    workspaceId,
                    datasetName,
                    table = PowerBiDatasetSchema.TableSelection,
                    rowCount = 0,
                    warning = "Nothing selected in Revit. The Selection table was NOT cleared. Pass clearIfEmpty=true to explicitly clear it.",
                    tip = "Select elements in Revit first, then call pbi_publish_selection again."
                });
            }

            var exportRunId = Guid.NewGuid().ToString();
            var exportedAt = DateTime.UtcNow;
            var docGuid = "";
            var projectId = docKey;
            try { docGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }

            selectionRows = new List<Dictionary<string, object?>>(selectedIds.Count);
            foreach (var id in selectedIds)
            {
                long elementIdValue;
#if REVIT2024_OR_GREATER
                elementIdValue = id.Value;
#else
                elementIdValue = id.IntegerValue;
#endif
                string uniqueId = "";
                try
                {
                    var elem = doc.GetElement(id);
                    if (elem != null) uniqueId = elem.UniqueId ?? "";
                }
                catch { }

                selectionRows.Add(new Dictionary<string, object?>
                {
                    ["_SchemaVersion"] = PowerBiDatasetSchema.CurrentVersion,
                    ["UpdatedAtUtc"]   = exportedAt,
                    ["ProjectId"]      = projectId,
                    ["DocumentGuid"]   = docGuid,
                    ["ElementId"]      = elementIdValue,
                    ["UniqueId"]       = uniqueId,
                    ["SelectionSetId"] = exportRunId
                });
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Selection snapshot failed: {ex.Message}");
        }

        // ── HTTP publish on background thread ─────────────────────────────────
        var sw = Stopwatch.StartNew();
        try
        {
            var result = PowerBiToolHelper.RunWithoutContext(() => PublishAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                selectionRows));

            sw.Stop();

            // Update binding
            try
            {
                string projectName = "";
                string documentGuid = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                try { documentGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
                settings.SetBinding(docKey, new ProjectBinding
                {
                    WorkspaceId   = workspaceId!,
                    DatasetId     = result.DatasetId,
                    DatasetName   = datasetName,
                    ProjectName   = projectName,
                    DocumentGuid  = documentGuid,
                    LastPathHash  = ProjectDocumentKey.ComputePathHash(doc.PathName ?? ""),
                    SchemaVersion = PowerBiDatasetSchema.CurrentVersion
                });
            }
            catch { /* non-critical */ }

            return CortexResult<object>.Ok(new
            {
                success = true,
                workspaceId,
                datasetId = result.DatasetId,
                datasetName,
                table = PowerBiDatasetSchema.TableSelection,
                exportRunId = selectionRows.Count > 0
                    ? selectionRows[0]["SelectionSetId"]?.ToString()
                    : null,
                rowCount = result.RowCount,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired during publish.",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Insufficient permissions in workspace. Power BI API returned 403.",
                suggestion: "Ensure your account has at least Contributor role on the workspace.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Dataset not found: {ex.Message}",
                suggestion: "Run pbi_publish_elements first to auto-create the dataset with all tables including Selection.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Selection publish failed: {ex.Message}");
        }
    }

    // ─── Async publish ────────────────────────────────────────────────────────

    private static async System.Threading.Tasks.Task<PublishSummary> PublishAsync(
        string accessToken,
        string workspaceId,
        string? datasetId,
        string datasetName,
        List<Dictionary<string, object?>> rows)
    {
        using var client = new PowerBiServiceClient(accessToken);

        // Resolve dataset id — with stale-binding detection
        if (string.IsNullOrEmpty(datasetId))
        {
            var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                .ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' not found. Run pbi_publish_elements first to create it.");
            datasetId = existing.Id;
        }
        else
        {
            // Validate cached id is still alive
            try
            {
                var datasets = await client.ListDatasetsAsync(workspaceId).ConfigureAwait(false);
                bool found = false;
                foreach (var ds in datasets)
                    if (ds.Id == datasetId) { found = true; break; }

                if (!found)
                {
                    var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                        .ConfigureAwait(false);
                    if (existing == null)
                        throw new InvalidOperationException(
                            $"Dataset '{datasetName}' not found. Run pbi_publish_elements first to create it.");
                    datasetId = existing.Id;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* proceed with cached id */ }
        }

        // DELETE existing rows, then POST new snapshot
        await client.DeleteRowsAsync(workspaceId, datasetId!,
            PowerBiDatasetSchema.TableSelection).ConfigureAwait(false);

        int posted = 0;
        if (rows.Count > 0)
        {
            var asObjects = rows.ConvertAll(r => (object)r);
            posted = await client.PostRowsAsync(
                workspaceId, datasetId!, PowerBiDatasetSchema.TableSelection,
                asObjects).ConfigureAwait(false);
        }

        return new PublishSummary { DatasetId = datasetId!, RowCount = posted };
    }

    private class PublishSummary
    {
        public string DatasetId { get; set; } = "";
        public int RowCount { get; set; }
    }
}
```

- [ ] **Step 2: Build R25 to verify it compiles**

```
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: `Errori: 0`

- [ ] **Step 3: Build R24 (net48) to verify compatibility**

```
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: `Errori: 0` (warnings for pre-existing nullable issues are OK)

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Plugin/PowerBiLive/Tools/PbiPublishSelectionTool.cs
git commit -m "feat(powerbi-live): PbiPublishSelectionTool - publish Revit selection to PBI Selection table"
```

---

## Task 2: Register the tool in `CortexRouter` / `ToolRegistrar`

- [ ] **Step 1: Find where tools are registered**

```bash
grep -rn "PbiPublishSchedulesTool\|PbiPublishElementsTool\|RegisterTool\|AddTool\|new.*Tool()" \
  src/RevitCortex.Plugin/ | grep -v ".cs:" | head -20
```

Look for a file that instantiates all `ICortexTool` implementations (likely `RevitCortexApp.cs` or a `ToolRegistry.cs`).

- [ ] **Step 2: Add registration**

In the file found above, add `PbiPublishSelectionTool` next to the other `pbi_*` tools. The pattern will be one of:

```csharp
// Pattern A — explicit list
tools.Add(new PbiPublishSelectionTool());

// Pattern B — auto-discovery via reflection (no change needed if all ICortexTool in same assembly)
```

If pattern B (reflection-based), skip this task — the tool registers itself automatically.

- [ ] **Step 3: Build R25 to verify registration compiles**

```
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: `Errori: 0`

- [ ] **Step 4: Commit if pattern A was used**

```bash
git add <registration file>
git commit -m "feat(powerbi-live): register PbiPublishSelectionTool"
```

---

## Task 3: Add MCP wrapper in `ElementTools.cs`

**Files:**
- Modify: `src/RevitCortex.Server/Tools/ElementTools.cs` (after the `PbiGetBinding` method, around line 562)

- [ ] **Step 1: Add the MCP wrapper**

Insert after the `PbiGetBinding` method (after its closing `}`):

```csharp
[McpServerTool(Name = "pbi_publish_selection"), Description("Publishes the current Revit selection to the Power BI Selection table (one row per selected element). Each call replaces the previous snapshot (DELETE then POST). workspaceId and datasetId can be omitted when a ProjectBinding was saved by a previous publish for this document. Returns rowCount=0 with a warning if nothing is selected and clearIfEmpty is false.")]
public static async Task<string> PbiPublishSelection(
    RevitConnectionManager revit,
    [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding exists for this document.")] string? workspaceId = null,
    [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName.")] string? datasetId = null,
    [Description("Dataset name used for lookup. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
    [Description("If true, DELETE the Selection table rows even when nothing is selected in Revit. Default: false.")] bool clearIfEmpty = false,
    CancellationToken ct = default)
{
    var p = new JObject { ["clearIfEmpty"] = clearIfEmpty };
    if (workspaceId != null) p["workspaceId"] = workspaceId;
    if (datasetId != null)   p["datasetId"]   = datasetId;
    if (datasetName != null) p["datasetName"] = datasetName;
    var result = await revit.ExecuteAsync("pbi_publish_selection", p, ct);
    return result.ToString();
}
```

- [ ] **Step 2: Build the MCP server to verify it compiles**

```
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Server/Tools/ElementTools.cs
git commit -m "feat(powerbi-live): MCP wrapper for pbi_publish_selection"
```

---

## Task 4: Update `tool-schemas.txt`

**Files:**
- Modify: `tool-schemas.txt`

- [ ] **Step 1: Add schema entry**

Open `tool-schemas.txt` and find the block of `pbi_*` entries (near `pbi_get_binding`). Add after `pbi_get_binding`:

```
pbi_publish_selection(workspaceId?,datasetId?,datasetName?,clearIfEmpty?:bool=false) → {success,workspaceId,datasetId,datasetName,table,exportRunId?,rowCount,durationMs} | {success,rowCount:0,warning}
```

- [ ] **Step 2: Commit**

```bash
git add tool-schemas.txt
git commit -m "docs: add pbi_publish_selection to tool-schemas.txt"
```

---

## Task 5: Update `WORKFLOWS.md` and `docs/USER_GUIDE.md`

**Files:**
- Modify: `WORKFLOWS.md`
- Modify: `docs/USER_GUIDE.md`

- [ ] **Step 1: Add Phase 2A section to `WORKFLOWS.md`**

Find the "PBI Live" section and append after the existing Phase 1 workflow:

```markdown
### PBI Live — Phase 2A: Publish Selection

Publish the current Revit selection to the Power BI Selection table.

**Prerequisite:** A ProjectBinding must exist (run `pbi_publish_elements` once first).

**Flow:**
1. Select elements in Revit
2. Call `pbi_publish_selection` (no params needed if binding exists)
3. In Power BI, filter/visualize the Selection table by `SelectionSetId` or `ElementId`

**Tool:** `pbi_publish_selection(clearIfEmpty?)`

**Key behaviors:**
- Replace semantics: each call DELETEs previous rows and POSTs the new snapshot
- Empty selection with `clearIfEmpty=false` (default): returns warning, table unchanged
- Empty selection with `clearIfEmpty=true`: clears the table
- Stale binding (dataset manually deleted): falls back to name-lookup (same as publish_elements)
```

- [ ] **Step 2: Update `docs/USER_GUIDE.md` PBI Live tools table**

Find the PBI Live tools table and add a row for `pbi_publish_selection`:

```
| `pbi_publish_selection` | Pubblica la selezione Revit corrente nella tabella Selection del dataset PBI | workspaceId?, datasetId?, datasetName?, clearIfEmpty? |
```

- [ ] **Step 3: Commit**

```bash
git add WORKFLOWS.md docs/USER_GUIDE.md
git commit -m "docs: add pbi_publish_selection to WORKFLOWS.md and USER_GUIDE.md"
```

---

## Task 6: Deploy and test end-to-end

- [ ] **Step 1: Close Revit**

Verify Revit is closed before deploying.

- [ ] **Step 2: Deploy all targets**

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2025
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2024
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2023
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2026
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2027
```

Each should end with `=== Deploy complete ===`.

- [ ] **Step 3: Open Revit 2025, open a model, connect Cortex**

- [ ] **Step 4: Test — nothing selected, clearIfEmpty=false (default)**

Call `pbi_publish_selection` with no params.

Expected response:
```json
{
  "success": true,
  "rowCount": 0,
  "warning": "Nothing selected in Revit. The Selection table was NOT cleared. Pass clearIfEmpty=true to explicitly clear it.",
  "tip": "Select elements in Revit first, then call pbi_publish_selection again."
}
```

- [ ] **Step 5: Test — elements selected, binding auto-resolve**

Select 5-10 elements in Revit, then call `pbi_publish_selection` with no params.

Expected response:
```json
{
  "success": true,
  "workspaceId": "ef3f410b-...",
  "datasetId": "...",
  "datasetName": "RevitCortex Live - Snowdon Towers - v1",
  "table": "Selection",
  "exportRunId": "<guid>",
  "rowCount": <N>,
  "durationMs": <ms>
}
```

Verify in Power BI Service: workspace Test → dataset → Selection table has N rows.

- [ ] **Step 6: Test — clearIfEmpty=true with nothing selected**

Deselect all in Revit, then call:
```json
{ "clearIfEmpty": true }
```

Expected: `rowCount=0`, NO warning, Selection table in PBI is now empty.

- [ ] **Step 7: Commit test sign-off note**

```bash
git commit --allow-empty -m "test(powerbi-live): pbi_publish_selection end-to-end validated"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|-----------------|------|
| `pbi_publish_selection` tool with all 4 inputs | Task 1 |
| Snapshot on main thread, HTTP on background | Task 1 (threading contract in PublishAsync) |
| `clearIfEmpty=false` returns warning without touching PBI | Task 1 (early return) |
| `clearIfEmpty=true` DELETEs even on empty selection | Task 1 (no early return when clearIfEmpty=true) |
| Binding auto-resolve (workspaceId/datasetId/datasetName) | Task 1 |
| Stale-binding detection | Task 1 (PublishAsync stale check) |
| Binding save after publish | Task 1 (settings.SetBinding) |
| MCP wrapper | Task 3 |
| tool-schemas.txt | Task 4 |
| WORKFLOWS.md + USER_GUIDE.md | Task 5 |
| Deploy R23→R27 | Task 6 |
| All test matrix cases | Task 6 |

**Placeholder scan:** No TBDs, no "similar to task N", all code blocks complete. ✅

**Type consistency:**
- `PowerBiDatasetSchema.TableSelection` — used in Task 1, defined in existing `PowerBiDatasetSchema.cs` as `"Selection"` ✅
- `PowerBiDatasetSchema.CurrentVersion` — used in Task 1, defined in existing schema ✅
- `ProjectDocumentKey.Compute(doc)` — used in Task 1, defined in `PowerBiSettings.cs` ✅
- `ProjectDocumentKey.ComputePathHash(string)` — used in Task 1, defined in `PowerBiSettings.cs` ✅
- `PowerBiToolHelper.RunWithoutContext` — used in Task 1, defined in `PowerBiToolHelper.cs` ✅
- `PowerBiToolHelper.CheckExternalWritesAllowed` — used in Task 1, defined in `PowerBiToolHelper.cs` ✅
- `PowerBiSettings.Load()`, `.GetBinding()`, `.SetBinding()` — used in Task 1, defined in `PowerBiSettings.cs` ✅
- `PowerBiAuthService`, `AuthState` — used in Task 1, defined in existing auth files ✅
- `PowerBiServiceClient` — used in Task 1 (PublishAsync), defined in existing client ✅
- `PowerBiApiException.StatusCode` — used in Task 1 catch blocks, defined in existing exception type ✅
- `#if REVIT2024_OR_GREATER` — used for ElementId.Value vs .IntegerValue, same pattern as `PowerBiElementExporter.cs:210-214` ✅

**Tool registration:** Task 2 handles both auto-discovery (reflection) and explicit registration patterns. ✅

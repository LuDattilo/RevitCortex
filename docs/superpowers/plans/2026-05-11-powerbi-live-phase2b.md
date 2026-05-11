# Power BI Live Phase 2B — pbi_query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `pbi_query` — a tool that executes a DAX query against the bound Power BI dataset and selects the matching elements in Revit.

**Architecture:** HTTP POST to `executeQueries` runs on a background thread (no Revit API needed), then the returned `ElementId` list is used to call `UIDocument.Selection.SetElementIds()` on the Revit main thread. Template params (category, level, parameterName/Value, exportRunId) are compiled to DAX internally; a `daxQuery` escape hatch accepts raw DAX.

**Tech Stack:** C# net48/net8, Autodesk.Revit.DB, Power BI REST API v1.0, Newtonsoft.Json, ModelContextProtocol SDK

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs` | Modify | Add `ExecuteQueryAsync` + `ParseElementIds` |
| `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs` | Create | ICortexTool — input validation, DAX generation, orchestration |
| `src/RevitCortex.Server/Tools/ElementTools.cs` | Modify | Add MCP wrapper `pbi_query` after `PbiPublishSelection` |
| `tool-schemas.txt` | Modify | Add compact signature for `pbi_query` |
| `WORKFLOWS.md` | Modify | Add Phase 2B usage example |
| `docs/USER_GUIDE.md` | Modify | Update PBI Live tool table |

---

## Task 1: Add `ExecuteQueryAsync` to `PowerBiServiceClient`

**Files:**
- Modify: `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs`

### Background

`PowerBiServiceClient` currently has `GetAsync`, `PostAsync`, `SendWithRetryAsync`. We need to add a new public method `ExecuteQueryAsync` that POSTs to `groups/{workspaceId}/datasets/{datasetId}/executeQueries` and parses the `[ElementId]` column from the response. The parser must handle both `[ElementId]` and `ElementId` key formats (Power BI sometimes returns bracketed names).

- [ ] **Step 1: Open the file and locate the insertion point**

The `// ─── Rows ─` section ends around line 165 with `DeleteRowsAsync`. Add the new `// ─── DAX Queries ─` section immediately after, before `// ─── HTTP helpers ─`.

- [ ] **Step 2: Add the `ExecuteQueryAsync` method and `ParseElementIds` helper**

Insert after `DeleteRowsAsync` (around line 165):

```csharp
// ─── DAX Queries ──────────────────────────────────────────────────────────

/// <summary>
/// POST groups/{workspaceId}/datasets/{datasetId}/executeQueries with a DAX query.
/// Returns the list of Int64 ElementId values from the first [ElementId] column in the result.
///
/// Requires the Power BI tenant setting "ExecuteQueries.Execute.All" to be enabled.
/// Returns an empty list (not an exception) when the query returns zero rows.
/// </summary>
public async Task<List<long>> ExecuteQueryAsync(
    string workspaceId,
    string datasetId,
    string daxQuery,
    CancellationToken ct = default)
{
    var url = $"groups/{workspaceId}/datasets/{datasetId}/executeQueries";
    var body = new
    {
        queries = new[] { new { query = daxQuery } },
        serializerSettings = new { includeNulls = true }
    };
    var resp = await PostAsync(url, body, ct).ConfigureAwait(false);
    return ParseElementIds(resp);
}

private static List<long> ParseElementIds(JObject responseRoot)
{
    var result = new List<long>();
    var rows = responseRoot["results"]?[0]?["tables"]?[0]?["rows"] as JArray;
    if (rows == null) return result;
    foreach (var row in rows)
    {
        // Power BI may return "[ElementId]" (bracketed) or "ElementId" (plain)
        var val = row["[ElementId]"] ?? row["ElementId"];
        if (val == null) continue;
        try { result.Add(val.Value<long>()); }
        catch { /* skip unparseable values */ }
    }
    return result;
}
```

- [ ] **Step 3: Build to verify it compiles on both targets**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: both succeed with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs
git commit -m "feat(pbi): add ExecuteQueryAsync to PowerBiServiceClient"
```

---

## Task 2: Create `PbiQueryTool.cs`

**Files:**
- Create: `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs`

### Background

This tool orchestrates the entire `pbi_query` flow:
1. Parse + validate inputs
2. Authenticate (MSAL cache, main thread)
3. Resolve dataset (stale-binding detection — same pattern as `PbiPublishSelectionTool`)
4. Build DAX from template params (or use raw `daxQuery`)
5. Call `ExecuteQueryAsync` on a background thread via `PowerBiToolHelper.RunWithoutContext`
6. Apply `maxElements` cap
7. If 0 elements → return success with warning, do NOT touch Revit selection
8. Build `List<ElementId>` on main thread (skip deleted elements)
9. `SetElementIds` or `IsolateElementsTemporary` based on `action`
10. Return structured response

**Threading note:** `Execute()` is called on the Revit main thread. `RunWithoutContext` runs the HTTP on a background thread. The ElementId list comes back to the main thread for the Revit API call — no second `RunWithoutContext` needed because steps 8-9 happen synchronously after `RunWithoutContext` returns.

**DAX escaping:** In DAX, a literal double-quote inside a string is escaped by doubling it: `"O""Connor"`. Apply this to all template-generated string values.

- [ ] **Step 1: Create the file with the full implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Executes a DAX query against the bound Power BI push dataset and selects
/// the matching elements in Revit.
///
/// Threading contract (inverted vs publish tools):
///   Step 1: validate inputs + authenticate on Revit main thread.
///   Step 2: HTTP POST executeQueries on background thread.
///   Step 3: SetElementIds / IsolateElementsTemporary on Revit main thread.
///
/// Inputs:
///   workspaceId     (string, optional): Falls back to ProjectBinding.
///   datasetId       (string, optional): Falls back to binding, then name-lookup.
///   datasetName     (string, optional): Used for lookup when datasetId absent.
///   category        (string, optional): OST code, e.g. "OST_Walls".
///   level           (string, optional): Level name, e.g. "Level 1".
///   parameterName   (string, optional): Column name in Elements table.
///   parameterValue  (string, optional): Value to match (string equality).
///   exportRunId     (string, optional): GUID of a previous publish run.
///   daxQuery        (string, optional): Raw DAX override — takes precedence.
///   action          (string, optional): "select" (default) or "isolate".
///   maxElements     (int, optional): Safety cap, default 5000.
/// </summary>
public class PbiQueryTool : ICortexTool
{
    public string Name => "pbi_query";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Executes a DAX query against the bound Power BI dataset and selects the matching " +
        "elements in Revit. Use template params (category, level, parameterName/Value, " +
        "exportRunId) or supply a raw daxQuery. action='isolate' isolates instead of selecting.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // ── Inputs ────────────────────────────────────────────────────────────
        var workspaceId    = input["workspaceId"]?.Value<string>();
        var datasetId      = input["datasetId"]?.Value<string>();
        var datasetName    = input["datasetName"]?.Value<string>();
        var category       = input["category"]?.Value<string>();
        var level          = input["level"]?.Value<string>();
        var parameterName  = input["parameterName"]?.Value<string>();
        var parameterValue = input["parameterValue"]?.Value<string>();
        var exportRunId    = input["exportRunId"]?.Value<string>();
        var daxQuery       = input["daxQuery"]?.Value<string>();
        var action         = (input["action"]?.Value<string>() ?? "select").ToLowerInvariant();
        var maxElements    = input["maxElements"]?.Value<int>() ?? 5_000;

        // ── Validate action ───────────────────────────────────────────────────
        if (action != "select" && action != "isolate")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Invalid action '{action}'. Allowed: select, isolate.");

        // ── Validate filter params ────────────────────────────────────────────
        bool hasTemplate = !string.IsNullOrWhiteSpace(category)
                        || !string.IsNullOrWhiteSpace(exportRunId);
        bool hasDax      = !string.IsNullOrWhiteSpace(daxQuery);

        if (!hasTemplate && !hasDax)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Specify at least one filter: category, exportRunId, or daxQuery.");

        if (!string.IsNullOrWhiteSpace(parameterName) && string.IsNullOrWhiteSpace(parameterValue))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName requires parameterValue.");

        if (!string.IsNullOrWhiteSpace(parameterValue) && string.IsNullOrWhiteSpace(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterValue requires parameterName.");

        if (hasDax && !daxQuery!.TrimStart().StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "daxQuery must start with EVALUATE.");

        // ── Authenticate ──────────────────────────────────────────────────────
        var settings = PowerBiSettings.Load();
        var writeCheck = PowerBiToolHelper.CheckExternalWritesAllowed(settings);
        if (writeCheck != null) return writeCheck;

        var auth = new PowerBiAuthService(settings);
        AuthState authState;
        try
        {
            authState = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync());
        }
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

        // ── Resolve document + binding ────────────────────────────────────────
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

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
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces.");

        // ── Build DAX ─────────────────────────────────────────────────────────
        string finalDax = hasDax
            ? daxQuery!
            : BuildTemplateDax(category, level, parameterName, parameterValue, exportRunId);

        // ── HTTP on background thread ─────────────────────────────────────────
        var sw = Stopwatch.StartNew();
        List<long> rawIds;

        try
        {
            rawIds = PowerBiToolHelper.RunWithoutContext(() => QueryAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                finalDax));
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired during query.",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI tenant setting 'ExecuteQueries.Execute.All' may be disabled. " +
                "Ask your PBI admin to enable it, or check the Power BI admin portal under " +
                "Tenant settings → Integration settings → Run queries against datasets.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Dataset not found. Run pbi_publish_elements first.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"DAX query failed: {ex.Message}");
        }

        sw.Stop();
        long queryMs = sw.ElapsedMilliseconds;

        // ── 0 results → return without touching selection ─────────────────────
        if (rawIds.Count == 0)
            return CortexResult<object>.Ok(new
            {
                success = true,
                elementCount = 0,
                warning = "Query returned no elements. Current Revit selection unchanged.",
                daxUsed = finalDax,
                durationMs = queryMs
            });

        // ── Apply maxElements cap ─────────────────────────────────────────────
        int? cappedAt = null;
        if (rawIds.Count > maxElements)
        {
            rawIds = rawIds.GetRange(0, maxElements);
            cappedAt = maxElements;
        }

        // ── Build valid ElementIds on main thread ─────────────────────────────
        var validIds = new List<ElementId>();
        foreach (var idVal in rawIds)
        {
#if REVIT2024_OR_GREATER
            var eid = new ElementId(idVal);
#else
            var eid = new ElementId((int)idVal);
#endif
            if (doc.GetElement(eid) != null)
                validIds.Add(eid);
        }

        if (validIds.Count == 0)
            return CortexResult<object>.Ok(new
            {
                success = true,
                elementCount = 0,
                warning = "Query returned ElementIds but none were found in the active document (elements may have been deleted).",
                daxUsed = finalDax,
                durationMs = queryMs
            });

        // ── Apply selection / isolation ───────────────────────────────────────
        var uiDoc = new UIDocument(doc);
        uiDoc.Selection.SetElementIds(validIds);

        if (action == "isolate")
        {
            try
            {
                doc.ActiveView.IsolateElementsTemporary(validIds);
            }
            catch
            {
                // IsolateElementsTemporary can fail on views that don't support it (e.g. sheets)
                // Selection was already applied; silently ignore isolation failure
            }
        }

        // Retrieve resolved datasetId from binding (updated by QueryAsync if stale)
        var updatedBinding = settings.GetBinding(docKey);
        var resolvedDatasetId = updatedBinding?.DatasetId ?? datasetId ?? "";

        return CortexResult<object>.Ok(new
        {
            success = true,
            workspaceId,
            datasetId = resolvedDatasetId,
            datasetName,
            elementCount = validIds.Count,
            action,
            daxUsed = finalDax,
            cappedAt = (object?)cappedAt,
            durationMs = queryMs
        });
    }

    // ─── DAX template builder ─────────────────────────────────────────────────

    private static string BuildTemplateDax(
        string? category,
        string? level,
        string? parameterName,
        string? parameterValue,
        string? exportRunId)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(category))
            conditions.Add($"Elements[Category] = \"{EscapeDax(category!)}\"");

        if (!string.IsNullOrWhiteSpace(level))
            conditions.Add($"Elements[Level] = \"{EscapeDax(level!)}\"");

        if (!string.IsNullOrWhiteSpace(parameterName) && !string.IsNullOrWhiteSpace(parameterValue))
            conditions.Add($"Elements[{parameterName}] = \"{EscapeDax(parameterValue!)}\"");

        if (!string.IsNullOrWhiteSpace(exportRunId))
            conditions.Add($"Elements[ExportRunId] = \"{EscapeDax(exportRunId!)}\"");

        string filterExpr = string.Join(" && ", conditions);

        return
            "EVALUATE SELECTCOLUMNS(\r\n" +
            $"  FILTER(Elements, {filterExpr}),\r\n" +
            "  \"ElementId\", Elements[ElementId]\r\n" +
            ")";
    }

    /// <summary>
    /// Escapes a string value for use inside a DAX string literal.
    /// In DAX, double-quotes are escaped by doubling them: O"Connor -> O""Connor.
    /// </summary>
    private static string EscapeDax(string value) => value.Replace("\"", "\"\"");

    // ─── Async HTTP ───────────────────────────────────────────────────────────

    private static async System.Threading.Tasks.Task<List<long>> QueryAsync(
        string accessToken,
        string workspaceId,
        string? datasetId,
        string datasetName,
        string daxQuery)
    {
        using var client = new PowerBiServiceClient(accessToken);

        // Resolve / validate dataset id (stale-binding detection)
        if (string.IsNullOrEmpty(datasetId))
        {
            var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                .ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' not found. Run pbi_publish_elements first.");
            datasetId = existing.Id;
        }
        else
        {
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
                            $"Dataset '{datasetName}' not found. Run pbi_publish_elements first.");
                    datasetId = existing.Id;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* proceed with cached id */ }
        }

        return await client.ExecuteQueryAsync(workspaceId, datasetId!, daxQuery)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build R25 and R24**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: 0 errors both targets.

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs
git commit -m "feat(pbi): add PbiQueryTool — DAX executeQueries → Revit selection"
```

---

## Task 3: Add MCP wrapper in `ElementTools.cs`

**Files:**
- Modify: `src/RevitCortex.Server/Tools/ElementTools.cs`

### Background

The MCP wrapper follows the exact same pattern as `PbiPublishSelection` (the method just above the `ImportFromPowerBi` method). Insert after `PbiPublishSelection` (around line 580).

- [ ] **Step 1: Open file and locate the insertion point**

Find the closing brace of `PbiPublishSelection` at around line 579, and the comment/attribute for `ImportFromPowerBi` at line 581. Insert between them.

- [ ] **Step 2: Add the wrapper**

Insert between `PbiPublishSelection` and `ImportFromPowerBi`:

```csharp
[McpServerTool(Name = "pbi_query"), Description("Executes a DAX query against the bound Power BI dataset and selects matching elements in Revit. Use template params (category, level, parameterName+parameterValue, exportRunId) for common filters, or supply a raw daxQuery for advanced queries. action='isolate' temporarily isolates the elements instead of selecting. workspaceId and datasetId can be omitted when a ProjectBinding exists. Returns elementCount=0 with a warning when no elements match.")]
public static async Task<string> PbiQuery(
    RevitConnectionManager revit,
    [Description("Power BI workspace (group) GUID. Can be omitted if a ProjectBinding exists.")] string? workspaceId = null,
    [Description("Existing dataset id. If omitted, resolved from ProjectBinding or looked up by datasetName.")] string? datasetId = null,
    [Description("Dataset name for lookup. Default: 'RevitCortex Live - {ProjectName} - v1'.")] string? datasetName = null,
    [Description("OST category code to filter by, e.g. 'OST_Walls'. Applied as Elements[Category] = value.")] string? category = null,
    [Description("Level name to filter by, e.g. 'Level 1'. Applied as Elements[Level] = value.")] string? level = null,
    [Description("Column name in the Elements table to filter by, e.g. 'Mark'. Requires parameterValue.")] string? parameterName = null,
    [Description("Value to match for parameterName. String equality match. Requires parameterName.")] string? parameterValue = null,
    [Description("ExportRunId GUID from a previous pbi_publish_elements run, to reselect that exact snapshot.")] string? exportRunId = null,
    [Description("Raw DAX query starting with EVALUATE. Overrides all template params. Example: EVALUATE SELECTCOLUMNS(FILTER(Elements, Elements[Area] > 50), \"ElementId\", Elements[ElementId])")] string? daxQuery = null,
    [Description("'select' (default) or 'isolate' — isolate temporarily hides all other elements in the active view.")] string? action = null,
    [Description("Maximum number of ElementIds to select. Default: 5000.")] int maxElements = 5000,
    CancellationToken ct = default)
{
    var p = new JObject { ["maxElements"] = maxElements };
    if (workspaceId    != null) p["workspaceId"]    = workspaceId;
    if (datasetId      != null) p["datasetId"]      = datasetId;
    if (datasetName    != null) p["datasetName"]    = datasetName;
    if (category       != null) p["category"]       = category;
    if (level          != null) p["level"]          = level;
    if (parameterName  != null) p["parameterName"]  = parameterName;
    if (parameterValue != null) p["parameterValue"] = parameterValue;
    if (exportRunId    != null) p["exportRunId"]    = exportRunId;
    if (daxQuery       != null) p["daxQuery"]       = daxQuery;
    if (action         != null) p["action"]         = action;
    var result = await revit.ExecuteAsync("pbi_query", p, ct);
    return result.ToString();
}
```

- [ ] **Step 3: Build the server**

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Server/Tools/ElementTools.cs
git commit -m "feat(pbi): add pbi_query MCP wrapper in ElementTools"
```

---

## Task 4: Update `tool-schemas.txt`

**Files:**
- Modify: `tool-schemas.txt`

- [ ] **Step 1: Add the compact signature**

Find the line for `pbi_publish_selection` in `tool-schemas.txt` and add the following line immediately after it:

```
pbi_query(workspaceId?,datasetId?,datasetName?,category?,level?,parameterName?,parameterValue?,exportRunId?,daxQuery?,action?:string="select",maxElements?:int=5000) → {success,workspaceId,datasetId,datasetName,elementCount,action,daxUsed,cappedAt?,durationMs} | {success,elementCount:0,warning,daxUsed,durationMs}
```

- [ ] **Step 2: Commit**

```bash
git add tool-schemas.txt
git commit -m "docs: add pbi_query to tool-schemas.txt"
```

---

## Task 5: Update `WORKFLOWS.md` and `docs/USER_GUIDE.md`

**Files:**
- Modify: `WORKFLOWS.md`
- Modify: `docs/USER_GUIDE.md`

- [ ] **Step 1: Add Phase 2B section to `WORKFLOWS.md`**

Find the "Power BI Live" section and add after the Phase 2A entry:

```markdown
### Phase 2B — pbi_query (PBI → Revit)

Select elements in Revit by querying the Power BI dataset with DAX.

**Select all walls:**
```
pbi_query(category: "OST_Walls")
```

**Select doors on Level 1:**
```
pbi_query(category: "OST_Doors", level: "Level 1")
```

**Reselect a previous publish snapshot:**
```
pbi_query(exportRunId: "<guid from pbi_publish_elements response>")
```

**Advanced — raw DAX (area > 50 m²):**
```
pbi_query(daxQuery: "EVALUATE SELECTCOLUMNS(FILTER(Elements, Elements[Area] > 50), \"ElementId\", Elements[ElementId])")
```

**Notes:**
- Requires Power BI tenant setting `ExecuteQueries.Execute.All` (admin setting)
- `Elements[Category]` stores OST codes (`OST_Walls`), not display names
- `parameterName` refers to dataset column names (fixed schema), not custom Revit parameters
```

- [ ] **Step 2: Update `docs/USER_GUIDE.md` PBI Live table**

Find the PBI Live tools table and add `pbi_query` row:

```markdown
| `pbi_query` | Execute a DAX query against the dataset and select matching elements in Revit | Phase 2B |
```

- [ ] **Step 3: Commit**

```bash
git add WORKFLOWS.md docs/USER_GUIDE.md
git commit -m "docs: add Phase 2B pbi_query to WORKFLOWS and USER_GUIDE"
```

---

## Task 6: Build all targets and deploy

- [ ] **Step 1: Build all 5 Revit targets**

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: all 5 succeed with 0 errors. R27 may require .NET SDK ≥ 10; skip if SDK not available and note in commit message.

- [ ] **Step 2: Publish MCP server**

```bash
dotnet publish src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```

Copy the published output to the Claude Desktop MCP server location (typically `~/.revitcortex/server-v4/`).

- [ ] **Step 3: Deploy plugin**

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

- [ ] **Step 4: Restart Revit + Claude Desktop**

- Open Revit, load the test model, click **Cortex Switch** to start the server
- Restart Claude Desktop to pick up the new MCP server binary

- [ ] **Step 5: Smoke test — verify tool appears**

In Claude Desktop, ask: `pbi_query(category: "OST_Walls")` with a document that has a ProjectBinding from a previous `pbi_publish_elements` run.

Expected response:
```json
{
  "success": true,
  "elementCount": <N>,
  "action": "select",
  "daxUsed": "EVALUATE SELECTCOLUMNS(..."
}
```

- [ ] **Step 6: Test 0-result case**

```
pbi_query(category: "OST_Nonexistent_Category_XYZ")
```

Expected:
```json
{
  "success": true,
  "elementCount": 0,
  "warning": "Query returned no elements. Current Revit selection unchanged."
}
```

- [ ] **Step 7: Test no-filter error**

```
pbi_query()
```

Expected: error with message `"Specify at least one filter: category, exportRunId, or daxQuery."`

- [ ] **Step 8: Commit build artifacts if any, or tag**

```bash
git add -A
git status
# Commit only source changes, not build output
```

---

## Self-Review

**Spec coverage check:**

| Spec section | Covered by task |
|---|---|
| §1 Scope — one tool `pbi_query` | Task 2, Task 3 |
| §2 Threading contract | Task 2 (documented + implemented) |
| §3 DAX endpoint + response parsing | Task 1 (`ExecuteQueryAsync` + `ParseElementIds`) |
| §4.2 All inputs | Task 2 (all params parsed), Task 3 (all params in wrapper) |
| §4.3 DAX template generation | Task 2 (`BuildTemplateDax`) |
| §4.3 DAX string escaping | Task 2 (`EscapeDax`) |
| §4.4 action: select / isolate | Task 2 (both branches) |
| §4.5 Behavior steps 1-8 | Task 2 (complete flow) |
| §4.6 Response shapes | Task 2 (both `Ok` returns) |
| §4.7 All error cases | Task 2 (all catch branches) |
| §5 File list | Tasks 1-5 (all files) |
| §6 `ExecuteQueryAsync` signature | Task 1 (exact match) |
| §7 ElementId resolution + skip deleted | Task 2 (validation loop) |
| §8 Test matrix | Task 6 (smoke tests cover key cases) |
| §9 Known constraints noted in comments | Task 2 (error messages reference constraints) |

All spec requirements covered. ✅

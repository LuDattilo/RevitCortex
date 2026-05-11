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

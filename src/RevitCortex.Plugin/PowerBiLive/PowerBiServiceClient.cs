using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// REST client for Power BI v1.0 API. Covers all endpoints needed for
/// Phase 0 (workspaces) and Phase 1 (datasets, tables, rows).
///
/// Retry policy: 3 attempts, exponential backoff 1s/2s/4s with +-20% jitter.
/// Respects Retry-After header on 429. Non-retryable: 400, 401, 403, 404, 409.
/// </summary>
public class PowerBiServiceClient : IDisposable
{
    private const string BaseUrl = "https://api.powerbi.com/v1.0/myorg/";

    private static readonly int[] RetryableStatusCodes = new[] { 429, 500, 502, 503, 504 };
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private static readonly Random _rng = new Random();

    public PowerBiServiceClient(string accessToken)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ─── Workspaces ──────────────────────────────────────────────────────────

    /// <summary>GET groups — workspaces the user can access.</summary>
    public async Task<List<WorkspaceInfo>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var body = await GetAsync("groups", ct);
        var arr = body["value"] as JArray ?? new JArray();
        var result = new List<WorkspaceInfo>();
        foreach (var w in arr.OfType<JObject>())
        {
            result.Add(new WorkspaceInfo
            {
                Id = w["id"]?.ToString() ?? "",
                Name = w["name"]?.ToString() ?? "",
                Type = w["type"]?.ToString() ?? "",
                State = w["state"]?.ToString() ?? "",
                IsOnDedicatedCapacity = w["isOnDedicatedCapacity"]?.Value<bool>() ?? false
            });
        }
        return result;
    }

    // ─── Datasets ────────────────────────────────────────────────────────────

    /// <summary>GET groups/{groupId}/datasets</summary>
    public async Task<List<DatasetInfo>> ListDatasetsAsync(
        string workspaceId, CancellationToken ct = default)
    {
        var body = await GetAsync($"groups/{workspaceId}/datasets", ct);
        var arr = body["value"] as JArray ?? new JArray();
        var result = new List<DatasetInfo>();
        foreach (var d in arr.OfType<JObject>())
        {
            result.Add(new DatasetInfo
            {
                Id = d["id"]?.ToString() ?? "",
                Name = d["name"]?.ToString() ?? "",
                ConfiguredBy = d["configuredBy"]?.ToString() ?? "",
                IsRefreshable = d["isRefreshable"]?.Value<bool>() ?? false,
                CreatedDate = d["createdDate"]?.ToString() ?? ""
            });
        }
        return result;
    }

    /// <summary>
    /// POST groups/{groupId}/datasets — creates a push dataset.
    /// Returns the new dataset id.
    /// </summary>
    public async Task<string> CreatePushDatasetAsync(
        string workspaceId,
        object datasetBody,
        CancellationToken ct = default)
    {
        var resp = await PostAsync(
            $"groups/{workspaceId}/datasets?defaultRetentionPolicy=None",
            datasetBody, ct);
        var id = resp["id"]?.ToString();
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Power BI did not return a dataset id.");
        return id;
    }

    /// <summary>
    /// Finds a dataset by name in the workspace. Returns null if not found.
    /// </summary>
    public async Task<DatasetInfo?> GetDatasetByNameAsync(
        string workspaceId, string datasetName, CancellationToken ct = default)
    {
        var datasets = await ListDatasetsAsync(workspaceId, ct);
        foreach (var d in datasets)
        {
            if (string.Equals(d.Name, datasetName, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }

    // ─── Rows ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST rows in batches of up to 10,000. Returns total rows posted.
    /// </summary>
    public async Task<int> PostRowsAsync(
        string workspaceId,
        string datasetId,
        string tableName,
        IReadOnlyList<object> rows,
        CancellationToken ct = default)
    {
        const int batchSize = 10_000;
        int posted = 0;
        int i = 0;
        while (i < rows.Count)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<object>();
            for (int j = i; j < rows.Count && j < i + batchSize; j++)
                batch.Add(rows[j]);

            var body = new { rows = batch };
            await PostAsync(
                $"groups/{workspaceId}/datasets/{datasetId}/tables/{Uri.EscapeDataString(tableName)}/rows",
                body, ct);
            posted += batch.Count;
            i += batchSize;
        }
        return posted;
    }

    /// <summary>DELETE .../tables/{tableName}/rows</summary>
    public async Task DeleteRowsAsync(
        string workspaceId,
        string datasetId,
        string tableName,
        CancellationToken ct = default)
    {
        var url = $"groups/{workspaceId}/datasets/{datasetId}/tables/{Uri.EscapeDataString(tableName)}/rows";
        await SendWithRetryAsync(new HttpRequestMessage(HttpMethod.Delete, url), ct);
    }

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

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private async Task<JObject> GetAsync(string relUrl, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, relUrl);
        var body = await SendWithRetryAsync(req, ct);
        return JObject.Parse(body);
    }

    private async Task<JObject> PostAsync(string relUrl, object payload, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, relUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var body = await SendWithRetryAsync(req, ct);
        if (string.IsNullOrWhiteSpace(body)) return new JObject();
        try { return JObject.Parse(body); }
        catch { return new JObject(); }
    }

    private async Task<string> SendWithRetryAsync(
        HttpRequestMessage originalReq, CancellationToken ct)
    {
        Exception? lastEx = null;
        var reqBytes = originalReq.Content != null
            ? await originalReq.Content.ReadAsByteArrayAsync()
            : null;
        var reqContentType = originalReq.Content?.Headers?.ContentType?.ToString();

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            // Re-create the request on retry (HttpRequestMessage is single-use)
            var req = new HttpRequestMessage(originalReq.Method, originalReq.RequestUri);
            foreach (var h in originalReq.Headers)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (reqBytes != null)
            {
                req.Content = new ByteArrayContent(reqBytes);
                if (!string.IsNullOrEmpty(reqContentType))
                    req.Content.Headers.TryAddWithoutValidation("Content-Type", reqContentType);
            }

            HttpResponseMessage? resp = null;
            try
            {
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastEx = ex;
                if (attempt < MaxRetries)
                {
                    await DelayAsync(attempt, null, ct).ConfigureAwait(false);
                    continue;
                }
                throw new InvalidOperationException(
                    $"Power BI request timed out after {MaxRetries + 1} attempts.", ex);
            }

            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return responseBody;

            // Non-retryable
            int sc = (int)resp.StatusCode;
            if (!Array.Exists(RetryableStatusCodes, x => x == sc))
                throw new PowerBiApiException(sc, responseBody);

            // Retryable
            lastEx = new PowerBiApiException(sc, responseBody);
            if (attempt < MaxRetries)
            {
                TimeSpan? retryAfter = null;
                if (resp.Headers.TryGetValues("Retry-After", out var vals))
                {
                    string? first = null;
                    foreach (var v in vals) { first = v; break; }
                    if (first != null && int.TryParse(first, out int secs))
                        retryAfter = TimeSpan.FromSeconds(secs);
                }
                await DelayAsync(attempt, retryAfter, ct).ConfigureAwait(false);
            }
        }

        throw lastEx!;
    }

    private static async Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        double ms = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        ms *= 0.8 + _rng.NextDouble() * 0.4; // +-20% jitter
        var delay = TimeSpan.FromMilliseconds(Math.Min(ms, MaxDelay.TotalMilliseconds));
        if (retryAfter.HasValue && retryAfter.Value > delay) delay = retryAfter.Value;
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}

// ─── Value objects ─────────────────────────────────────────────────────────────

public class WorkspaceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "";
    public bool IsOnDedicatedCapacity { get; set; }
}

public class DatasetInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfiguredBy { get; set; } = "";
    public bool IsRefreshable { get; set; }
    public string CreatedDate { get; set; } = "";
}

public class PowerBiApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public PowerBiApiException(int statusCode, string body)
        : base($"Power BI API error {statusCode}: {Truncate(body, 300)}")
    {
        StatusCode = statusCode;
        ResponseBody = body;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "...";
}

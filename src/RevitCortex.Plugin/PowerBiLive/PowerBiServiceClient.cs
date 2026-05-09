using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Minimal REST client for Power BI v1.0. Phase 0 only needs read endpoints
/// (list workspaces). Subsequent phases add dataset/table/rows operations
/// with batching and retry/backoff.
/// </summary>
public class PowerBiServiceClient : IDisposable
{
    private const string BaseUrl = "https://api.powerbi.com/v1.0/myorg/";
    private readonly HttpClient _http;
    private readonly string _accessToken;

    public PowerBiServiceClient(string accessToken)
    {
        _accessToken = accessToken;
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// GET groups — workspaces the user can access. Returns id/name/type/state/role.
    /// </summary>
    public async Task<List<WorkspaceInfo>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("groups", ct);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Power BI API returned {(int)resp.StatusCode}: {body}");

        var json = JObject.Parse(body);
        var arr = json["value"] as JArray ?? new JArray();
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

    public void Dispose() => _http.Dispose();
}

public class WorkspaceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "";
    public bool IsOnDedicatedCapacity { get; set; }
}

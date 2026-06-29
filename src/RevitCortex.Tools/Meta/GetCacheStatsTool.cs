using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

/// <summary>
/// Diagnostic: returns hit/miss telemetry from the tool-result cache. Not
/// itself cached (every call should reflect current state). Useful for
/// inspecting which cached tools are paying for themselves.
/// </summary>
[ToolSafety(true, false)]
public class GetCacheStatsTool : ICortexTool
{
    public string Name => "get_cache_stats";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Returns hit/miss/entry counters from the tool-result cache for diagnostic use.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var stats = session.Cache.GetStats();

        var perTool = stats.PerTool
            .OrderByDescending(kv => kv.Value.Hits)
            .Select(kv => new
            {
                tool = kv.Key,
                hits = kv.Value.Hits,
                misses = kv.Value.Misses,
                entries = kv.Value.Entries,
                hitRate = (kv.Value.Hits + kv.Value.Misses) == 0
                    ? 0.0
                    : (double)kv.Value.Hits / (kv.Value.Hits + kv.Value.Misses),
            })
            .ToList<object>();

        var totalCalls = stats.TotalHits + stats.TotalMisses;
        return CortexResult<object>.Ok(new
        {
            entryCount = stats.EntryCount,
            estimatedBytes = stats.EstimatedBytes,
            totalHits = stats.TotalHits,
            totalMisses = stats.TotalMisses,
            overallHitRate = totalCalls == 0 ? 0.0 : (double)stats.TotalHits / totalCalls,
            documentVersion = session.DocumentVersion,
            perTool,
        });
    }
}

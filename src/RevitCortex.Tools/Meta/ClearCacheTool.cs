using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

/// <summary>
/// Admin: drops every cached tool result. Use for live debugging when a
/// tool is suspected of returning stale data, or to reset hit-rate
/// telemetry between experiments.
/// </summary>
public class ClearCacheTool : ICortexTool
{
    public string Name => "clear_cache";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Drops every entry from the tool-result cache. Returns the entry count just before flushing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var before = session.Cache.GetStats().EntryCount;
        session.Cache.InvalidateAll();
        return CortexResult<object>.Ok(new
        {
            cleared = before,
            message = before == 0
                ? "Cache was already empty."
                : $"Cleared {before} cache entries.",
        });
    }
}

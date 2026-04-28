using System.Collections.Generic;

namespace RevitCortex.Core.Caching;

/// <summary>
/// Per-tool counters used by <see cref="CacheStats"/>.
/// </summary>
public class ToolStat
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public int Entries { get; set; }
}

/// <summary>
/// Snapshot of cache telemetry. Returned by <see cref="IToolResultCache.GetStats"/>.
/// </summary>
public class CacheStats
{
    public int EntryCount { get; set; }
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public long EstimatedBytes { get; set; }
    public Dictionary<string, ToolStat> PerTool { get; } = new Dictionary<string, ToolStat>();
}

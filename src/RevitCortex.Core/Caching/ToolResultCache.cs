using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Caching;

/// <summary>
/// Default in-memory implementation of <see cref="IToolResultCache"/>.
///
/// Storage is a single <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// <c>"{toolName}|{paramHash}"</c>. The Revit API runs on a single thread but
/// stats reads can come from a tool while a write happens, so all mutating
/// state is either inside the dictionary itself or guarded by interlocked /
/// lock-protected counters.
/// </summary>
public class ToolResultCache : IToolResultCache
{
    private readonly ConcurrentDictionary<string, CachedEntry> _entries =
        new ConcurrentDictionary<string, CachedEntry>();

    private readonly object _statsLock = new object();
    private long _totalHits;
    private long _totalMisses;
    private readonly Dictionary<string, ToolStat> _perTool =
        new Dictionary<string, ToolStat>();

    public bool TryGet(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        out CortexResult<object> result)
    {
        return TryGet(toolName, paramHash, scope, currentDocVersion, out result, out _);
    }

    public bool TryGet(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        out CortexResult<object> result,
        out long estimatedBytes)
    {
        var key = MakeKey(toolName, paramHash);
        if (_entries.TryGetValue(key, out var entry))
        {
            // Scope must match exactly: a Document entry doesn't satisfy a Session lookup.
            if (entry.Scope == scope && IsFresh(entry, currentDocVersion))
            {
                entry.HitCount++;
                RecordHit(toolName);
                result = entry.Result;
                estimatedBytes = entry.EstimatedBytes;
                return true;
            }
        }
        RecordMiss(toolName);
        result = null!;
        estimatedBytes = 0;
        return false;
    }

    public void Set(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        CortexResult<object> result,
        long? knownBytes = null)
    {
        var bytes = knownBytes ?? EstimateBytes(result);
        var entry = new CachedEntry(toolName, paramHash, scope, result, currentDocVersion, bytes);
        var key = MakeKey(toolName, paramHash);
        _entries[key] = entry;
        EnsureToolStat(toolName);
    }

    public void InvalidateScope(CacheScope scope)
    {
        foreach (var kv in _entries.ToArray())
        {
            if (kv.Value.Scope == scope)
                _entries.TryRemove(kv.Key, out _);
        }
    }

    public void InvalidateAll()
    {
        _entries.Clear();
    }

    public CacheStats GetStats()
    {
        var stats = new CacheStats();
        long bytes = 0;
        var perToolEntries = new Dictionary<string, int>();

        foreach (var entry in _entries.Values)
        {
            bytes += entry.EstimatedBytes;
            if (perToolEntries.ContainsKey(entry.ToolName))
                perToolEntries[entry.ToolName]++;
            else
                perToolEntries[entry.ToolName] = 1;
        }

        stats.EntryCount = _entries.Count;
        stats.EstimatedBytes = bytes;

        lock (_statsLock)
        {
            stats.TotalHits = _totalHits;
            stats.TotalMisses = _totalMisses;

            foreach (var kv in _perTool)
            {
                var ts = new ToolStat
                {
                    Hits = kv.Value.Hits,
                    Misses = kv.Value.Misses,
                    Entries = perToolEntries.TryGetValue(kv.Key, out var n) ? n : 0,
                };
                stats.PerTool[kv.Key] = ts;
            }
        }

        // Tools with entries but no hit/miss yet should still appear in PerTool.
        foreach (var kv in perToolEntries)
        {
            if (!stats.PerTool.ContainsKey(kv.Key))
                stats.PerTool[kv.Key] = new ToolStat { Entries = kv.Value };
        }

        return stats;
    }

    private static bool IsFresh(CachedEntry entry, long currentDocVersion)
    {
        // Session entries are immune to doc version drift.
        if (entry.Scope == CacheScope.Session) return true;
        return entry.DocumentVersion == currentDocVersion;
    }

    private static string MakeKey(string toolName, string paramHash) =>
        toolName + "|" + paramHash;

    private void RecordHit(string toolName)
    {
        lock (_statsLock)
        {
            _totalHits++;
            EnsureToolStatNoLock(toolName).Hits++;
        }
    }

    private void RecordMiss(string toolName)
    {
        lock (_statsLock)
        {
            _totalMisses++;
            EnsureToolStatNoLock(toolName).Misses++;
        }
    }

    private void EnsureToolStat(string toolName)
    {
        lock (_statsLock)
        {
            EnsureToolStatNoLock(toolName);
        }
    }

    private ToolStat EnsureToolStatNoLock(string toolName)
    {
        if (!_perTool.TryGetValue(toolName, out var ts))
        {
            ts = new ToolStat();
            _perTool[toolName] = ts;
        }
        return ts;
    }

    private static long EstimateBytes(CortexResult<object> result)
    {
        try
        {
            var json = JsonConvert.SerializeObject(result);
            return Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            return 0;
        }
    }
}

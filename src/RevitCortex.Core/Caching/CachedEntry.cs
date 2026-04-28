using System;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Caching;

/// <summary>
/// One slot in the tool-result cache. Constructor + readonly properties so it
/// works on netstandard2.0 / net48 without C# 9 features.
/// </summary>
public class CachedEntry
{
    public string ToolName { get; }
    public string ParamHash { get; }
    public CacheScope Scope { get; }
    public CortexResult<object> Result { get; }
    public long DocumentVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public long EstimatedBytes { get; }

    public int HitCount { get; set; }

    public CachedEntry(
        string toolName,
        string paramHash,
        CacheScope scope,
        CortexResult<object> result,
        long documentVersion,
        long estimatedBytes)
    {
        ToolName = toolName;
        ParamHash = paramHash;
        Scope = scope;
        Result = result;
        DocumentVersion = documentVersion;
        EstimatedBytes = estimatedBytes;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

using RevitCortex.Core.Results;

namespace RevitCortex.Core.Caching;

/// <summary>
/// In-memory cache for read-only tool results, keyed on (toolName, paramHash).
/// Invalidation is driven by Revit document events (Plugin layer), so the
/// implementation must be thread-safe.
/// </summary>
public interface IToolResultCache
{
    /// <summary>
    /// Look up a cached result. Returns false on miss, on stale
    /// <see cref="CacheScope.Document"/>/<see cref="CacheScope.Transaction"/>
    /// entries (where <paramref name="currentDocVersion"/> moved past the
    /// entry's snapshot), or on scope mismatch.
    /// </summary>
    bool TryGet(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        out CortexResult<object> result);

    /// <summary>
    /// Same as <see cref="TryGet(string,string,CacheScope,long,out CortexResult{object})"/>
    /// but also returns the entry's stored byte estimate, so hit paths don't have
    /// to re-serialize the result just to measure it.
    /// </summary>
    bool TryGet(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        out CortexResult<object> result,
        out long estimatedBytes);

    /// <summary>
    /// Store a successful result. Failures (CortexResult.Fail) MUST NOT be
    /// passed here — caller is responsible for filtering. When the caller has
    /// already serialized the result (e.g. for audit), pass the byte count via
    /// <paramref name="knownBytes"/> to avoid a second serialization.
    /// </summary>
    void Set(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        CortexResult<object> result,
        long? knownBytes = null);

    /// <summary>
    /// Drop all entries with the given scope. Other scopes are untouched.
    /// </summary>
    void InvalidateScope(CacheScope scope);

    /// <summary>
    /// Drop all entries regardless of scope. Used on document close or manual flush.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Snapshot of telemetry counters. Safe to call concurrently.
    /// </summary>
    CacheStats GetStats();
}

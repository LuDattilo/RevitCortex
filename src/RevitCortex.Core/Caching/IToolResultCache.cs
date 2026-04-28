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
    /// Store a successful result. Failures (CortexResult.Fail) MUST NOT be
    /// passed here — caller is responsible for filtering.
    /// </summary>
    void Set(
        string toolName,
        string paramHash,
        CacheScope scope,
        long currentDocVersion,
        CortexResult<object> result);

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

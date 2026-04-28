using RevitCortex.Core.Caching;

namespace RevitCortex.Core.Tools;

/// <summary>
/// Opt-in marker interface for read-only tools whose results can be cached
/// across calls. Implementing this is a contract that the tool produces the
/// same output for the same input, until invalidation fires for the chosen
/// scope. The router pattern-matches on this interface — tools that don't
/// implement it are never cached.
/// </summary>
public interface ICacheableTool
{
    /// <summary>
    /// Lifetime of cached entries for this tool.
    /// </summary>
    CacheScope CacheScope { get; }
}

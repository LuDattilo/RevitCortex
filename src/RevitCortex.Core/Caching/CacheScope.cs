namespace RevitCortex.Core.Caching;

/// <summary>
/// Lifetime / invalidation scope for a cached tool result.
/// </summary>
public enum CacheScope
{
    /// <summary>
    /// Immutable for the lifetime of the Revit session.
    /// Examples: get_project_info, list_schedulable_fields.
    /// </summary>
    Session,

    /// <summary>
    /// Invalidated by any DocumentChanged event.
    /// Examples: get_phases, get_worksets, get_warnings, get_materials,
    /// analyze_model_statistics, get_linked_file_instances.
    /// </summary>
    Document,

    /// <summary>
    /// Invalidated by DocumentChanged AND DocumentSaved/Synchronized.
    /// Use when external sync may bring new state (e.g. workshared models).
    /// </summary>
    Transaction,
}

namespace RiveTT.Core.Caching;

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
    /// Examples: list_phases, list_worksets, list_warnings, list_materials,
    /// analyze_model_statistics, list_linked_file_instances.
    /// </summary>
    Document,

    /// <summary>
    /// Invalidated by DocumentChanged AND DocumentSaved/Synchronized.
    /// Use when external sync may bring new state (e.g. workshared models).
    /// </summary>
    Transaction,
}

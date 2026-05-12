using System;
using System.Collections.Generic;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Saved configuration for a Power BI export. Persisted as JSON in
/// ~/.revitcortex/profiles/<name>.json so the user can reapply a curated
/// selection of categories + parameters across sessions.
/// </summary>
public class PowerBiExportProfile
{
    public string Name { get; set; } = "Default";

    /// <summary>If true, the profile exports existing schedules instead of categories+parameters.</summary>
    public bool UseSchedules { get; set; }

    /// <summary>Schedule (ViewSchedule) IDs to export when <see cref="UseSchedules"/> is true.</summary>
    public List<long> ScheduleIds { get; set; } = new();

    /// <summary>OST_* codes (preferred) or display names of categories to export.</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>Instance parameter names to export. Empty = auto-discover.</summary>
    public List<string> InstanceParameters { get; set; } = new();

    /// <summary>Type parameter names (without [Type] prefix). Empty = none.</summary>
    public List<string> TypeParameters { get; set; } = new();

    public bool IncludeTypeParameters { get; set; }

    public int MaxElements { get; set; } = 10000;

    public string? OutputFolder { get; set; }

    public string? FileName { get; set; }

    /// <summary>If true, export overwrites the same file; if false, timestamps it.</summary>
    public bool OverwriteFile { get; set; } = true;

    /// <summary>If true, re-runs export when the model is saved.</summary>
    public bool AutoExportOnSave { get; set; }

    /// <summary>If true, triggers a Power BI Service dataset refresh after a successful export.</summary>
    public bool TriggerPbiRefresh { get; set; }

    /// <summary>Power BI workspace (group) GUID for the CSV-backed Import dataset.</summary>
    public string? RefreshWorkspaceId { get; set; }

    /// <summary>Power BI dataset GUID of the Import dataset to refresh.</summary>
    public string? RefreshDatasetId { get; set; }

    /// <summary>
    /// Scope mode for the export: "WholeModel" (default), "ActiveView", "Selection".
    /// SheetLink-style 3-state radio. The plugin enforces this on the server side.
    /// </summary>
    public string ScopeMode { get; set; } = "WholeModel";

    /// <summary>
    /// Optional schema mapping mode for Power BI: "Auto" (default — let PBI
    /// infer), "Suggested" (server-derived types), "Custom" (user-edited).
    /// When non-Auto the export writes a sibling .pq file with explicit
    /// Table.TransformColumnTypes so PBI doesn't rely on locale-dependent
    /// inference.
    /// </summary>
    public string SchemaMappingMode { get; set; } = "Auto";

    /// <summary>
    /// Per-column type hints. Only consulted when SchemaMappingMode != "Auto".
    /// </summary>
    public List<ColumnTypeMapping> ColumnTypes { get; set; } = new();

    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

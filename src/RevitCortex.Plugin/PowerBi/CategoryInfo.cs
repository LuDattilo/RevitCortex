namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Lightweight projection of a Revit category for the export wizard UI.
/// Uses the OST_* code as the stable identifier so wizard selections survive
/// across locales (e.g. EN "Walls" vs IT "Muri").
/// </summary>
public class CategoryInfo
{
    public string OstCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int InstanceCount { get; set; }

    /// <summary>
    /// Revit category classification: "Model", "Annotation", "Analytical", "Internal".
    /// Mirrors the tabs used in Visibility/Graphics Overrides.
    /// </summary>
    public string CategoryType { get; set; } = "Model";
}

/// <summary>
/// Lightweight projection of a discovered parameter, with the % of sampled
/// elements that have a non-null value. Coverage helps the user decide
/// whether a parameter is worth exporting.
/// </summary>
public class ParameterInfo
{
    public string Name { get; set; } = "";

    /// <summary>"Instance" or "Type"</summary>
    public string Scope { get; set; } = "Instance";

    /// <summary>Localized parameter group (e.g. "Dimensions", "Identity Data").</summary>
    public string GroupName { get; set; } = "Other";

    /// <summary>Percent of sampled elements that populate this parameter (0-100).</summary>
    public int CoveragePercent { get; set; }

    /// <summary>If true, the parameter is read-only on the sampled elements (cannot be written back).</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>If true, the parameter is shared (came from a shared parameter file).</summary>
    public bool IsShared { get; set; }

    /// <summary>If true, the parameter exists but never had a value in the sample.</summary>
    public bool AlwaysEmpty => CoveragePercent == 0;
}

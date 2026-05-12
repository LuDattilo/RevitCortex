namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Per-column Power BI type hint. When the user activates Suggested or Custom
/// schema mapping, the export tool generates a companion .pq file with
/// explicit <c>Table.TransformColumnTypes</c> calls instead of relying on
/// Power BI's locale-sensitive auto-inference.
/// </summary>
public class ColumnTypeMapping
{
    /// <summary>CSV column header (after any aliasing).</summary>
    public string ColumnName { get; set; } = "";

    /// <summary>
    /// Power BI logical type. One of:
    /// <c>auto</c> (skip mapping for this column),
    /// <c>int</c> (Int64.Type),
    /// <c>number</c> (decimal number),
    /// <c>fixed</c> (Currency.Type / fixed decimal),
    /// <c>percent</c> (Percentage.Type),
    /// <c>bool</c> (Logical.Type),
    /// <c>date</c>, <c>datetime</c>, <c>text</c>, <c>duration</c>.
    /// </summary>
    public string PbiType { get; set; } = "auto";

    /// <summary>
    /// Optional display format hint (e.g. <c>"0.00"</c>, <c>"yyyy-MM-dd"</c>,
    /// <c>"€0.00"</c>). Not used by Power Query type transform itself but
    /// preserved in the profile so downstream model formatting can pick it up.
    /// </summary>
    public string? Format { get; set; }
}

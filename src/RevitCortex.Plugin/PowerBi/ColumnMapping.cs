using System.Collections.Generic;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// One CSV column definition. Three variants:
/// 1. Direct parameter (Source = "param", ParameterName set, Scope set)
/// 2. Built-in field (Source = "field", FieldName in: ElementId | Category | Family | Type)
/// 3. Computed expression (Source = "formula", Formula set with {Param} tokens)
///
/// Header is the column name written to CSV — overrides the original
/// parameter/field name when set. Otherwise the source name is used.
/// </summary>
public class ColumnMapping
{
    /// <summary>Source kind: "param", "field", "formula".</summary>
    public string Source { get; set; } = "param";

    /// <summary>Custom CSV header (alias). Empty = use the source name.</summary>
    public string Header { get; set; } = "";

    /// <summary>For Source="param": the parameter name as in Revit.</summary>
    public string ParameterName { get; set; } = "";

    /// <summary>For Source="param": "Instance" or "Type". Default Instance.</summary>
    public string Scope { get; set; } = "Instance";

    /// <summary>
    /// For Source="field": one of "ElementId", "Category", "Family", "Type".
    /// </summary>
    public string FieldName { get; set; } = "";

    /// <summary>
    /// For Source="formula": expression with placeholders.
    /// Supported tokens:
    ///   {ParamName}        — instance parameter value
    ///   {[Type] ParamName} — type parameter value
    ///   {ElementId}, {Category}, {Family}, {Type}
    /// Other characters are appended literally.
    /// Example: "{Family} - {Type Name} ({Mark})"
    /// </summary>
    public string Formula { get; set; } = "";
}

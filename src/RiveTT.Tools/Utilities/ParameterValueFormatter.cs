using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Converts a <see cref="Parameter"/> into a self-describing value.
///
/// Why this exists: Revit stores lengths in decimal feet, areas in square feet
/// and volumes in cubic feet, whatever the project units are. Tools that returned
/// <c>param.AsDouble()</c> raw produced values like <c>Surface: 122.81</c> on a
/// French metric project — read as 122.81 m² when it is 11.41 m². Every numeric
/// parameter now travels with its converted value, its unit and the raw internal
/// number, so a reader can never mistake one for the other.
/// </summary>
public static class ParameterValueFormatter
{
    public sealed class FormattedValue
    {
        /// <summary>Value in project display units (numeric), or the raw storage value for non-doubles.</summary>
        public object? Value { get; init; }

        /// <summary>Revit's own formatted string, e.g. "1200 mm" or "11.41 m²".</summary>
        public string? DisplayValue { get; init; }

        /// <summary>Unit catalog id of <see cref="Value"/>, e.g. "millimeters". Null for non-doubles.</summary>
        public string? Unit { get; init; }

        /// <summary>Raw Revit internal value (feet / ft² / ft³). Null for non-doubles.</summary>
        public double? InternalValue { get; init; }
    }

    public static FormattedValue Format(Parameter parameter)
    {
        if (parameter == null || !parameter.HasValue)
            return new FormattedValue();

        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    var text = parameter.AsString();
                    return new FormattedValue { Value = text, DisplayValue = text };

                case StorageType.Integer:
                    var integer = parameter.AsInteger();
                    return new FormattedValue
                    {
                        Value = integer,
                        DisplayValue = SafeValueString(parameter) ?? integer.ToString(CultureInfo.InvariantCulture)
                    };

                case StorageType.ElementId:
                    var elementId = parameter.AsElementId();
                    var idValue = GetElementIdValue(elementId);
                    return new FormattedValue
                    {
                        Value = idValue,
                        // AsValueString() on an ElementId parameter yields the referenced
                        // element's name ("RDC"), which is what a caller filtering on
                        // "Level" actually means.
                        DisplayValue = SafeValueString(parameter) ?? idValue.ToString(CultureInfo.InvariantCulture)
                    };

                case StorageType.Double:
                    var internalValue = parameter.AsDouble();
                    ForgeTypeId? unitTypeId = null;
                    try { unitTypeId = parameter.GetUnitTypeId(); } catch { }

                    double? converted = null;
                    string? unitId = null;
                    if (unitTypeId != null && !unitTypeId.Empty())
                    {
                        try
                        {
                            converted = UnitUtils.ConvertFromInternalUnits(internalValue, unitTypeId);
                            unitId = unitTypeId.TypeId;
                        }
                        catch { }
                    }

                    return new FormattedValue
                    {
                        Value = converted ?? internalValue,
                        DisplayValue = SafeValueString(parameter),
                        Unit = ShortUnit(unitId),
                        InternalValue = internalValue
                    };

                default:
                    return new FormattedValue { DisplayValue = SafeValueString(parameter) };
            }
        }
        catch
        {
            return new FormattedValue();
        }
    }

    /// <summary>
    /// Display string used by exports and filters. Prefers Revit's formatted value
    /// (localized, unit-aware, resolves ElementId references to names) and falls
    /// back to the storage value.
    /// </summary>
    public static string DisplayString(Parameter parameter, Document? document = null)
    {
        if (parameter == null) return "";
        var formatted = Format(parameter);
        if (!string.IsNullOrEmpty(formatted.DisplayValue)) return formatted.DisplayValue!;

        if (parameter.StorageType == StorageType.ElementId && document != null)
        {
            var referenced = document.GetElement(parameter.AsElementId());
            if (referenced != null) return referenced.Name ?? "";
        }

        return formatted.Value switch
        {
            null => "",
            double number => number.ToString("F4", CultureInfo.InvariantCulture),
            var other => other.ToString() ?? ""
        };
    }

    private static string? SafeValueString(Parameter parameter)
    {
        try
        {
            var value = parameter.AsValueString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// "autodesk.unit.unit:millimeters-1.0.1" -> "millimeters". The full Forge id
    /// is noise in every response; the leaf name is what a reader needs.
    /// </summary>
    private static string? ShortUnit(string? unitTypeId)
    {
        if (string.IsNullOrEmpty(unitTypeId)) return null;
        var separator = unitTypeId!.LastIndexOf(':');
        var leaf = separator >= 0 ? unitTypeId.Substring(separator + 1) : unitTypeId;
        var dash = leaf.IndexOf('-');
        return dash > 0 ? leaf.Substring(0, dash) : leaf;
    }

    private static long GetElementIdValue(ElementId? id)
    {
        if (id == null) return -1;
        return id.Value;
    }
}

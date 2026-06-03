using System;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitCortex.Tools.Elements;

internal static class ParameterLookup
{
    public static Parameter? FindParameter(
        Element element,
        string? parameterName,
        string? builtInParameterName,
        out string requestedParameter,
        out string? matchedBuiltInParameter)
    {
        requestedParameter = FirstNonEmpty(builtInParameterName, parameterName);
        matchedBuiltInParameter = null;

        if (TryParseBuiltInParameter(builtInParameterName, out var explicitBuiltInParameter))
            return FindBuiltInParameter(element, explicitBuiltInParameter, out matchedBuiltInParameter);

        var namedParameter = FindNamedParameter(element, parameterName);
        if (namedParameter != null)
        {
            matchedBuiltInParameter = GetBuiltInParameterName(namedParameter);
            return namedParameter;
        }

        if (TryParseBuiltInParameter(parameterName, out var implicitBuiltInParameter))
            return FindBuiltInParameter(element, implicitBuiltInParameter, out matchedBuiltInParameter);

        return null;
    }

    public static string? GetBuiltInParameterName(Parameter param)
    {
        var idValue = GetElementIdValue(param.Id);
        if (idValue >= 0 || idValue < int.MinValue || idValue > int.MaxValue)
            return null;

        var builtInParameter = (BuiltInParameter)(int)idValue;
        return Enum.IsDefined(typeof(BuiltInParameter), builtInParameter)
            ? builtInParameter.ToString()
            : null;
    }

    private static Parameter? FindNamedParameter(Element element, string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return null;

        var trimmedName = parameterName.Trim();
        var param = element.LookupParameter(trimmedName);
        if (param != null)
            return param;

        var typeElement = GetTypeElement(element);
        return typeElement?.LookupParameter(trimmedName);
    }

    private static Parameter? FindBuiltInParameter(
        Element element,
        BuiltInParameter builtInParameter,
        out string matchedBuiltInParameter)
    {
        matchedBuiltInParameter = builtInParameter.ToString();

        var param = element.get_Parameter(builtInParameter);
        if (param != null)
            return param;

        var typeElement = GetTypeElement(element);
        return typeElement?.get_Parameter(builtInParameter);
    }

    private static Element? GetTypeElement(Element element)
    {
        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElement = element.Document.GetElement(typeId);
            if (typeElement != null)
                return typeElement;
        }

        var areaReinforcement = element as AreaReinforcement;
        return areaReinforcement?.AreaReinforcementType;
    }

    private static bool TryParseBuiltInParameter(string? value, out BuiltInParameter builtInParameter)
    {
        builtInParameter = default(BuiltInParameter);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        candidate = RemovePrefix(candidate, "Autodesk.Revit.DB.BuiltInParameter.");
        candidate = RemovePrefix(candidate, "BuiltInParameter.");

        int numericValue;
        if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericValue))
        {
            builtInParameter = (BuiltInParameter)numericValue;
            return Enum.IsDefined(typeof(BuiltInParameter), builtInParameter);
        }

        return Enum.TryParse(candidate, true, out builtInParameter)
            && Enum.IsDefined(typeof(BuiltInParameter), builtInParameter);
    }

    private static string RemovePrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value.Substring(prefix.Length)
            : value;
    }

    private static string FirstNonEmpty(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
            return first.Trim();

        return string.IsNullOrWhiteSpace(second) ? "" : second.Trim();
    }

    private static long GetElementIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

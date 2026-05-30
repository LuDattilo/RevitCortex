using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

public static class RebarToolHelpers
{
    public const double MmPerFoot = 304.8;

    public static double ToMm(double feet) => feet * MmPerFoot;
    public static double FromMm(double mm) => mm / MmPerFoot;

    public static TEnum ParseEnum<TEnum>(string? value, string fieldName, out string? error)
        where TEnum : struct, Enum
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"'{fieldName}' is required. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
            return default;
        }
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        error = $"Invalid '{fieldName}' = '{value}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
        return default;
    }

    public enum LayoutRuleKind { Single, FixedNumber, MaximumSpacing, NumberWithSpacing, MinimumClearSpacing }

    public class LayoutSpec
    {
        public LayoutRuleKind Rule;
        public int Number = 2;
        public double ArrayLengthMm;
        public double SpacingMm;
        public bool BarsOnNormalSide = true;
        public bool IncludeFirstBar = true;
        public bool IncludeLastBar = true;
    }

    public static LayoutSpec ParseLayoutSpec(JObject json, out string? error)
    {
        error = null;
        var spec = new LayoutSpec();
        var ruleStr = json["rule"]?.Value<string>();
        switch ((ruleStr ?? "").Trim().ToLowerInvariant())
        {
            case "single": spec.Rule = LayoutRuleKind.Single; break;
            case "fixed_number": spec.Rule = LayoutRuleKind.FixedNumber; break;
            case "maximum_spacing": spec.Rule = LayoutRuleKind.MaximumSpacing; break;
            case "number_with_spacing": spec.Rule = LayoutRuleKind.NumberWithSpacing; break;
            case "minimum_clear_spacing": spec.Rule = LayoutRuleKind.MinimumClearSpacing; break;
            default:
                error = "Invalid layout 'rule'. Valid: single, fixed_number, maximum_spacing, number_with_spacing, minimum_clear_spacing";
                return spec;
        }
        spec.Number = json["number"]?.Value<int?>() ?? 2;
        spec.ArrayLengthMm = json["arrayLengthMm"]?.Value<double?>() ?? 0;
        spec.SpacingMm = json["spacingMm"]?.Value<double?>() ?? 0;
        spec.BarsOnNormalSide = json["barsOnNormalSide"]?.Value<bool?>() ?? true;
        spec.IncludeFirstBar = json["includeFirstBar"]?.Value<bool?>() ?? true;
        spec.IncludeLastBar = json["includeLastBar"]?.Value<bool?>() ?? true;
        return spec;
    }

    public static XYZ ParseXyzMm(JToken token)
    {
        var x = token["x"]?.Value<double?>() ?? 0;
        var y = token["y"]?.Value<double?>() ?? 0;
        var z = token["z"]?.Value<double?>() ?? 0;
        return new XYZ(FromMm(x), FromMm(y), FromMm(z));
    }

    public static IList<Curve> ParseCurveSpecsMm(JArray specs, out string? error)
    {
        error = null;
        var curves = new List<Curve>();
        foreach (var item in specs.OfType<JObject>())
        {
            var type = (item["type"]?.Value<string>() ?? "line").Trim().ToLowerInvariant();
            try
            {
                if (type == "line")
                    curves.Add(Line.CreateBound(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!)));
                else if (type == "arc")
                    curves.Add(Arc.Create(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!), ParseXyzMm(item["mid"]!)));
                else { error = $"Unknown curve type '{type}'. Use 'line' or 'arc'."; return curves; }
            }
            catch (Exception ex) { error = $"Invalid curve geometry: {ex.Message}"; return curves; }
        }
        if (curves.Count == 0) error = "No curves parsed from spec array.";
        return curves;
    }

    public static JObject XyzToDtoMm(XYZ p) => new JObject
    {
        ["x"] = ToMm(p.X), ["y"] = ToMm(p.Y), ["z"] = ToMm(p.Z)
    };

    public static JObject CurveToDtoMm(Curve c)
    {
        var dto = new JObject
        {
            ["type"] = c is Arc ? "arc" : (c is Line ? "line" : c.GetType().Name.ToLowerInvariant()),
            ["start"] = XyzToDtoMm(c.GetEndPoint(0)),
            ["end"] = XyzToDtoMm(c.GetEndPoint(1)),
            ["lengthMm"] = ToMm(c.Length)
        };
        if (c is Arc arc) dto["mid"] = XyzToDtoMm(arc.Evaluate(0.5, true));
        return dto;
    }

    public static (Autodesk.Revit.DB.Structure.Rebar? rebar, CortexResult<object>? error) RequireRebar(Document doc, long? rebarId)
    {
        if (rebarId == null || rebarId <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "rebarId is required"));
        var rebar = doc.GetElement(ToolHelpers.ToElementId(rebarId.Value)) as Autodesk.Revit.DB.Structure.Rebar;
        if (rebar == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No Rebar element with id {rebarId}",
                suggestion: "Use get_rebar_host_data or list_* to find rebar ids"));
        return (rebar, null);
    }

    public static (Element? host, CortexResult<object>? error) RequireHost(Document doc, long? hostId)
    {
        if (hostId == null || hostId <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostId is required"));
        var host = doc.GetElement(ToolHelpers.ToElementId(hostId.Value));
        if (host == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {hostId}"));
        if (!RebarHostData.IsValidHost(host))
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {hostId} ({host.Category?.Name}) is not a valid rebar host",
                suggestion: "Host must be a structural concrete beam/column/wall/floor/foundation. Mark it structural or set a concrete material first."));
        return (host, null);
    }

    public static RebarBarType? ResolveRebarBarType(Document doc, long? typeId, string? typeName)
    {
        if (typeId.HasValue && typeId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(typeId.Value)) is RebarBarType byId) return byId;
        var all = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var byName = all.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return all.FirstOrDefault();
    }

    public static RebarHookType? ResolveRebarHookType(Document doc, long? hookId, string? hookName)
    {
        if (hookId.HasValue && hookId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(hookId.Value)) is RebarHookType byId) return byId;
        if (!string.IsNullOrWhiteSpace(hookName))
            return new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public static RebarShape? ResolveRebarShape(Document doc, long? shapeId, string? shapeName)
    {
        if (shapeId.HasValue && shapeId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(shapeId.Value)) is RebarShape byId) return byId;
        if (!string.IsNullOrWhiteSpace(shapeName))
            return new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .FirstOrDefault(s => s.Name.Equals(shapeName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public static void ApplyLayout(RebarShapeDrivenAccessor acc, LayoutSpec s)
    {
        switch (s.Rule)
        {
            case LayoutRuleKind.Single:
                acc.SetLayoutAsSingle(); break;
            case LayoutRuleKind.FixedNumber:
                acc.SetLayoutAsFixedNumber(s.Number, FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.MaximumSpacing:
                acc.SetLayoutAsMaximumSpacing(FromMm(s.SpacingMm), FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.NumberWithSpacing:
                acc.SetLayoutAsNumberWithSpacing(s.Number, FromMm(s.SpacingMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.MinimumClearSpacing:
                acc.SetLayoutAsMinimumClearSpacing(FromMm(s.SpacingMm), FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
        }
    }

    public static CortexResult<object> MinVersionError(string feature, int minYear)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires Revit {minYear} or newer; the active target does not support it.",
            suggestion: $"Open the model in Revit {minYear}+ to use this feature.");

    /// <summary>
    /// Resolves a rebar coupler "type". There is NO RebarCouplerType element class in the Revit API
    /// (verified by reflection across R23-R27). Couplers are family instances of category
    /// BuiltInCategory.OST_Coupler; their types are FamilySymbols in that category. Resolves by an
    /// explicit element id first (validated to be an OST_Coupler FamilySymbol), then by name, then the
    /// first available coupler symbol in the document.
    /// </summary>
    public static FamilySymbol? ResolveCouplerType(Document doc, long? typeId, string? typeName)
    {
        if (typeId.HasValue && typeId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(typeId.Value)) is FamilySymbol byId &&
            byId.Category != null &&
            (BuiltInCategory)GetCategoryId(byId.Category) == BuiltInCategory.OST_Coupler)
            return byId;

        var all = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_Coupler)
            .Cast<FamilySymbol>()
            .ToList();
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var byName = all.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return all.FirstOrDefault();
    }

    private static long GetCategoryId(Category cat)
    {
#if REVIT2024_OR_GREATER
        return cat.Id.Value;
#else
        return cat.Id.IntegerValue;
#endif
    }

    /// <summary>Resolves the 'end' value for coupler/splice operations: accepts 0 or 1 (default 0).</summary>
    public static int ParseBarEnd(JToken? token, int fallback = 0)
    {
        var v = token?.Value<int?>() ?? fallback;
        return v == 1 ? 1 : 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Colors elements in the active view by grouping them on a parameter value.
/// Supports OST_* category codes or localized display names.
/// Color strategies: customColors array → gradient (blue→red) → random.
/// Mirrors the fork's ColorSplashEventHandler logic.
/// </summary>
public class ColorElementsTool : ICortexTool
{
    public string Name => "color_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private static readonly Random Rng = new Random();

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var categoryName  = input["categoryName"]?.Value<string>();
        var parameterName = input["parameterName"]?.Value<string>();
        var useGradient   = input["useGradient"]?.Value<bool>() ?? false;
        var customColors  = input["customColors"] as JArray;

        if (string.IsNullOrWhiteSpace(categoryName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categoryName is required",
                suggestion: "Use an OST_* code (e.g. OST_Walls) or a localized display name");

        if (string.IsNullOrWhiteSpace(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required",
                suggestion: "E.g. \"Type Name\", \"Level\", \"Comments\"");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var activeView = doc.ActiveView;
        if (activeView == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active view in the document");

        if (!activeView.CanUseTemporaryVisibilityModes())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Cannot modify element overrides in {activeView.ViewType} views. Switch to a 3D or floor plan view.");

        // Resolve category
        Autodesk.Revit.DB.Category? category = ResolveCategory(doc, categoryName!);
        if (category == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Category '{categoryName}' not found",
                suggestion: "Use an OST_* code or the exact localized display name");

        try
        {
            // Collect instances in the active view scoped to the category
            var collector = new FilteredElementCollector(doc, activeView.Id)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType();

            var elements = collector.ToElements();

            if (elements.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"No elements of category '{categoryName}' found in the current view");

            // Group elements by parameter value (instance → type → "None")
            var groups = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);

            foreach (var element in elements)
            {
                var paramVal = GetParameterValue(doc, element, parameterName!);

                if (!groups.ContainsKey(paramVal))
                    groups[paramVal] = new List<ElementId>();

                groups[paramVal].Add(element.Id);
            }

            // Generate a color per group
            var colorMap = GenerateColors(groups.Keys.ToList(), useGradient, customColors);

            // Find solid fill pattern once
            var solidFillId = FindSolidFillPatternId(doc);

            // Apply overrides inside a transaction
            using var tx = new Transaction(doc, "RevitCortex: Color Elements");
            tx.Start();

            try
            {
                var coloringResults = new List<object>();

                foreach (var kvp in groups)
                {
                    var rgb = colorMap[kvp.Key];
                    var color = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);

                    var overrides = new OverrideGraphicSettings();
                    overrides.SetProjectionLineColor(color);
                    overrides.SetSurfaceForegroundPatternColor(color);
                    overrides.SetCutForegroundPatternColor(color);

                    if (solidFillId != ElementId.InvalidElementId)
                    {
                        overrides.SetSurfaceForegroundPatternId(solidFillId);
                        overrides.SetCutForegroundPatternId(solidFillId);
                    }

                    foreach (var id in kvp.Value)
                        activeView.SetElementOverrides(id, overrides);

                    coloringResults.Add(new
                    {
                        parameterValue = kvp.Key,
                        count = kvp.Value.Count,
                        color = new { r = rgb[0], g = rgb[1], b = rgb[2] },
                        elementIds = kvp.Value.Select(GetElementIdLong).ToList()
                    });
                }

                tx.Commit();

                return CortexResult<object>.Ok(new
                {
                    message = $"Colored {elements.Count} element(s) across {groups.Count} group(s)",
                    totalElements = elements.Count,
                    coloredGroups = groups.Count,
                    results = coloringResults
                });
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to color elements: {ex.Message}");
        }
    }

    // ── Category resolution ───────────────────────────────────────────────────

    private static Autodesk.Revit.DB.Category? ResolveCategory(Document doc, string categoryName)
    {
        // 1) Try OST_* enum parse (language-independent)
        if (Enum.TryParse(categoryName, out BuiltInCategory bic) &&
            bic != BuiltInCategory.INVALID)
        {
            try { return Autodesk.Revit.DB.Category.GetCategory(doc, bic); }
            catch { /* fall through */ }
        }

        // 2) Case-insensitive display-name match against all document categories
        foreach (Autodesk.Revit.DB.Category cat in doc.Settings.Categories)
        {
            if (cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                return cat;
        }

        return null;
    }

    // ── Parameter value extraction ────────────────────────────────────────────

    private static string GetParameterValue(Document doc, Element element, string parameterName)
    {
        // Instance parameter first
        var param = element.LookupParameter(parameterName);

        // Fallback to type parameter
        if (param == null)
        {
            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(typeId);
                param = typeElem?.LookupParameter(parameterName);
            }
        }

        if (param == null || !param.HasValue)
            return "None";

        return param.StorageType switch
        {
            StorageType.String  => param.AsString() ?? "None",
            StorageType.Double  => param.AsValueString() ?? param.AsDouble().ToString(),
            StorageType.Integer => param.AsValueString() ?? param.AsInteger().ToString(),
            StorageType.ElementId => ResolveElementIdParamValue(doc, param),
            _ => "None"
        };
    }

    private static string ResolveElementIdParamValue(Document doc, Parameter param)
    {
        var id = param.AsElementId();
        if (id == ElementId.InvalidElementId) return "None";
        var refElem = doc.GetElement(id);
        if (refElem != null) return refElem.Name;
#if REVIT2024_OR_GREATER
        return id.Value.ToString();
#else
        return id.IntegerValue.ToString();
#endif
    }

    // ── Color generation ──────────────────────────────────────────────────────

    private static Dictionary<string, int[]> GenerateColors(
        List<string> keys, bool useGradient, JArray? customColors)
    {
        var map = new Dictionary<string, int[]>(StringComparer.Ordinal);

        if (customColors != null && customColors.Count > 0)
        {
            // Cycle through provided colors
            for (int i = 0; i < keys.Count; i++)
            {
                var token = customColors[i % customColors.Count];
                if (token["r"] != null && token["g"] != null && token["b"] != null)
                {
                    map[keys[i]] = new[]
                    {
                        token["r"]!.Value<int>(),
                        token["g"]!.Value<int>(),
                        token["b"]!.Value<int>()
                    };
                }
                else
                {
                    map[keys[i]] = RandomColor();
                }
            }
        }
        else if (useGradient && keys.Count > 1)
        {
            // Blue (0,0,180) → Red (180,0,0)
            int[] start = { 0, 0, 180 };
            int[] end   = { 180, 0, 0 };

            for (int i = 0; i < keys.Count; i++)
            {
                double t = (double)i / (keys.Count - 1);
                map[keys[i]] = new[]
                {
                    (int)(start[0] + (end[0] - start[0]) * t),
                    (int)(start[1] + (end[1] - start[1]) * t),
                    (int)(start[2] + (end[2] - start[2]) * t)
                };
            }
        }
        else
        {
            foreach (var key in keys)
                map[key] = RandomColor();
        }

        return map;
    }

    private static int[] RandomColor() => new[]
    {
        Rng.Next(30, 200),
        Rng.Next(30, 200),
        Rng.Next(30, 200)
    };

    // ── Solid fill pattern ────────────────────────────────────────────────────

    private static ElementId FindSolidFillPatternId(Document doc)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement));

        foreach (FillPatternElement fpe in collector)
        {
            if (fpe.GetFillPattern().IsSolidFill)
                return fpe.Id;
        }

        return ElementId.InvalidElementId;
    }

    // ── ElementId helper ──────────────────────────────────────────────────────

    private static long GetElementIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

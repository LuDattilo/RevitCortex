using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Colors elements by parameter value and optionally creates a drafting legend view.
/// Supports auto, gradient, and custom color schemes.
/// </summary>
public class CreateColorLegendTool : ICortexTool
{
    public string Name => "create_color_legend";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Colors elements by parameter value and optionally creates a drafting legend view. Supports auto, gradient, and custom color schemes.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required");

        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var colorScheme = input["colorScheme"]?.Value<string>() ?? "auto";
        var customColors = input["customColors"] as JArray;
        var createLegendView = input["createLegendView"]?.Value<bool>() ?? true;
        var legendTitle = input["legendTitle"]?.Value<string>() ?? "Color Legend";

        var targetViewId = input["targetViewId"]?.Value<long>() ?? 0;
        View? targetView;
        if (targetViewId > 0)
        {
#if REVIT2024_OR_GREATER
            targetView = doc.GetElement(new ElementId(targetViewId)) as View;
#else
            targetView = doc.GetElement(new ElementId((int)targetViewId)) as View;
#endif
        }
        else
        {
            targetView = doc.ActiveView;
        }

        if (targetView == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Could not resolve target view");

        if (targetView.ViewType == ViewType.DrawingSheet)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Cannot color elements on a Sheet view. Activate a model view (FloorPlan, Section, or 3D) or pass a targetViewId.",
                suggestion: "Use list_views to find a suitable model view, then pass its ID as targetViewId.");

        try
        {
            // Collect elements by category
            var elements = CollectElements(doc, categories, targetView);
            if (elements.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "No elements found for the specified categories");

            // Group by parameter value
            var groups = GroupByParameterValue(elements, parameterName);

            // Generate color assignments
            var colorMap = GenerateColors(groups.Keys.ToList(), colorScheme, customColors);

            // Apply overrides
            int coloredCount = 0;
            using var tx = new Transaction(doc, "RevitCortex: Create Color Legend");
            tx.Start();

            try
            {
                foreach (var kvp in groups)
                {
                    if (!colorMap.TryGetValue(kvp.Key, out var color)) continue;

                    var overrides = new OverrideGraphicSettings();
                    overrides.SetProjectionLineColor(color);
                    overrides.SetSurfaceForegroundPatternColor(color);

                    // Try to set solid fill pattern
                    var solidFill = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                    if (solidFill != null)
                        overrides.SetSurfaceForegroundPatternId(solidFill.Id);

                    foreach (var elem in kvp.Value)
                    {
                        targetView.SetElementOverrides(elem.Id, overrides);
                        coloredCount++;
                    }
                }

                // Create legend view if requested
                long? legendViewId = null;
                if (createLegendView)
                    legendViewId = CreateLegend(doc, legendTitle, colorMap, groups);

                tx.Commit();

                var colorEntries = colorMap.Select(kvp => new
                {
                    value = kvp.Key,
                    color = $"#{kvp.Value.Red:X2}{kvp.Value.Green:X2}{kvp.Value.Blue:X2}",
                    elementCount = groups.ContainsKey(kvp.Key) ? groups[kvp.Key].Count : 0
                }).ToList();

                return CortexResult<object>.Ok(new
                {
                    coloredElementCount = coloredCount,
                    groupCount = colorMap.Count,
                    colorEntries,
                    legendViewId
                });
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create color legend: {ex.Message}");
        }
    }

    private static List<Element> CollectElements(Document doc, List<string> categories, View view)
    {
        var result = new List<Element>();
        if (categories.Count == 0)
        {
            result.AddRange(new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null));
            return result;
        }

        foreach (var catName in categories)
        {
            var catId = CategoryResolver.ResolveToId(doc, catName);
            if (catId == null) continue;

            result.AddRange(new FilteredElementCollector(doc, view.Id)
                .OfCategoryId(catId)
                .WhereElementIsNotElementType());
        }
        return result;
    }

    private static Dictionary<string, List<Element>> GroupByParameterValue(
        List<Element> elements, string parameterName)
    {
        var groups = new Dictionary<string, List<Element>>();
        foreach (var elem in elements)
        {
            // Try instance parameter first, then fall back to type parameter
            var param = elem.LookupParameter(parameterName);
            if (param == null || !param.HasValue)
            {
                var typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeElem = elem.Document.GetElement(typeId);
                    var typeParam = typeElem?.LookupParameter(parameterName);
                    if (typeParam != null && typeParam.HasValue)
                        param = typeParam;
                }
            }

            if (param == null || !param.HasValue) continue;

            var val = param.StorageType switch
            {
                StorageType.String  => param.AsString() ?? "",
                StorageType.Integer => param.AsValueString() ?? param.AsInteger().ToString(),
                StorageType.Double  => param.AsValueString() ?? param.AsDouble().ToString("F2"),
                StorageType.ElementId => ResolveElementIdValue(elem.Document, param),
                _ => ""
            };

            if (string.IsNullOrEmpty(val)) val = "(empty)";

            if (!groups.ContainsKey(val))
                groups[val] = new List<Element>();
            groups[val].Add(elem);
        }
        return groups;
    }

    private static string ResolveElementIdValue(Document doc, Parameter param)
    {
        var id = param.AsElementId();
        if (id == ElementId.InvalidElementId) return "";
        var refElem = doc.GetElement(id);
        return refElem?.Name ?? id.ToString();
    }

    private static Dictionary<string, Color> GenerateColors(
        List<string> values, string scheme, JArray? customColors)
    {
        var map = new Dictionary<string, Color>();

        // Apply custom colors first
        if (customColors != null)
        {
            foreach (var cc in customColors)
            {
                var v = cc["value"]?.Value<string>();
                if (v == null) continue;
                var r = (byte)(cc["r"]?.Value<int>() ?? 0);
                var g = (byte)(cc["g"]?.Value<int>() ?? 0);
                var b = (byte)(cc["b"]?.Value<int>() ?? 0);
                map[v] = new Color(r, g, b);
            }
        }

        var remaining = values.Where(v => !map.ContainsKey(v)).ToList();

        if (scheme == "gradient")
        {
            for (int i = 0; i < remaining.Count; i++)
            {
                float t = remaining.Count <= 1 ? 0f : (float)i / (remaining.Count - 1);
                var (r, g, b) = HslToRgb(240 - t * 240, 0.8f, 0.5f); // Blue→Red
                map[remaining[i]] = new Color(r, g, b);
            }
        }
        else // auto
        {
            var hues = new[] { 0, 210, 120, 45, 280, 330, 180, 60, 15, 240, 150, 300 };
            for (int i = 0; i < remaining.Count; i++)
            {
                float hue = i < hues.Length ? hues[i] : (i * 137.508f) % 360;
                var (r, g, b) = HslToRgb(hue, 0.7f, 0.5f);
                map[remaining[i]] = new Color(r, g, b);
            }
        }

        return map;
    }

    private static (byte r, byte g, byte b) HslToRgb(float h, float s, float l)
    {
        h /= 360f;
        float r, g, b;
        if (Math.Abs(s) < 0.001f)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1f / 3f);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1f / 3f);
        }
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    private static long? CreateLegend(
        Document doc, string title,
        Dictionary<string, Color> colorMap,
        Dictionary<string, List<Element>> groups)
    {
        // Create a drafting view for the legend
        var draftingViewFamilyType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);

        if (draftingViewFamilyType == null) return null;

        var legendView = ViewDrafting.Create(doc, draftingViewFamilyType.Id);
        legendView.Name = title;
        legendView.Scale = 50;

        // Add text notes for each color entry
        var defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        double yOffset = 0;
        double rowHeight = 0.02; // ~6mm at 1:50

        foreach (var kvp in colorMap)
        {
            var count = groups.ContainsKey(kvp.Key) ? groups[kvp.Key].Count : 0;
            var label = $"{kvp.Key} ({count})";
            var position = new XYZ(0.05, yOffset, 0); // slight indent

            var options = new TextNoteOptions(defaultTextTypeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left
            };
            TextNote.Create(doc, legendView.Id, position, label, options);
            yOffset -= rowHeight;
        }

        return ToolHelpers.GetElementIdValue(legendView.Id);
    }
}

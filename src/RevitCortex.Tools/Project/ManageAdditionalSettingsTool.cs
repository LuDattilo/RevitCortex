using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Manages Revit Additional Settings: line styles, line weights, line patterns,
/// fill patterns, halftone/underlay, and detail levels.
/// Corresponds to Manage → Additional Settings dropdown.
/// </summary>
public class ManageAdditionalSettingsTool : ICortexTool
{
    public string Name => "manage_additional_settings";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Manages Additional Settings (Manage tab): line styles, line weights, line patterns, fill patterns, halftone/underlay. " +
        "Actions: list_line_styles, create_line_style, set_line_style, list_line_weights, " +
        "list_line_patterns, list_fill_patterns, get_halftone, set_halftone.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list_line_styles";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list_line_styles"   => ListLineStyles(doc),
                "create_line_style"  => CreateLineStyle(doc, input, session),
                "set_line_style"     => SetLineStyle(doc, input, session),
                "list_line_weights"  => ListLineWeights(doc),
                "list_line_patterns" => ListLinePatterns(doc),
                "list_fill_patterns" => ListFillPatterns(doc),
                "get_halftone"       => GetHalftone(doc),
                "set_halftone"       => SetHalftone(doc, input, session),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: list_line_styles, create_line_style, set_line_style, " +
                                "list_line_weights, list_line_patterns, list_fill_patterns, get_halftone, set_halftone")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Additional settings operation failed: {ex.Message}");
        }
    }

    // ── LINE STYLES ──────────────────────────────────────────────────────

    private static CortexResult<object> ListLineStyles(Document doc)
    {
        var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

        var sortedSubs = linesCategory.SubCategories
            .Cast<Category>()
            .OrderBy(sub => sub.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var styles = sortedSubs.Select(sub => BuildLineStyleInfo(doc, sub)).ToList();

        return CortexResult<object>.Ok(new
        {
            styleCount = styles.Count,
            lineStyles = styles
        });
    }

    private static CortexResult<object> CreateLineStyle(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

        // Check for duplicate
        foreach (Category sub in linesCategory.SubCategories)
        {
            if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Line style '{name}' already exists");
        }

        if (!session.RequestConfirmation("create line style", 1, name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Line Style");
        tx.Start();

        var newStyle = doc.Settings.Categories.NewSubcategory(linesCategory, name);

        ApplyLineStyleOverrides(doc, newStyle, input);

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "create",
            name   = newStyle.Name,
            id     = ToolHelpers.GetElementIdValue(newStyle.Id)
        });
    }

    private static CortexResult<object> SetLineStyle(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        Category? target = null;
        foreach (Category sub in linesCategory.SubCategories)
        {
            if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                target = sub;
                break;
            }
        }

        if (target == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Line style '{name}' not found");

        if (!session.RequestConfirmation("modify line style", 1, name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Modify Line Style");
        tx.Start();
        ApplyLineStyleOverrides(doc, target, input);
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "set",
            name   = target.Name,
            style  = BuildLineStyleInfo(doc, target)
        });
    }

    private static void ApplyLineStyleOverrides(Document doc, Category style, JObject input)
    {
        var r = input["colorR"]?.Value<int?>();
        var g = input["colorG"]?.Value<int?>();
        var b = input["colorB"]?.Value<int?>();
        if (r.HasValue && g.HasValue && b.HasValue)
            style.LineColor = new Color((byte)r.Value, (byte)g.Value, (byte)b.Value);

        var lineWeight = input["lineWeight"]?.Value<int?>();
        if (lineWeight.HasValue)
            style.SetLineWeight(lineWeight.Value, GraphicsStyleType.Projection);

        var patternName = input["linePatternName"]?.Value<string>();
        if (!string.IsNullOrEmpty(patternName))
        {
            var patternId = FindLinePatternId(doc, patternName!);
            if (patternId != null)
                style.SetLinePatternId(patternId, GraphicsStyleType.Projection);
        }
    }

    // ── LINE WEIGHTS ─────────────────────────────────────────────────────

    private static CortexResult<object> ListLineWeights(Document doc)
    {
        // Line weights are assigned per-category. Collect all top-level categories
        // and return their projection/cut weights.
        var categories = doc.Settings.Categories;
        var results = new List<object>();

        foreach (Category cat in categories)
        {
            try
            {
                var projWeight = cat.GetLineWeight(GraphicsStyleType.Projection);
                var cutWeight  = cat.GetLineWeight(GraphicsStyleType.Cut);

                if (projWeight == null && cutWeight == null) continue;

                results.Add(new
                {
                    category         = cat.Name,
                    projectionWeight = projWeight,
                    cutWeight        = cutWeight
                });
            }
            catch { /* skip unsupported categories */ }
        }

        results = results.OrderBy(r => ((dynamic)r).category.ToString()).ToList();

        return CortexResult<object>.Ok(new
        {
            note = "Line weights 1-16 map to print widths. Use set_line_style to change weights per line style.",
            categoryCount = results.Count,
            categories = results
        });
    }

    // ── LINE PATTERNS ─────────────────────────────────────────────────────

    private static CortexResult<object> ListLinePatterns(Document doc)
    {
        var patterns = new FilteredElementCollector(doc)
            .OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>()
            .Select(lpe => new
            {
                id   = ToolHelpers.GetElementIdValue(lpe.Id),
                name = lpe.Name
            })
            .OrderBy(p => p.name)
            .ToList<object>();

        // Add the built-in Solid pattern entry
        patterns.Insert(0, new
        {
            id   = ToolHelpers.GetElementIdValue(LinePatternElement.GetSolidPatternId()),
            name = "<Solid>"
        });

        return CortexResult<object>.Ok(new
        {
            patternCount = patterns.Count,
            linePatterns = patterns
        });
    }

    // ── FILL PATTERNS ─────────────────────────────────────────────────────

    private static CortexResult<object> ListFillPatterns(Document doc)
    {
        var patterns = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .Select(fpe =>
            {
                var fp = fpe.GetFillPattern();
                return new
                {
                    id       = ToolHelpers.GetElementIdValue(fpe.Id),
                    name     = fpe.Name,
                    target   = fp.Target.ToString(),   // Drafting or Model
                    isSolid  = fp.IsSolidFill
                };
            })
            .OrderBy(p => p.name)
            .ToList<object>();

        return CortexResult<object>.Ok(new
        {
            patternCount = patterns.Count,
            fillPatterns = patterns
        });
    }

    // ── HALFTONE / UNDERLAY ───────────────────────────────────────────────
    // Note: HalftoneAndUnderlaySettings API class name/location varies across Revit versions.
    // We use reflection to call it safely across R23-R27.

    private static CortexResult<object> GetHalftone(Document doc)
    {
        var (settings, getMethod, setMethod, settingsType) = ResolveHalftoneApi(doc);
        if (settings == null)
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "Halftone/Underlay settings API is not available in this Revit version");

        int halftonePercent    = GetIntProp(settings, settingsType!, "HalftonePercent");
        int underlayBrightness = GetIntProp(settings, settingsType!, "BackgroundPatternBrightness");

        return CortexResult<object>.Ok(new
        {
            halftonePercent,
            underlayBrightness
        });
    }

    private static CortexResult<object> SetHalftone(Document doc, JObject input, CortexSession session)
    {
        var (settings, getMethod, setMethod, settingsType) = ResolveHalftoneApi(doc);
        if (settings == null || setMethod == null)
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "Halftone/Underlay settings API is not available in this Revit version");

        var percent = input["halftonePercent"]?.Value<int?>();
        if (percent.HasValue)
        {
            if (percent < 0 || percent > 100)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "halftonePercent must be 0–100");
            settingsType!.GetProperty("HalftonePercent")?.SetValue(settings, percent.Value);
        }

        var underlayBrightness = input["underlayBrightness"]?.Value<int?>();
        if (underlayBrightness.HasValue)
        {
            if (underlayBrightness < 0 || underlayBrightness > 100)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "underlayBrightness must be 0–100");
            settingsType!.GetProperty("BackgroundPatternBrightness")?.SetValue(settings, underlayBrightness.Value);
        }

        if (!session.RequestConfirmation("set halftone/underlay settings", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Halftone/Underlay Settings");
        tx.Start();
        setMethod.Invoke(null, new[] { doc, settings });
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action             = "set",
            halftonePercent    = GetIntProp(settings, settingsType!, "HalftonePercent"),
            underlayBrightness = GetIntProp(settings, settingsType!, "BackgroundPatternBrightness")
        });
    }

    private static (object? settings, System.Reflection.MethodInfo? getMethod,
                    System.Reflection.MethodInfo? setMethod, Type? settingsType)
        ResolveHalftoneApi(Document doc)
    {
        // Try known type names across Revit versions
        string[] typeNames =
        {
            "Autodesk.Revit.DB.HalftoneAndUnderlaySettings",
            "Autodesk.Revit.DB.HalftoneSettings"
        };

        foreach (var typeName in typeNames)
        {
            var type = typeof(Document).Assembly.GetType(typeName);
            if (type == null) continue;

            var getMethod = type.GetMethod("GetHalftoneAndUnderlaySettings",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var setMethod = type.GetMethod("SetHalftoneAndUnderlaySettings",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (getMethod == null) continue;
            var settings = getMethod.Invoke(null, new object[] { doc });
            return (settings, getMethod, setMethod, type);
        }

        return (null, null, null, null);
    }

    private static int GetIntProp(object obj, Type type, string propName)
    {
        try { return (int)(type.GetProperty(propName)?.GetValue(obj) ?? 0); }
        catch { return 0; }
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static object BuildLineStyleInfo(Document doc, Category sub)
    {
        var patternId   = sub.GetLinePatternId(GraphicsStyleType.Projection);
        var patternName = GetLinePatternName(doc, patternId);
        var color       = sub.LineColor;

        return new
        {
            id          = ToolHelpers.GetElementIdValue(sub.Id),
            name        = sub.Name,
            lineWeight  = sub.GetLineWeight(GraphicsStyleType.Projection),
            linePattern = patternName,
            color       = color.IsValid ? $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}" : ""
        };
    }

    private static string GetLinePatternName(Document doc, ElementId patternId)
    {
        if (patternId == null || patternId == ElementId.InvalidElementId) return "Solid";
        var solidId = LinePatternElement.GetSolidPatternId();
        if (patternId.Equals(solidId)) return "Solid";
        return (doc.GetElement(patternId) as LinePatternElement)?.Name ?? "";
    }

    private static ElementId? FindLinePatternId(Document doc, string name)
    {
        if (name.Equals("solid", StringComparison.OrdinalIgnoreCase))
            return LinePatternElement.GetSolidPatternId();

        return new FilteredElementCollector(doc)
            .OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>()
            .FirstOrDefault(lpe => lpe.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}

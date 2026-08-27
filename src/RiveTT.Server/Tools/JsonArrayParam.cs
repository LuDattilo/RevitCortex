using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Tools;

/// <summary>
/// Optional collection parameters travel as a JSON array in a string.
///
/// Why: an OPTIONAL array parameter (<c>long[]? = null</c>, <c>string[]? = null</c>)
/// does not bind — the call fails before the method body runs and the host reports
/// the generic "An error occurred invoking '&lt;tool&gt;'", with nothing reaching
/// Revit. Measured live on 0.2.0, same tool, same session:
/// get_element_parameters(elementIds:[...]) — a REQUIRED array — answers normally,
/// while adding the optional parameterNames:[...] makes the whole call fail.
/// 55 parameters across 41 tools were in that state, which silently removed every
/// category filter, id filter and field list from the surface.
///
/// A required array binds fine, so only optional ones are converted. The JSON
/// string form is what this codebase already used for the parameters that worked
/// (create_stair.runs, create_detail_line.path, filter_elements.levelFilter).
///
/// That fix is necessary but not sufficient: every tool description for these
/// parameters reads "JSON array, e.g. [\"A\",\"B\"]" — which correctly describes
/// what the value IS, and invites a caller (human or model) to pass exactly that,
/// a native JSON array, not a string containing one. Measured live again on
/// 27/08: get_element_parameters(parameterNames:["Number"]) — a genuine JSON array
/// — fails with the same opaque error the string fix was meant to prevent, because
/// the parameter is declared <c>string?</c> and the host's parameter binder cannot
/// coerce a JSON array into a C# string. The <see cref="JsonElement"/> overload
/// below accepts whatever shape actually arrives (array, JSON-encoded string, or a
/// bare scalar) instead of demanding the caller already know the answer to a
/// question the tool's own schema and description do not raise. See the P1.4
/// addendum in PLAN_CORRECTION.md.
/// </summary>
internal static class JsonArrayParam
{
    internal static bool TryParse(string? value, out JArray parsed)
    {
        parsed = new JArray();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value!.Trim();
        try
        {
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                parsed = JArray.Parse(text);
                return true;
            }

            // Tolerate a bare scalar or a comma-separated list: an agent writing
            // categories="Walls" or "Walls,Doors" means the obvious thing, and
            // failing on it would be pedantry.
            var items = text.Split(',');
            var array = new JArray();
            foreach (var item in items)
            {
                var trimmed = item.Trim().Trim('"');
                if (trimmed.Length == 0) continue;
                if (long.TryParse(trimmed, out var number)) array.Add(number);
                else array.Add(trimmed);
            }

            if (array.Count == 0) return false;
            parsed = array;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accepts whatever JSON shape actually arrived for an optional array
    /// parameter: a native JSON array (what every description literally shows),
    /// a JSON-encoded string, or a bare scalar. <see cref="System.Text.Json.JsonElement"/>
    /// is what makes the parameter bind at all when the caller sends an array —
    /// System.Text.Json.JsonElement.
    /// </summary>
    internal static bool TryParse(System.Text.Json.JsonElement? value, out JArray parsed)
    {
        parsed = new JArray();
        if (value is not { } element) return false;

        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Array:
                parsed = JArray.Parse(element.GetRawText());
                return parsed.Count > 0;
            case System.Text.Json.JsonValueKind.String:
                return TryParse(element.GetString(), out parsed);
            case System.Text.Json.JsonValueKind.Number:
                parsed = new JArray { JToken.Parse(element.GetRawText()) };
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Structured refusal in the same shape as a runtime failure, so a malformed
    /// value is never rendered as a broken tool.
    /// </summary>
    internal static string InvalidArrayResult(string tool, string parameterName, string? value)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = "InvalidInput",
                ["tool"] = tool,
                ["message"] = $"{parameterName} must be a JSON array (received: \"{value}\")",
                ["suggestion"] = $"Pass {parameterName} as a JSON array, e.g. [1,2] or [\"Walls\",\"Doors\"]. " +
                                 "A single value or a comma-separated list is also accepted.",
                ["stage"] = "validation",
                ["modelChanged"] = false
            }
        }.ToString();
    }

    /// <summary>Same refusal, for the JsonElement-accepting overload.</summary>
    internal static string InvalidArrayResult(string tool, string parameterName, System.Text.Json.JsonElement? value)
        => InvalidArrayResult(tool, parameterName, value?.ToString());
}

using System;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Tools;

/// <summary>
/// Three-state boolean for the MCP surface: true, false, or "leave unchanged".
///
/// Why it is a string and not a <c>bool?</c>: a nullable boolean parameter makes
/// the whole tool call fail before the method body runs — the host reports the
/// generic "An error occurred invoking '&lt;tool&gt;'" and nothing reaches Revit
/// (no audit entry, no transaction). Measured on this build:
/// list_system_types(category) answers normally, while
/// list_system_types(category, includeLoadable: true) — one added bool? — fails.
/// Nullable long/double/string parameters bind correctly; only bool? is affected.
///
/// Flags with a documented default were therefore made non-nullable and always
/// forwarded. The handful whose third state is meaningful ("mark as building
/// story" vs "leave as is") travel as this string instead, so the distinction
/// survives without the broken type.
/// </summary>
internal static class TriStateFlag
{
    internal static bool TryParse(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value!.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "true":
            case "1":
            case "yes":
            case "oui":
                parsed = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "non":
                parsed = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Structured refusal, in the same shape as a runtime failure, so a bad value
    /// is never rendered as a broken tool.
    /// </summary>
    internal static string InvalidFlagResult(string tool, string parameterName, string? value)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = "InvalidInput",
                ["tool"] = tool,
                ["message"] = $"{parameterName} must be \"true\" or \"false\" (received: \"{value}\")",
                ["suggestion"] = $"Omit {parameterName} to leave the current value unchanged.",
                ["stage"] = "validation",
                ["modelChanged"] = false
            }
        }.ToString();
    }
}

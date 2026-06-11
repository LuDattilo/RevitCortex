using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;

namespace RevitCortex.Tools.Utilities;

/// <summary>
/// Shared helper methods used across many tool implementations.
/// Eliminates boilerplate duplication for common patterns.
/// </summary>
public static class ToolHelpers
{
    /// <summary>
    /// Retrieves the active Revit Document from the session store.
    /// Returns null if no document is open.
    /// </summary>
    public static Document? GetDocument(CortexSession session)
    {
        return session.Store.Get<object>("activeDocument") as Document;
    }

    /// <summary>
    /// Retrieves the active Document or returns a standard failure result.
    /// Use this when early-returning on missing document.
    /// </summary>
    public static (Document? doc, CortexResult<object>? error) RequireDocument(CortexSession session)
    {
        var doc = GetDocument(session);
        if (doc == null)
        {
            return (null, CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                "No active document in session",
                suggestion: "Open a Revit document before using this tool"));
        }
        return (doc, null);
    }

    /// <summary>
    /// Gets the integer/long value of an ElementId, handling the API difference
    /// between Revit 2023 (.IntegerValue) and 2024+ (.Value).
    /// </summary>
    public static long GetElementIdValue(Element? elem)
    {
        if (elem == null) return -1;
#if REVIT2024_OR_GREATER
        return elem.Id.Value;
#else
        return elem.Id.IntegerValue;
#endif
    }

    /// <summary>
    /// Gets the integer/long value of an ElementId directly.
    /// </summary>
    public static long GetElementIdValue(ElementId? id)
    {
        if (id == null || id == ElementId.InvalidElementId) return -1;
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>
    /// Creates an ElementId from a long value, handling the API difference
    /// between Revit 2023 (int constructor) and 2024+ (long constructor).
    /// </summary>
    public static ElementId ToElementId(long rawId)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(rawId);
#else
        return new ElementId((int)rawId);
#endif
    }

    /// <summary>
    /// Shared dryRun reader: preview-first by default. Destructive tools across the
    /// codebase default to dryRun=true; per-tool copy-paste produced divergent
    /// implicit defaults (the steel suite's `?.Value&lt;bool?&gt;() == true` meant
    /// "execute when omitted"). Always read the flag through this helper.
    /// </summary>
    public static bool GetDryRun(JObject input, bool defaultValue = true)
    {
        return input["dryRun"]?.Value<bool>() ?? defaultValue;
    }
}

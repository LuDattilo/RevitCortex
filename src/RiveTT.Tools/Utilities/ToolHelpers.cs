using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;

namespace RiveTT.Tools.Utilities;

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
        return elem.Id.Value;
    }

    /// <summary>
    /// Gets the integer/long value of an ElementId directly.
    /// </summary>
    public static long GetElementIdValue(ElementId? id)
    {
        if (id == null || id == ElementId.InvalidElementId) return -1;
        return id.Value;
    }

    /// <summary>
    /// Creates an ElementId from a long value, handling the API difference
    /// between Revit 2023 (int constructor) and 2024+ (long constructor).
    /// </summary>
    public static ElementId ToElementId(long rawId)
    {
        return new ElementId(rawId);
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

    /// <summary>
    /// The view a view-scoped tool should act on: <c>viewId</c> when given, otherwise the
    /// document's active view.
    ///
    /// Tools that only ever read doc.ActiveView cannot be driven from outside — nothing in
    /// the MCP surface activates a view, so an agent had to ask a human to click the right
    /// tab before every call. Accepting viewId removes that round trip and makes the
    /// operation reproducible.
    /// </summary>
    /// <param name="error">Set when no usable view could be resolved; the tool returns it.</param>
    public static View? ResolveTargetView(Document doc, JObject input, out CortexResult<object>? error)
    {
        error = null;
        var viewId = input["viewId"]?.Value<long?>();

        if (viewId is > 0)
        {
            var element = doc.GetElement(ToElementId(viewId.Value));
            if (element is not View requested)
            {
                error = CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"viewId {viewId} is not a view (found: {element?.GetType().Name ?? "nothing"}).",
                    suggestion: "Pass the element id of a view, or omit viewId to use the active view.");
                return null;
            }

            // A template is not a place to put annotations, and neither is a sheet.
            if (requested.IsTemplate)
            {
                error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"'{requested.Name}' is a view TEMPLATE, not a view.",
                    suggestion: "Pass a real view; apply_view_template edits templates.");
                return null;
            }

            return requested;
        }

        var active = doc.ActiveView;
        if (active == null)
        {
            error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active view in the document, and no viewId was given.",
                suggestion: "Pass viewId explicitly.");
        }
        return active;
    }
}

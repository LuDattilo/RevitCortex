using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Revit ExternalEventHandler that applies a pre-built selection (and optional
/// isolation) on the Revit main thread. Raised by PbiSelectHttpListener when
/// a POST /pbi-select request arrives from the Power BI Desktop custom visual.
///
/// Usage:
///   1. Call Prepare(rawIds, action) on the background thread (before Raise)
///   2. Call ExternalEvent.Raise() to schedule execution
///   3. Revit calls Execute(app) on the main thread — IDs are validated against
///      the live document, then applied to the selection.
///
/// Validation is intentionally deferred to the main thread: the Revit API is
/// strictly single-threaded, so calling Document.GetElement from the listener
/// background thread is unsafe.
/// </summary>
public class PbiSelectionEventHandler : IExternalEventHandler
{
    private volatile IList<long>? _pendingRawIds;
    private volatile string _pendingAction = "select";

    /// <summary>
    /// Sets the raw IDs and action to apply on the next Execute call.
    /// Thread-safe: called from the PbiSelectHttpListener background thread.
    /// IDs are validated against the active document on the main thread inside Execute.
    /// </summary>
    public void Prepare(IList<long> rawIds, string action)
    {
        _pendingRawIds = rawIds;
        _pendingAction = action ?? "select";
    }

    public void Execute(UIApplication app)
    {
        var rawIds = _pendingRawIds;
        var action = _pendingAction;
        _pendingRawIds = null;

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectionEventHandler] Execute called: rawIds={rawIds?.Count ?? 0}, action={action}");

        if (rawIds == null || rawIds.Count == 0) return;

        var doc = app?.ActiveUIDocument?.Document;
        if (doc == null)
        {
            System.Diagnostics.Trace.WriteLine(
                "[PbiSelectionEventHandler] Execute aborted: doc is null on UI thread.");
            return;
        }

        try
        {
            // Validate IDs on the main thread — the only place Document access is safe.
            var validIds = new List<ElementId>(rawIds.Count);
            foreach (var idVal in rawIds)
            {
#if REVIT2024_OR_GREATER
                var eid = new ElementId(idVal);
#else
                var eid = new ElementId((int)idVal);
#endif
                if (doc.GetElement(eid) != null)
                    validIds.Add(eid);
            }

            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectionEventHandler] Validated {validIds.Count}/{rawIds.Count} ids.");

            if (validIds.Count == 0) return;

            var uiDoc = new UIDocument(doc);
            uiDoc.Selection.SetElementIds(validIds);

            if (action == "isolate")
            {
                try
                {
                    // IsolateElementsTemporary manages its own transaction internally;
                    // wrapping it in an external Transaction throws "modifiable document".
                    doc.ActiveView.IsolateElementsTemporary(validIds);
                }
                catch (Exception ex)
                {
                    // Some view types (sheets, schedules) don't support temporary
                    // isolation. Selection is already applied — non-fatal.
                    System.Diagnostics.Trace.WriteLine(
                        $"[PbiSelectionEventHandler] IsolateElementsTemporary failed (non-fatal): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectionEventHandler] Execute error: {ex.Message}");
        }
    }

    public string GetName() => "RevitCortex PBI Selection";
}

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
///   1. Call Prepare(validIds, action) on the background thread (before Raise)
///   2. Call ExternalEvent.Raise() to schedule execution
///   3. Revit calls Execute(app) on the main thread — selection is applied
/// </summary>
public class PbiSelectionEventHandler : IExternalEventHandler
{
    private volatile IList<ElementId>? _pendingIds;
    private volatile string _pendingAction = "select";

    /// <summary>
    /// Sets the ids and action to apply on the next Execute call.
    /// Thread-safe: called from the PbiSelectHttpListener background thread.
    /// </summary>
    public void Prepare(IList<ElementId> validIds, string action)
    {
        _pendingIds = validIds;
        _pendingAction = action ?? "select";
    }

    public void Execute(UIApplication app)
    {
        var ids = _pendingIds;
        var action = _pendingAction;
        _pendingIds = null;

        if (ids == null || ids.Count == 0) return;

        var doc = app?.ActiveUIDocument?.Document;
        if (doc == null) return;

        try
        {
            var uiDoc = new UIDocument(doc);
            uiDoc.Selection.SetElementIds(ids);

            if (action == "isolate")
            {
                try
                {
                    using var tx = new Transaction(doc, "PBI isolate");
                    tx.Start();
                    doc.ActiveView.IsolateElementsTemporary(ids);
                    tx.Commit();
                }
                catch
                {
                    // IsolateElementsTemporary can fail on views that don't support it
                    // (e.g. sheets, schedules). Selection is already applied — non-fatal.
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

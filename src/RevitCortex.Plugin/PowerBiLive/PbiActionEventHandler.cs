using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Revit ExternalEventHandler that handles all PBI Desktop visual actions on the
/// main thread: select, isolate, color override, reset overrides, create 3D view.
///
/// Synchronous-from-listener pattern:
///   1. Background thread acquires the handler lock (AcquireLock)
///   2. Calls Prepare* + ExternalEvent.Raise()
///   3. Calls WaitForCompletion() — blocks on a ManualResetEventSlim
///   4. Revit invokes Execute(UIApp) on main thread → does the work → Set()
///   5. Background thread unblocks and reports the result
///
/// This lets the HTTP listener wait for the UI-thread action to complete
/// before writing the HTTP response. Timeout (10 s) protects against a hung
/// Execute. No user-visible confirmation dialogs — PBI users would find them
/// disruptive in a per-click workflow.
/// </summary>
public class PbiActionEventHandler : IExternalEventHandler
{
    /// <summary>The action kind currently queued for Execute.</summary>
    public enum Kind
    {
        Select,
        Color,
        ResetOverrides,
        CreateView,
    }

    // ─── Pending state (only one action in flight at a time) ───────────────

    private volatile Kind _pendingKind = Kind.Select;
    private volatile IList<long>? _pendingIds;
    private volatile string _pendingAction = "select";
    private volatile IList<PbiSelectHttpListener.ColorOverride>? _pendingColors;
    private volatile string? _pendingViewName;

    // Result signalling — the background thread sets _done after Execute completes.
    private readonly ManualResetEventSlim _done = new(initialState: false);
    private volatile string? _result;     // null = cancelled / failed
    private readonly object _gate = new(); // serialises Prepare/Wait calls

    public string? LastResult => _result;

    /// <summary>How long the listener waits for Execute to complete before timing out.</summary>
    public TimeSpan ExecuteTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Acquires the lock so the listener can prepare + raise + wait atomically.
    /// Returns a disposable to release the lock when done.
    /// </summary>
    public IDisposable AcquireLock() => new LockHandle(_gate);

    private sealed class LockHandle : IDisposable
    {
        private readonly object _gate;
        private bool _taken;
        public LockHandle(object gate) { _gate = gate; Monitor.Enter(_gate); _taken = true; }
        public void Dispose() { if (_taken) { Monitor.Exit(_gate); _taken = false; } }
    }

    /// <summary>Called by the listener BEFORE Raise(), inside AcquireLock().</summary>
    public void PrepareSelection(IList<long> rawIds, string action)
    {
        _pendingKind = Kind.Select;
        _pendingIds = rawIds;
        _pendingAction = action ?? "select";
        _pendingColors = null;
        _pendingViewName = null;
        _done.Reset();
        _result = null;
    }

    public void PrepareColor(IList<PbiSelectHttpListener.ColorOverride> items)
    {
        _pendingKind = Kind.Color;
        _pendingColors = items;
        _pendingIds = null;
        _pendingAction = "color";
        _pendingViewName = null;
        _done.Reset();
        _result = null;
    }

    public void PrepareReset()
    {
        _pendingKind = Kind.ResetOverrides;
        _pendingIds = null;
        _pendingColors = null;
        _pendingAction = "reset";
        _pendingViewName = null;
        _done.Reset();
        _result = null;
    }

    public void PrepareCreateView(IList<long> rawIds, string? viewName)
    {
        _pendingKind = Kind.CreateView;
        _pendingIds = rawIds;
        _pendingViewName = viewName;
        _pendingColors = null;
        _pendingAction = "createView";
        _done.Reset();
        _result = null;
    }

    /// <summary>Waits for Execute to finish. Returns true if completed within timeout.</summary>
    public bool WaitForCompletion() => _done.Wait(ExecuteTimeout);

    public void Execute(UIApplication app)
    {
        try
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                System.Diagnostics.Trace.WriteLine("[PbiActionEventHandler] doc null on UI thread.");
                _result = null;
                return;
            }

            switch (_pendingKind)
            {
                case Kind.Select:
                    _result = ExecuteSelect(doc);
                    break;
                case Kind.Color:
                    _result = ExecuteColor(doc);
                    break;
                case Kind.ResetOverrides:
                    _result = ExecuteResetOverrides(doc);
                    break;
                case Kind.CreateView:
                    _result = ExecuteCreateView(doc, app!);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[PbiActionEventHandler] Execute error: {ex.Message}");
            _result = null;
        }
        finally
        {
            _done.Set();
        }
    }

    // ─── Action implementations (UI thread) ────────────────────────────────

    private string? ExecuteSelect(Document doc)
    {
        var rawIds = _pendingIds;
        var action = _pendingAction;
        if (rawIds == null || rawIds.Count == 0) return "0";

        var validIds = ResolveIds(doc, rawIds);
        if (validIds.Count == 0) return "0";

        var uiDoc = new UIDocument(doc);
        uiDoc.Selection.SetElementIds(validIds);

        if (action == "isolate")
        {
            try
            {
                doc.ActiveView.IsolateElementsTemporary(validIds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiActionEventHandler] IsolateElementsTemporary failed (non-fatal): {ex.Message}");
            }
        }
        return validIds.Count.ToString();
    }

    private string? ExecuteColor(Document doc)
    {
        var items = _pendingColors;
        if (items == null || items.Count == 0) return "0";

        // Map id → ElementId pairs + parsed colors
        var resolved = new List<(ElementId Eid, Color Color)>(items.Count);
        foreach (var it in items)
        {
#if REVIT2024_OR_GREATER
            var eid = new ElementId(it.Id);
#else
            var eid = new ElementId((int)it.Id);
#endif
            if (doc.GetElement(eid) == null) continue;
            if (!TryParseHex(it.Hex, out var color)) continue;
            resolved.Add((eid, color));
        }
        if (resolved.Count == 0) return "0";

        // Find a solid fill pattern once (reused for all overrides)
        var solidFillId = GetSolidFillPatternId(doc);

        using var tx = new Transaction(doc, "PBI color override");
        tx.Start();
        var view = doc.ActiveView;
        foreach (var (eid, color) in resolved)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetCutForegroundPatternColor(color);
                ogs.SetCutForegroundPatternId(solidFillId);
                ogs.SetCutForegroundPatternVisible(true);
            }
            try { view.SetElementOverrides(eid, ogs); } catch { /* skip per-element failures */ }
        }
        tx.Commit();
        return resolved.Count.ToString();
    }

    private string? ExecuteResetOverrides(Document doc)
    {
        var view = doc.ActiveView;

        // Collect all elements that currently have any override applied in this view.
        // (Iterating "all visible elements" is the correct surface — Revit doesn't
        // give us a "list of overridden elements" API, but resetting an element
        // with no override is a no-op.)
        var visible = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElementIds();
        if (visible == null || visible.Count == 0) return "0";

        using var tx = new Transaction(doc, "PBI reset overrides");
        tx.Start();
        var empty = new OverrideGraphicSettings();
        foreach (var id in visible)
        {
            try { view.SetElementOverrides(id, empty); } catch { /* skip */ }
        }
        tx.Commit();
        return visible.Count.ToString();
    }

    private string? ExecuteCreateView(Document doc, UIApplication app)
    {
        var rawIds = _pendingIds;
        if (rawIds == null || rawIds.Count == 0) return null;

        var validIds = ResolveIds(doc, rawIds);
        if (validIds.Count == 0) return "0";

        // Find 3D view type
        var view3DType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

        if (view3DType == null)
        {
            System.Diagnostics.Trace.WriteLine("[PbiActionEventHandler] No 3D ViewFamilyType found.");
            return null;
        }

        // Compute bounding box for section box
        BoundingBoxXYZ? bbox = ComputeBoundingBox(doc, validIds);

        View3D? newView;
        using (var tx = new Transaction(doc, "PBI create view"))
        {
            tx.Start();
            newView = View3D.CreateIsometric(doc, view3DType.Id);
            newView.Name = ProposeViewName(doc, _pendingViewName);
            if (bbox != null)
            {
                newView.IsSectionBoxActive = true;
                newView.SetSectionBox(bbox);
            }
            // Isolate the elements so the new view shows only them
            try { newView.IsolateElementsTemporary(validIds); } catch { /* non-fatal */ }
            tx.Commit();
        }

        // NOTE: intentionally do NOT switch to the new view. The user keeps
        // their current view; the new 3D view appears in the Project Browser
        // under "3D Views" and can be opened manually when needed.
        return newView.Name;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static IList<ElementId> ResolveIds(Document doc, IList<long> rawIds)
    {
        var valid = new List<ElementId>(rawIds.Count);
        foreach (var v in rawIds)
        {
#if REVIT2024_OR_GREATER
            var eid = new ElementId(v);
#else
            var eid = new ElementId((int)v);
#endif
            if (doc.GetElement(eid) != null)
                valid.Add(eid);
        }
        return valid;
    }

    private static ElementId GetSolidFillPatternId(Document doc)
    {
        var solid = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp =>
                fp.GetFillPattern().IsSolidFill &&
                fp.GetFillPattern().Target == FillPatternTarget.Drafting);
        return solid?.Id ?? ElementId.InvalidElementId;
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = new Color(0, 0, 0);
        if (string.IsNullOrEmpty(hex)) return false;
        var s = hex.TrimStart('#');
        if (s.Length == 8) s = s.Substring(0, 6); // strip alpha if present
        if (s.Length != 6) return false;
        try
        {
            var r = Convert.ToByte(s.Substring(0, 2), 16);
            var g = Convert.ToByte(s.Substring(2, 2), 16);
            var b = Convert.ToByte(s.Substring(4, 2), 16);
            color = new Color(r, g, b);
            return true;
        }
        catch { return false; }
    }

    private static BoundingBoxXYZ? ComputeBoundingBox(Document doc, IList<ElementId> ids)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool any = false;
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            var bb = el?.get_BoundingBox(null);
            if (bb == null) continue;
            any = true;
            if (bb.Min.X < minX) minX = bb.Min.X;
            if (bb.Min.Y < minY) minY = bb.Min.Y;
            if (bb.Min.Z < minZ) minZ = bb.Min.Z;
            if (bb.Max.X > maxX) maxX = bb.Max.X;
            if (bb.Max.Y > maxY) maxY = bb.Max.Y;
            if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
        }
        if (!any) return null;

        // Inflate by ~1 ft on each side for context
        const double pad = 1.0;
        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX - pad, minY - pad, minZ - pad),
            Max = new XYZ(maxX + pad, maxY + pad, maxZ + pad),
        };
    }

    private static string ProposeViewName(Document doc, string? requested)
    {
        var baseName = string.IsNullOrWhiteSpace(requested)
            ? $"PBI Selection {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            : requested!;
        // Ensure uniqueness
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Select(v => v.Name)
            .ToHashSet();
        var name = baseName;
        var n = 1;
        while (existing.Contains(name))
            name = $"{baseName} ({++n})";
        return name;
    }

    public string GetName() => "RevitCortex PBI Actions";
}

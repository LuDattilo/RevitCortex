using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Revit ExternalEventHandler that handles all PBI Desktop visual actions on the
/// main thread: select, isolate, color override, reset overrides, create 3D view.
///
/// Queue-based dispatch (replaces the previous shared-mutable-state pattern that
/// had a documented race: a timed-out request could be overwritten by the next
/// one, causing the wrong action on the wrong elements once Revit unblocked).
///
/// Contract:
///   - Each Dispatch* call enqueues an immutable PbiActionRequest with its own
///     ManualResetEventSlim and Raises ExternalEvent.
///   - Execute(UIApplication) drains one request at a time. Every Execute call
///     dequeues exactly one request (or none, if all were already processed in
///     a previous Execute pass), runs it, fills the result, and signals it.
///   - Caller blocks on its own request's done event with a timeout.
///   - If a request times out on the caller side, its `Cancelled` flag is set;
///     Execute checks the flag at start and silently skips it. The next valid
///     request keeps queue order intact and does NOT carry the previous payload.
///   - A "starvation watchdog" Raise() is fired after enqueue so subsequent
///     requests don't sit waiting in case ExternalEvent coalesces.
/// </summary>
public class PbiActionEventHandler : IExternalEventHandler
{
    public enum Kind
    {
        Select,
        Color,
        ResetOverrides,
        CreateView,
    }

    /// <summary>
    /// Immutable request carrying its own result slot + completion event so that
    /// concurrent dispatches never share mutable pending state.
    /// </summary>
    public sealed class Request
    {
        public Kind Kind { get; }
        public IReadOnlyList<long> RawIds { get; }
        public string Action { get; }
        public IReadOnlyList<PbiSelectHttpListener.ColorOverride> Colors { get; }
        public string? ViewName { get; }

        /// <summary>
        /// Optional UniqueId strings (PBI dataset "UniqueId" column). When provided
        /// they are resolved first (stable across purge / family reload); raw
        /// ElementIds are used as a fallback.
        /// </summary>
        public IReadOnlyList<string>? RawUniqueIds { get; }

        /// <summary>
        /// Optional Revit document title the visual believes is active. If set
        /// and doesn't match <c>doc.Title</c> the action is aborted with
        /// <c>Error = "wrong_document:&lt;expected&gt;|&lt;actual&gt;"</c>.
        /// </summary>
        public string? ExpectedDocumentTitle { get; }

        public ManualResetEventSlim Done { get; } = new(initialState: false);
        // Non-volatile fields are safe to read after Done is set (release fence).
        public string? Result;
        /// <summary>
        /// Structured error string. Format: <c>"&lt;code&gt;:&lt;message&gt;"</c>. When set,
        /// the listener returns it in <c>error</c> and ignores <c>Result</c>.
        /// </summary>
        public string? Error;
        public volatile bool Cancelled;

        public Request(Kind kind,
            IReadOnlyList<long>? rawIds,
            string action,
            IReadOnlyList<PbiSelectHttpListener.ColorOverride>? colors,
            string? viewName,
            IReadOnlyList<string>? rawUniqueIds = null,
            string? expectedDocumentTitle = null)
        {
            Kind = kind;
            RawIds = rawIds ?? Array.Empty<long>();
            Action = action ?? "";
            Colors = colors ?? Array.Empty<PbiSelectHttpListener.ColorOverride>();
            ViewName = viewName;
            RawUniqueIds = rawUniqueIds;
            ExpectedDocumentTitle = expectedDocumentTitle;
        }
    }

    private readonly ConcurrentQueue<Request> _queue = new();
    // Reference to the external event for self-Raise after enqueue. Set once at
    // wiring time by RevitCortexApp. The handler tolerates a null event (no
    // self-raise) for tests that drive Execute() manually.
    private ExternalEvent? _event;

    /// <summary>How long the listener waits for Execute to complete before timing out.</summary>
    public TimeSpan ExecuteTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Per-view registry of elements that PBI has painted. Used to scope
    /// Reset overrides to PBI-touched elements only.
    /// </summary>
    private readonly PbiOverrideRegistry _registry = new();

    /// <summary>
    /// Binds the ExternalEvent that owns this handler so the queue can self-raise
    /// when enqueuing while a previous Execute is still draining. Call once after
    /// `ExternalEvent.Create(handler)`. Tests can leave it null and call Execute
    /// directly.
    /// </summary>
    public void BindExternalEvent(ExternalEvent evt) => _event = evt;

    /// <summary>
    /// Enqueues a request, raises the ExternalEvent, and waits on the request's
    /// own completion event. Each caller has its own Done — no cross-request
    /// state corruption is possible. Returns the result string or null on
    /// timeout/cancellation/Execute failure.
    /// </summary>
    public string? Dispatch(Request request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        _queue.Enqueue(request);
        try { _event?.Raise(); } catch { /* event disposed */ }

        if (!request.Done.Wait(ExecuteTimeout))
        {
            request.Cancelled = true; // Tell future Execute pass to skip this one.
            return null;
        }
        return request.Result;
    }

    // ─── Convenience wrappers preserving the previous Dispatcher API ───────

    public string? DispatchSelection(IList<long> rawIds, string action) =>
        Dispatch(new Request(Kind.Select, rawIds?.ToArray(), action ?? "select", null, null));

    public string? DispatchColor(IList<PbiSelectHttpListener.ColorOverride> items) =>
        Dispatch(new Request(Kind.Color, null, "color", items?.ToArray(), null));

    public string? DispatchReset() =>
        Dispatch(new Request(Kind.ResetOverrides, null, "reset", null, null));

    public string? DispatchCreateView(IList<long> rawIds, string? viewName) =>
        Dispatch(new Request(Kind.CreateView, rawIds?.ToArray(), "createView", null, viewName));

    // ─── v1.0.0.10 rich-dispatch overloads ──────────────────────────────────
    // Backward-compatible: when the visual sends UniqueIds and/or a document
    // title for validation, callers route through these methods. The legacy
    // overloads above remain for tests and AI clients that don't need either.

    /// <summary>
    /// Selection with optional UniqueIds (preferred over ElementIds) and an
    /// optional expected document title for sanity check. Returns one of:
    ///   - resolved-count string ("118") on success
    ///   - null if the doc check failed or the action couldn't run; in that
    ///     case caller should consult <see cref="Request.Error"/> via the
    ///     direct <see cref="Dispatch"/> overload.
    /// </summary>
    public Request DispatchSelectionRich(
        IList<long> rawIds, IList<string>? uniqueIds, string action, string? expectedDocumentTitle)
    {
        var req = new Request(
            Kind.Select,
            rawIds?.ToArray(),
            action ?? "select",
            null,
            null,
            uniqueIds?.ToArray(),
            expectedDocumentTitle);
        Dispatch(req);
        return req;
    }

    public Request DispatchCreateViewRich(
        IList<long> rawIds, IList<string>? uniqueIds, string? viewName, string? expectedDocumentTitle)
    {
        var req = new Request(
            Kind.CreateView,
            rawIds?.ToArray(),
            "createView",
            null,
            viewName,
            uniqueIds?.ToArray(),
            expectedDocumentTitle);
        Dispatch(req);
        return req;
    }

    public void Execute(UIApplication app)
    {
        // Drain in a loop — a single Raise() can correspond to multiple enqueued
        // requests if Revit coalesced them. Each iteration handles exactly one.
        while (_queue.TryDequeue(out var req))
        {
            if (req.Cancelled)
            {
                // Caller already gave up; don't run the action but DO complete
                // the event so any straggler observers unblock.
                try { req.Done.Set(); } catch { }
                continue;
            }

            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    System.Diagnostics.Trace.WriteLine("[PbiActionEventHandler] doc null on UI thread.");
                    req.Error = "no_document:No active Revit document.";
                    req.Result = null;
                }
                else if (!ValidateDocumentTitle(doc, req))
                {
                    // Title mismatch — visual sent uniqueIds from a different model.
                    // Don't touch the model; surface a structured error.
                    req.Result = null;
                }
                else
                {
                    req.Result = req.Kind switch
                    {
                        Kind.Select         => ExecuteSelect(doc, req),
                        Kind.Color          => ExecuteColor(doc, req),
                        Kind.ResetOverrides => ExecuteResetOverrides(doc),
                        Kind.CreateView     => ExecuteCreateView(doc, app!, req),
                        _                   => null
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[PbiActionEventHandler] Execute error: {ex.Message}");
                req.Result = null;
            }
            finally
            {
                req.Done.Set();
            }
        }
    }

    // ─── Action implementations (UI thread) ────────────────────────────────

    private string? ExecuteSelect(Document doc, Request req)
    {
        var rawIds = req.RawIds;
        var action = req.Action;
        bool hasAnyIds = (rawIds != null && rawIds.Count > 0)
                      || (req.RawUniqueIds != null && req.RawUniqueIds.Count > 0);
        if (!hasAnyIds) return "0";

        // Prefer the merged resolver (UniqueId then ElementId) when the visual
        // sent both. Fall back to legacy ElementId-only path for old payloads.
        var validIds = req.RawUniqueIds != null
            ? ResolveIdsMerged(doc, req)
            : ResolveIds(doc, rawIds!);
        if (validIds.Count == 0) return "0";

        var uiDoc = new UIDocument(doc);
        uiDoc.Selection.SetElementIds(validIds);

        if (action == "isolate")
        {
            try
            {
                using var tx = new Transaction(doc, "PBI isolate");
                tx.Start();
                doc.ActiveView.IsolateElementsTemporary(validIds);
                tx.Commit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiActionEventHandler] IsolateElementsTemporary failed (non-fatal): {ex.Message}");
            }
        }
        return validIds.Count.ToString();
    }

    private string? ExecuteColor(Document doc, Request req)
    {
        var items = req.Colors;
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
        var painted = new List<ElementId>(resolved.Count);
        foreach (var (eid, color) in resolved)
        {
            // Pattern fill only — projection/cut line colors are left untouched so
            // the model's own edges stay legible (otherwise the colored outlines
            // overwhelm the geometry on busy views).
            var ogs = new OverrideGraphicSettings();
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetCutForegroundPatternColor(color);
                ogs.SetCutForegroundPatternId(solidFillId);
                ogs.SetCutForegroundPatternVisible(true);
            }
            try
            {
                view.SetElementOverrides(eid, ogs);
                painted.Add(eid);
            }
            catch { /* skip per-element failures */ }
        }
        // Record in the registry so Reset overrides can target only PBI-painted ids
        // later. Tracking happens inside the same tx as the override itself.
        if (painted.Count > 0)
            _registry.Track(doc, view.Id, painted);
        tx.Commit();
        return resolved.Count.ToString();
    }

    private string? ExecuteResetOverrides(Document doc)
    {
        var view = doc.ActiveView;
        int overridesCleared = 0;
        bool isolationCleared = false;

        // First: disable any temporary hide/isolate that may have been applied
        // by the "Isola" action. DisableTemporaryViewMode throws if no temp mode
        // is active, so we swallow the exception — that's the expected case
        // when the user clicks Reset on a non-isolated view.
        try
        {
            using var txIso = new Transaction(doc, "PBI reset temporary isolation");
            txIso.Start();
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            txIso.Commit();
            isolationCleared = true;
        }
        catch (Exception ex)
        {
            // No temporary isolation active — completely normal, not an error.
            System.Diagnostics.Trace.WriteLine(
                $"[PbiActionEventHandler] DisableTemporaryViewMode skipped: {ex.Message}");
        }

        // Then: clear graphic overrides on PBI-tracked elements only. Manual
        // overrides created by the user or other add-ins are intentionally left
        // untouched — a previous version that iterated all visible elements
        // could destroy coordination/authoring work and was a footgun.
        var trackedIds = _registry.GetTracked(doc, view.Id);
        if (trackedIds.Count > 0)
        {
            using var tx = new Transaction(doc, "PBI reset overrides");
            tx.Start();
            var empty = new OverrideGraphicSettings();
            foreach (var id in trackedIds)
            {
                try { view.SetElementOverrides(id, empty); overridesCleared++; }
                catch { /* skip */ }
            }
            _registry.Clear(doc, view.Id);
            tx.Commit();
        }

        // Return a single integer for backward-compat with the visual, but log
        // both counts for diagnostics. Visual will display "Reset vista".
        System.Diagnostics.Trace.WriteLine(
            $"[PbiActionEventHandler] Reset: overrides={overridesCleared}, isolation={isolationCleared}");
        return overridesCleared.ToString();
    }

    private string? ExecuteCreateView(Document doc, UIApplication app, Request req)
    {
        var rawIds = req.RawIds;
        bool hasAnyIds = (rawIds != null && rawIds.Count > 0)
                      || (req.RawUniqueIds != null && req.RawUniqueIds.Count > 0);
        if (!hasAnyIds) return null;

        var validIds = req.RawUniqueIds != null
            ? ResolveIdsMerged(doc, req)
            : ResolveIds(doc, rawIds!);
        if (validIds.Count == 0) return "0";

        var view3DType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

        if (view3DType == null)
        {
            System.Diagnostics.Trace.WriteLine("[PbiActionEventHandler] No 3D ViewFamilyType found.");
            return null;
        }

        BoundingBoxXYZ? bbox = ComputeBoundingBox(doc, validIds);

        View3D? newView;
        try
        {
            using var tx = new Transaction(doc, "PBI create view");
            tx.Start();
            newView = View3D.CreateIsometric(doc, view3DType.Id);
            newView.Name = ProposeViewName(doc, req.ViewName);
            if (bbox != null)
            {
                newView.IsSectionBoxActive = true;
                newView.SetSectionBox(bbox);
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiActionEventHandler] CreateView failed: {ex.Message}");
            return null;
        }

        // Isolate the elements in the new view. Must be a separate transaction
        // because IsolateElementsTemporary uses a temporary-view-mode transaction
        // group internally and can conflict with the just-committed creation tx.
        try
        {
            using var txIso = new Transaction(doc, "PBI create view isolate");
            txIso.Start();
            newView.IsolateElementsTemporary(validIds);
            txIso.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiActionEventHandler] CreateView isolate failed (non-fatal): {ex.Message}");
        }

        // Activate the new view so the user lands directly on the selection.
        // The previous version intentionally kept the current view to avoid
        // disrupting authoring/markup, but feedback was that "create view"
        // without "show view" is confusing — the user has to dig into Project
        // Browser to find what they just created. Switching here is the
        // expected behaviour for the "from selection" naming. Selection of the
        // elements is also restored so the user can immediately keep working
        // with them.
        try
        {
            var uiDoc = new UIDocument(doc);
            uiDoc.ActiveView = newView;
            uiDoc.Selection.SetElementIds(validIds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiActionEventHandler] CreateView activate failed (non-fatal): {ex.Message}");
        }

        return newView.Name;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static IList<ElementId> ResolveIds(Document doc, IReadOnlyList<long> rawIds)
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

    /// <summary>
    /// Merged resolver used by rich-dispatch paths. Prefers UniqueIds when
    /// the visual provides them (stable across purge / family reload) and
    /// falls back to ElementIds for ids the UniqueId lookup couldn't resolve.
    /// Deduplicates by ElementId. Caller's RawIds + RawUniqueIds are treated
    /// as parallel arrays of the same set, NOT as additive — the visual is
    /// expected to send the UniqueId alongside the matching ElementId.
    /// </summary>
    private static IList<ElementId> ResolveIdsMerged(Document doc, Request req)
    {
        var seen = new HashSet<long>();
        var valid = new List<ElementId>();

        // Pass 1: UniqueIds — preferred when available because they survive
        // purge/family-reload while ElementIds get reassigned.
        if (req.RawUniqueIds != null)
        {
            foreach (var uid in req.RawUniqueIds)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                Element? elem = null;
                try { elem = doc.GetElement(uid); } catch { /* malformed uid */ }
                if (elem == null) continue;
                long key = GetEidValue(elem.Id);
                if (seen.Add(key)) valid.Add(elem.Id);
            }
        }

        // Pass 2: ElementIds — adds anything the UniqueId pass missed.
        foreach (var v in req.RawIds)
        {
#if REVIT2024_OR_GREATER
            var eid = new ElementId(v);
#else
            var eid = new ElementId((int)v);
#endif
            if (!seen.Add(v)) continue;
            if (doc.GetElement(eid) != null)
                valid.Add(eid);
            else
                seen.Remove(v); // didn't actually add it
        }
        return valid;
    }

    private static long GetEidValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>
    /// Optional document-title sanity check. The visual maps an optional
    /// "Document title" data role; when it sends one and it doesn't match
    /// the active doc, we refuse the action and surface a structured error.
    /// </summary>
    private static bool ValidateDocumentTitle(Document doc, Request req)
    {
        if (string.IsNullOrEmpty(req.ExpectedDocumentTitle)) return true;
        var actual = doc.Title ?? "";
        var expected = req.ExpectedDocumentTitle!;
        // Tolerate the ".rvt" suffix being present on one side and not the other.
        var actualBase = actual.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
            ? actual.Substring(0, actual.Length - 4) : actual;
        var expectedBase = expected.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
            ? expected.Substring(0, expected.Length - 4) : expected;
        if (string.Equals(actualBase, expectedBase, StringComparison.OrdinalIgnoreCase))
            return true;
        req.Error = $"wrong_document:expected '{expected}' but active document is '{actual}'";
        return false;
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
        // Revit rejects ':', '\\', '/', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'
        // in view names. Default format uses '-' separators to stay safe; user-provided
        // names are sanitised to keep this contract.
        var baseName = string.IsNullOrWhiteSpace(requested)
            ? $"PBI Selection {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : SanitizeViewName(requested!);

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

    private static string SanitizeViewName(string raw)
    {
        var invalid = new[] { ':', '\\', '/', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        var cleaned = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(cleaned)
            ? $"PBI Selection {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : cleaned;
    }

    public string GetName() => "RevitCortex PBI Actions";
}

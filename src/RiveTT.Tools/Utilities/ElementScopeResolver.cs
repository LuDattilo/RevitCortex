using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;

namespace RiveTT.Tools.Utilities;

public static class ElementScopeResolver
{
    private const string StoreKey = "temporarySelectionSnapshots";

    public static string Capture(CortexSession session, IEnumerable<long> elementIds,
        TimeSpan ttl, out DateTime expiresAtUtc)
    {
        var store = GetStore(session);
        PurgeExpired(store);
        var token = Guid.NewGuid().ToString("N");
        expiresAtUtc = DateTime.UtcNow.Add(ttl);
        store.Items[token] = new SelectionSnapshot(
            elementIds.Where(id => id > 0).Distinct().ToArray(), expiresAtUtc);
        return token;
    }

    public static IReadOnlyList<Element> Resolve(
        Document doc, JObject input, CortexSession session,
        out string resolvedScope, out CortexResult<object>? error,
        string defaultScope = "whole_model")
    {
        error = null;
        var explicitIds = input["elementIds"]?.ToObject<List<long>>();
        if (explicitIds is { Count: > 0 })
        {
            resolvedScope = "elementIds";
            return ResolveIds(doc, explicitIds);
        }

        var selectionToken = input["selectionToken"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(selectionToken))
        {
            var store = GetStore(session);
            PurgeExpired(store);
            if (!store.Items.TryGetValue(selectionToken!, out var snapshot))
            {
                resolvedScope = "selectionToken";
                error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Selection token was not found or has expired",
                    suggestion: "Capture a new selection with capture_selection and retry.");
                return Array.Empty<Element>();
            }
            resolvedScope = "selectionToken";
            return ResolveIds(doc, snapshot.ElementIds);
        }

        var savedSelectionName = input["savedSelectionName"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(savedSelectionName))
        {
            var filter = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(sf => sf.Name.Equals(savedSelectionName,
                    StringComparison.OrdinalIgnoreCase));
            if (filter == null)
            {
                resolvedScope = "savedSelection";
                error = CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Saved selection '{savedSelectionName}' was not found");
                return Array.Empty<Element>();
            }
            resolvedScope = "savedSelection";
            return filter.GetElementIds()
                .Select(doc.GetElement).Where(e => e != null).Cast<Element>().ToList();
        }

        var scope = (input["scope"]?.Value<string>() ?? defaultScope).ToLowerInvariant();
        resolvedScope = scope;
        switch (scope)
        {
            case "selection":
            {
                var ids = new UIDocument(doc).Selection.GetElementIds();
                if (ids.Count == 0)
                {
                    error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "The current Revit selection is empty",
                        suggestion: "Pass elementIds, a selectionToken, or select elements in Revit.");
                    return Array.Empty<Element>();
                }
                return ids.Select(doc.GetElement).Where(e => e != null).Cast<Element>().ToList();
            }
            case "last_filter":
            {
                var ids = session.Store.Get<long[]>("lastFilterResults") ?? Array.Empty<long>();
                if (ids.Length == 0)
                {
                    error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "No previous filter result exists in this document session");
                    return Array.Empty<Element>();
                }
                return ResolveIds(doc, ids);
            }
            case "active_view":
                if (doc.ActiveView == null)
                {
                    error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "There is no active Revit view");
                    return Array.Empty<Element>();
                }
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType().ToElements().ToList();
            case "whole_model":
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType().ToElements().ToList();
            default:
                error = CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown scope '{scope}'",
                    suggestion: "Use whole_model, active_view, selection, last_filter, elementIds, selectionToken, or savedSelectionName.");
                return Array.Empty<Element>();
        }
    }

    private static IReadOnlyList<Element> ResolveIds(Document doc, IEnumerable<long> ids)
        => ids.Select(id => doc.GetElement(ToolHelpers.ToElementId(id)))
            .Where(e => e != null).Cast<Element>().ToList();

    private static SelectionSnapshotStore GetStore(CortexSession session)
    {
        var store = session.Store.Get<SelectionSnapshotStore>(StoreKey);
        if (store != null) return store;
        store = new SelectionSnapshotStore();
        session.Store.Set(StoreKey, store);
        return store;
    }

    private static void PurgeExpired(SelectionSnapshotStore store)
    {
        var now = DateTime.UtcNow;
        foreach (var token in store.Items.Where(p => p.Value.ExpiresAtUtc <= now)
                     .Select(p => p.Key).ToArray())
            store.Items.Remove(token);
    }

    private sealed class SelectionSnapshotStore
    {
        public Dictionary<string, SelectionSnapshot> Items { get; } = new();
    }

    private sealed record SelectionSnapshot(long[] ElementIds, DateTime ExpiresAtUtc);
}

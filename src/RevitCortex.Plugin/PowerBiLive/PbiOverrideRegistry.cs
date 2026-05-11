using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Tracks which elements were override-painted by Power BI per view, so that
/// "Reset overrides" can clear only PBI-touched elements without destroying
/// manual or third-party overrides on the same view.
///
/// Persistence: snapshot stored as a string on a DataStorage entity in the
/// document via ExtensibleStorage. Format is compact CSV grouped per view:
///   "viewId=12345;ids=1,2,3|viewId=67890;ids=4,5"
///
/// In-memory cache is rebuilt lazily from persisted state on first access
/// for a given document, so the registry survives Revit restarts.
///
/// Thread contract: callers MUST be on the Revit main thread (transaction
/// scope required for any persisted write). All public methods assume that.
/// </summary>
public sealed class PbiOverrideRegistry
{
    private static readonly Guid SchemaGuid = new Guid("4E7C7E60-8B26-4A1E-B7C9-21B0B2A9D8E2");
    private const string SchemaName = "RevitCortex.PbiOverrideRegistry";
    private const string FieldName  = "Snapshot";
    private const string StorageName = "RevitCortex.PbiOverrides";

    // In-memory cache: docHash → viewId → set of element ids.
    // The doc dimension lets multi-document sessions stay isolated.
    private readonly Dictionary<string, Dictionary<long, HashSet<long>>> _cache = new();

    /// <summary>
    /// Records that the given elements have been painted by PBI on the given view.
    /// Caller MUST run inside a Transaction so the persisted snapshot lands atomically
    /// with the override changes themselves.
    /// </summary>
    public void Track(Document doc, ElementId viewId, IEnumerable<ElementId> elementIds)
    {
        if (doc == null || viewId == null || viewId == ElementId.InvalidElementId) return;
        var docKey = GetDocKey(doc);
        var perDoc = LoadOrInit(doc);
        var viewKey = GetIdValue(viewId);
        if (!perDoc.TryGetValue(viewKey, out var set))
        {
            set = new HashSet<long>();
            perDoc[viewKey] = set;
        }
        foreach (var eid in elementIds)
        {
            if (eid == null || eid == ElementId.InvalidElementId) continue;
            set.Add(GetIdValue(eid));
        }
        Persist(doc, perDoc);
    }

    /// <summary>
    /// Returns the elements PBI has painted on the given view, resolved to live
    /// ElementIds. Ids that no longer exist in the document are filtered out and
    /// also dropped from the registry so the snapshot stays clean.
    /// Caller MUST run inside a Transaction if it intends to call Clear afterward.
    /// </summary>
    public IList<ElementId> GetTracked(Document doc, ElementId viewId)
    {
        var result = new List<ElementId>();
        if (doc == null || viewId == null || viewId == ElementId.InvalidElementId) return result;

        var perDoc = LoadOrInit(doc);
        var viewKey = GetIdValue(viewId);
        if (!perDoc.TryGetValue(viewKey, out var set) || set.Count == 0) return result;

        var stillAlive = new HashSet<long>();
        foreach (var raw in set)
        {
#if REVIT2024_OR_GREATER
            var eid = new ElementId(raw);
#else
            var eid = new ElementId((int)raw);
#endif
            if (doc.GetElement(eid) != null)
            {
                result.Add(eid);
                stillAlive.Add(raw);
            }
        }

        if (stillAlive.Count != set.Count)
        {
            perDoc[viewKey] = stillAlive;
            Persist(doc, perDoc);
        }

        return result;
    }

    /// <summary>
    /// Clears all tracked elements for a view. Used by reset-overrides after the
    /// view overrides themselves have been wiped.
    /// </summary>
    public void Clear(Document doc, ElementId viewId)
    {
        if (doc == null || viewId == null || viewId == ElementId.InvalidElementId) return;
        var perDoc = LoadOrInit(doc);
        if (perDoc.Remove(GetIdValue(viewId)))
            Persist(doc, perDoc);
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private Dictionary<long, HashSet<long>> LoadOrInit(Document doc)
    {
        var key = GetDocKey(doc);
        if (_cache.TryGetValue(key, out var existing)) return existing;

        var loaded = LoadFromStorage(doc);
        _cache[key] = loaded;
        return loaded;
    }

    private static Dictionary<long, HashSet<long>> LoadFromStorage(Document doc)
    {
        var result = new Dictionary<long, HashSet<long>>();
        try
        {
            var storage = FindStorage(doc);
            if (storage == null) return result;

            var schema = GetOrCreateSchema();
            var entity = storage.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return result;

            var snapshot = entity.Get<string>(FieldName);
            if (string.IsNullOrEmpty(snapshot)) return result;

            foreach (var group in snapshot.Split('|'))
            {
                // Format: "viewId=N;ids=a,b,c"
                if (string.IsNullOrEmpty(group)) continue;
                long viewId = 0;
                HashSet<long>? ids = null;
                foreach (var token in group.Split(';'))
                {
                    if (token.StartsWith("viewId="))
                    {
                        long.TryParse(token.Substring("viewId=".Length),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out viewId);
                    }
                    else if (token.StartsWith("ids="))
                    {
                        ids = new HashSet<long>();
                        foreach (var idStr in token.Substring("ids=".Length).Split(','))
                        {
                            if (long.TryParse(idStr, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var parsed))
                                ids.Add(parsed);
                        }
                    }
                }
                if (viewId != 0 && ids != null && ids.Count > 0)
                    result[viewId] = ids;
            }
        }
        catch
        {
            // Storage may be from an older/incompatible schema; start fresh.
        }
        return result;
    }

    private static void Persist(Document doc, Dictionary<long, HashSet<long>> perDoc)
    {
        try
        {
            var snapshot = string.Join("|", perDoc.Select(kv =>
                $"viewId={kv.Key.ToString(CultureInfo.InvariantCulture)};" +
                $"ids={string.Join(",", kv.Value.Select(v => v.ToString(CultureInfo.InvariantCulture)))}"));

            var schema = GetOrCreateSchema();
            var storage = FindStorage(doc) ?? CreateStorage(doc);
            if (storage == null) return;

            var entity = new Entity(schema);
            entity.Set(FieldName, snapshot);
            storage.SetEntity(entity);
        }
        catch
        {
            // Best-effort; persistence failure should never break the user action.
        }
    }

    private static Schema GetOrCreateSchema()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing != null) return existing;

        var builder = new SchemaBuilder(SchemaGuid)
            .SetSchemaName(SchemaName)
            .SetReadAccessLevel(AccessLevel.Public)
            .SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldName, typeof(string));
        return builder.Finish();
    }

    private static DataStorage? FindStorage(Document doc)
    {
        try
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(d => d.Name == StorageName);
        }
        catch { return null; }
    }

    private static DataStorage? CreateStorage(Document doc)
    {
        try
        {
            var ds = DataStorage.Create(doc);
            ds.Name = StorageName;
            return ds;
        }
        catch
        {
            return null;
        }
    }

    private static string GetDocKey(Document doc)
    {
        try { return doc.PathName ?? doc.Title ?? ""; }
        catch { return ""; }
    }

    private static long GetIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

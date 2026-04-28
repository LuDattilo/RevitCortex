using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCortex.Core.Interop;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// Resolves a <see cref="CortexElementRef"/> against the active host
    /// document and its loaded RevitLinkInstance set. Uses sourceFile
    /// basename matching (case-insensitive) and a UniqueId/IfcGuid/ElementId
    /// fallback cascade.
    /// </summary>
    public class HostLinkResolver
    {
        private readonly Document _hostDoc;
        private readonly Dictionary<string, RevitLinkInstance?> _byBasename;

        private HostLinkResolver(Document hostDoc,
            Dictionary<string, RevitLinkInstance?> byBasename)
        {
            _hostDoc = hostDoc;
            _byBasename = byBasename;
        }

        public static string NormalizeBasename(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            try { return Path.GetFileName(value)!.ToLowerInvariant(); }
            catch { return value!.ToLowerInvariant(); }
        }

        public static HostLinkResolver Build(Document hostDoc)
        {
            var map = new Dictionary<string, RevitLinkInstance?>(StringComparer.Ordinal);
            var hostKey = NormalizeBasename(hostDoc.PathName);
            if (!string.IsNullOrEmpty(hostKey)) map[hostKey] = null;
            // Fallback host key: use the doc Title when PathName is empty
            // (e.g., unsaved documents, central models with stripped path).
            var titleKey = NormalizeBasename(hostDoc.Title);
            if (!string.IsNullOrEmpty(titleKey) && !map.ContainsKey(titleKey))
                map[titleKey] = null;

            var links = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                var key = NormalizeBasename(linkDoc?.PathName);
                if (string.IsNullOrEmpty(key))
                    key = NormalizeBasename(link.Name);
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                    map[key] = link;
            }
            return new HostLinkResolver(hostDoc, map);
        }

        public ResolveOutcome Resolve(CortexElementRef refToFind)
        {
            if (refToFind == null)
                return ResolveOutcome.NotFound("ref is null");
            var key = NormalizeBasename(refToFind.SourceFile);
            if (string.IsNullOrEmpty(key))
                return ResolveOutcome.NotFound("missing sourceFile");
            if (!_byBasename.TryGetValue(key, out var link))
                return ResolveOutcome.NotFound("source file not loaded as host or link");

            var doc = link != null ? link.GetLinkDocument() : _hostDoc;
            if (doc == null)
                return ResolveOutcome.NotFound("link document not loaded");

            var element = ResolveElement(doc, refToFind);
            if (element == null)
                return ResolveOutcome.NotFound(
                    "no element matches uniqueId/ifcGuid/elementId in source");

            return link == null
                ? ResolveOutcome.Host(element.Id)
                : ResolveOutcome.Linked(link.Id, element.Id);
        }

        private static Element? ResolveElement(Document doc, CortexElementRef r)
        {
            if (!string.IsNullOrWhiteSpace(r.RevitUniqueId))
            {
                try
                {
                    var byUid = doc.GetElement(r.RevitUniqueId);
                    if (byUid != null) return byUid;
                }
                catch { /* fall through */ }
            }

            if (!string.IsNullOrWhiteSpace(r.IfcGuid))
            {
                var byIfc = FindByIfcGuid(doc, r.IfcGuid!);
                if (byIfc != null) return byIfc;
            }

            if (!string.IsNullOrWhiteSpace(r.RevitElementId)
                && long.TryParse(r.RevitElementId, out var idValue))
            {
                try
                {
                    var elementId =
#if REVIT2024_OR_GREATER
                        new ElementId(idValue);
#else
                        new ElementId((int)idValue);
#endif
                    var byId = doc.GetElement(elementId);
                    if (byId != null) return byId;
                }
                catch { /* fall through */ }
            }
            return null;
        }

        private static Element? FindByIfcGuid(Document doc, string ifcGuid)
        {
            try
            {
                var bipParam = new ParameterValueProvider(
                    new ElementId(BuiltInParameter.IFC_GUID));
                var rule = new FilterStringRule(bipParam,
                    new FilterStringEquals(), ifcGuid);
                var filter = new ElementParameterFilter(rule);
                return new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .FirstElement();
            }
            catch
            {
                return null;
            }
        }
    }

    public class ResolveOutcome
    {
        public bool IsHost { get; }
        public bool IsLinked { get; }
        public ElementId? HostElementId { get; }
        public ElementId? LinkInstanceId { get; }
        public ElementId? LinkedElementId { get; }
        public string? NotFoundReason { get; }

        private ResolveOutcome(bool host, bool linked,
            ElementId? hostId, ElementId? linkId, ElementId? linkedId,
            string? reason)
        {
            IsHost = host; IsLinked = linked;
            HostElementId = hostId; LinkInstanceId = linkId; LinkedElementId = linkedId;
            NotFoundReason = reason;
        }
        public static ResolveOutcome Host(ElementId id)
            => new ResolveOutcome(true, false, id, null, null, null);
        public static ResolveOutcome Linked(ElementId linkInstanceId, ElementId linkedId)
            => new ResolveOutcome(false, true, null, linkInstanceId, linkedId, null);
        public static ResolveOutcome NotFound(string reason)
            => new ResolveOutcome(false, false, null, null, null, reason);
    }
}

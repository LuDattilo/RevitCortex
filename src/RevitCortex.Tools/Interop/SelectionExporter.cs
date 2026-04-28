using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Core.Interop;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// Reads the active UIDocument selection (host + linked) and emits
    /// a <see cref="CortexElementRef"/> per element. Pure helpers are
    /// public for unit testing without a live Revit document.
    /// </summary>
    public static class SelectionExporter
    {
        public class Output
        {
            public List<CortexElementRef> Refs { get; } = new List<CortexElementRef>();
            public List<object> Skipped { get; } = new List<object>();
        }

        public static Output Export(UIDocument uiDoc)
        {
            var output = new Output();
            if (uiDoc == null) return output;
            var hostDoc = uiDoc.Document;
            if (hostDoc == null) return output;

            IList<Reference> refs;
            try { refs = uiDoc.Selection.GetReferences(); }
            catch (Exception ex) when (!IsFatal(ex))
            {
                /* GetReferences failed — return empty output */
                _ = ex;
                return output;
            }

            var hostFile = HostLinkResolver.NormalizeBasename(hostDoc.PathName);
            if (string.IsNullOrEmpty(hostFile))
                hostFile = HostLinkResolver.NormalizeBasename(hostDoc.Title);

            foreach (var reference in refs)
            {
                try
                {
                    if (reference.LinkedElementId == ElementId.InvalidElementId)
                    {
                        var element = hostDoc.GetElement(reference.ElementId);
                        if (element == null)
                        {
                            output.Skipped.Add(new
                            {
                                reason = "host element not found",
                                elementId = GetIdValue(reference.ElementId)
                            });
                            continue;
                        }
                        output.Refs.Add(BuildRef(element, hostFile));
                    }
                    else
                    {
                        var linkInstance = hostDoc.GetElement(reference.ElementId)
                            as RevitLinkInstance;
                        var linkDoc = linkInstance?.GetLinkDocument();
                        var linkedElement = linkDoc?.GetElement(reference.LinkedElementId);
                        if (linkInstance == null || linkDoc == null || linkedElement == null)
                        {
                            output.Skipped.Add(new
                            {
                                reason = linkInstance == null
                                    ? "link instance not found"
                                    : linkDoc == null
                                        ? "link document not loaded"
                                        : "linked element not found",
                                elementId = GetIdValue(reference.LinkedElementId),
                                linkInstanceId = GetIdValue(reference.ElementId)
                            });
                            continue;
                        }
                        var linkFile = HostLinkResolver.NormalizeBasename(linkDoc.PathName);
                        if (string.IsNullOrEmpty(linkFile))
                            linkFile = HostLinkResolver.NormalizeBasename(linkInstance.Name);
                        output.Refs.Add(BuildRef(linkedElement, linkFile));
                    }
                }
                catch (Exception ex) when (!IsFatal(ex))
                {
                    output.Skipped.Add(new
                    {
                        reason = "per-ref exception: " + ex.Message,
                        elementId = GetIdValue(reference.ElementId)
                    });
                }
            }
            return output;
        }

        private static CortexElementRef BuildRef(Element element, string sourceFile)
        {
            var category = element.Category;
            long catId = 0;
            if (category != null)
            {
#if REVIT2024_OR_GREATER
                catId = category.Id.Value;
#else
                catId = (long)category.Id.IntegerValue;
#endif
            }
            var catCode = FormatCategory(catId, category?.Name);

            var ifcParam = element.LookupParameter("IFC GUID")?.AsString();
            long elementIdValue =
#if REVIT2024_OR_GREATER
                element.Id.Value;
#else
                (long)element.Id.IntegerValue;
#endif

            return new CortexElementRef
            {
                SourceApp = "Revit",
                SourceFile = sourceFile,
                RevitUniqueId = element.UniqueId,
                IfcGuid = string.IsNullOrEmpty(ifcParam) ? null : ifcParam,
                RevitElementId = elementIdValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Category = string.IsNullOrEmpty(catCode) ? null : catCode,
            };
        }

        /// <summary>
        /// Returns the OST_* enum name for built-in categories (negative ids),
        /// or falls back to <paramref name="displayName"/>. Returns empty string
        /// when neither is available. Public for unit testing without RevitAPI.dll
        /// being resolvable at the call site.
        /// </summary>
        public static string FormatCategory(long categoryIdValue, string? displayName)
        {
            // BuiltInCategory ids are negative. We resolve the OST_* name via
            // reflection so that this method does not statically reference the
            // BuiltInCategory enum — RevitAPI.dll is only loaded if the JIT
            // ever needs the type, which happens lazily inside ResolveOstName.
            if (categoryIdValue < 0)
            {
                var ostName = ResolveOstName(categoryIdValue);
                if (ostName != null) return ostName;
            }
            return displayName ?? "";
        }

        // RevitAPI.dll is not present in test hosts that don't open Revit, so
        // we resolve BuiltInCategory via Type.GetType only on demand and cache
        // the result. When the assembly cannot be loaded we degrade to the
        // displayName fallback — the runtime path inside Revit is unaffected.
        private static Type? _bicType;
        private static bool _bicTypeResolved;

        private static string? ResolveOstName(long categoryIdValue)
        {
            try
            {
                if (!_bicTypeResolved)
                {
                    _bicType = Type.GetType(
                        "Autodesk.Revit.DB.BuiltInCategory, RevitAPI",
                        throwOnError: false);
                    _bicTypeResolved = true;
                }
                if (_bicType == null) return null;

                var name = Enum.GetName(_bicType, categoryIdValue);
                return name != null && name.StartsWith("OST_", StringComparison.Ordinal)
                    ? name : null;
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                _ = ex;
                return null;
            }
        }

        private static long GetIdValue(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return 0;
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return (long)id.IntegerValue;
#endif
        }

        private static bool IsFatal(Exception ex)
            => ex is OutOfMemoryException
            || ex is StackOverflowException
            || ex is AccessViolationException;
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Interop;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// RevitAPI-dependent dispatch helpers for <see cref="CrossAppSelectionTool"/>.
    /// Kept in a separate class so that the JIT does not attempt to load
    /// RevitAPI.dll when compiling <c>CrossAppSelectionTool.Execute</c> —
    /// the dispatch to these methods only happens after all input validation
    /// (and thus never in unit-test hosts that exit early).
    /// </summary>
    internal static class CrossAppSelectionRevitDispatch
    {
        internal static CortexResult<object> ExecuteExport(object docObj)
        {
            var doc = (Document)docObj;
            var uiDoc = new UIDocument(doc);
            var output = SelectionExporter.Export(uiDoc);
            return CortexResult<object>.Ok(new
            {
                side = "revit",
                exportedCount = output.Refs.Count,
                refs = output.Refs,
                skipped = output.Skipped
            });
        }

        internal static CortexResult<object> ExecuteImport(object docObj, JObject input, CortexSession session)
        {
            var doc = (Document)docObj;
            var refsToken = input["refs"] as JArray;
            if (refsToken == null || refsToken.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "refs is required and cannot be empty",
                    suggestion: "Pass refs=[CortexElementRef, ...] from the export side.");

            var refs = new System.Collections.Generic.List<CortexElementRef>();
            foreach (var t in refsToken)
            {
                var r = t.ToObject<CortexElementRef>();
                if (r != null) refs.Add(r);
            }
            if (refs.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "no parseable CortexElementRef in refs");

            var resolver = HostLinkResolver.Build(doc);

            var hostIds = new System.Collections.Generic.List<long>();
            var linkedTargets = new JArray();
            var notFound = new System.Collections.Generic.List<object>();

            foreach (var r in refs)
            {
                var outcome = resolver.Resolve(r);
                if (outcome.IsHost && outcome.HostElementId != null)
                {
                    hostIds.Add(GetIdValue(outcome.HostElementId));
                }
                else if (outcome.IsLinked
                    && outcome.LinkInstanceId != null
                    && outcome.LinkedElementId != null)
                {
                    linkedTargets.Add(new JObject
                    {
                        ["instanceId"]      = GetIdValue(outcome.LinkInstanceId),
                        ["linkedElementId"] = GetIdValue(outcome.LinkedElementId)
                    });
                }
                else
                {
                    notFound.Add(new
                    {
                        @ref = r,
                        reason = outcome.NotFoundReason ?? "unresolved"
                    });
                }
            }

            if (hostIds.Count == 0 && linkedTargets.Count == 0)
            {
                return CortexResult<object>.Ok(new
                {
                    side = "revit",
                    requested = refs.Count,
                    resolved = 0,
                    selected = 0,
                    hostMatches = 0,
                    linkedMatches = 0,
                    notFound,
                    message = "No refs resolved. Confirm sourceFile basenames match the host document or a loaded link."
                });
            }

            // Compose with show_cross_model_elements (no source changes there).
            var inner = new RevitCortex.Tools.LinkedFiles.ShowCrossModelElementsTool();
            var innerInput = new JObject
            {
                ["hostElementIds"]       = new JArray(hostIds),
                ["linkedElements"]       = linkedTargets,
                ["select"]               = input["append"]?.Value<bool>() == true ? false : true,
                ["isolate"]              = input["isolate"]?.Value<bool?>() ?? true,
                ["createSectionBox"]     = input["createSectionBox"]?.Value<bool?>() ?? true,
                ["createLinkedMarkers"]  = input["createLinkedMarkers"]?.Value<bool?>() ?? true,
                ["usePostCommandIsolate"] = input["usePostCommandIsolate"]?.Value<bool?>() ?? false
            };

            // Reuse the caller's session so any state seeded upstream
            // (audit context, per-call flags) flows through to the inner tool.
            // activeDocument is already set by the outer Execute path.
            var innerResult = inner.Execute(innerInput, session);

            return CortexResult<object>.Ok(new
            {
                side = "revit",
                requested = refs.Count,
                resolved = hostIds.Count + linkedTargets.Count,
                selected = hostIds.Count + linkedTargets.Count,
                hostMatches = hostIds.Count,
                linkedMatches = linkedTargets.Count,
                notFound,
                innerResult = innerResult.Data
            });
        }

        private static long GetIdValue(ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return (long)id.IntegerValue;
#endif
        }
    }

    /// <summary>
    /// Symmetric Revit↔Navis selection bridge. mode=export emits
    /// CortexElementRefs from the current Revit selection (host + linked).
    /// mode=import consumes CortexElementRefs and selects/isolates them by
    /// composing show_cross_model_elements (no source changes there).
    /// </summary>
    [ToolSafety(false, false)]
    public class CrossAppSelectionTool : ICortexTool
    {
        public string Name => "cross_app_selection";
        public string Category => "Interop";
        public bool RequiresDocument => true;
        public bool IsDynamic => true;
        public string Description =>
            "Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume CortexElementRefs and select/isolate them, automatically resolving each ref to host or linked Revit element via sourceFile basename match. Resolution priority: revitUniqueId → ifcGuid → revitElementId.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var mode = input["mode"]?.ToString();
            if (string.IsNullOrWhiteSpace(mode))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "mode is required",
                    suggestion: "Pass mode=\"export\" or mode=\"import\".");

            var modeLower = mode!.ToLowerInvariant();
            if (modeLower != "export" && modeLower != "import")
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown mode '{mode}'",
                    suggestion: "Pass mode=\"export\" or mode=\"import\".");

            // For mode=import, validate refs presence before requiring an active doc,
            // so callers get a clear "missing payload" error even without Revit ready.
            if (modeLower == "import")
            {
                var refsToken = input["refs"] as JArray;
                if (refsToken == null || refsToken.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "refs is required and cannot be empty",
                        suggestion: "Pass refs=[CortexElementRef, ...] from the export side.");
            }

            var docObj = session.Store.Get<object>("activeDocument");
            if (docObj == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No active Revit document in session");

            // Dispatch to RevitAPI-dependent helpers. These methods are in a separate
            // class so the JIT does not attempt to load RevitAPI.dll when compiling
            // this Execute method — which would fail in unit-test hosts.
            return modeLower == "export"
                ? CrossAppSelectionRevitDispatch.ExecuteExport(docObj)
                : CrossAppSelectionRevitDispatch.ExecuteImport(docObj, input, session);
        }
    }
}

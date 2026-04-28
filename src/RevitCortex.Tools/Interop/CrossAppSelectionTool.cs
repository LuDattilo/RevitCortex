using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
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

        internal static CortexResult<object> ExecuteImport(object docObj, JObject input)
        {
            // Import body lands in plan task 6.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "import path not yet implemented",
                suggestion: "Will be wired up in plan task 6.");
        }
    }

    /// <summary>
    /// Symmetric Revit↔Navis selection bridge. mode=export emits
    /// CortexElementRefs from the current Revit selection (host + linked).
    /// mode=import consumes CortexElementRefs and selects/isolates them
    /// (delegating to show_cross_model_elements; wired in plan task 6).
    /// </summary>
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
                : CrossAppSelectionRevitDispatch.ExecuteImport(docObj, input);
        }
    }
}

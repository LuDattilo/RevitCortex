using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Runtime;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>
    /// Runs a .dyn headless inside Revit via reflection (journal-based launch).
    /// Dynamic: only registered when Dynamo is present. All Dynamo access is reflection —
    /// a load failure returns a structured error, never crashes the plugin.
    /// The headless execution body is wired in Task 16 (needs live Revit+Dynamo).
    /// </summary>
    public sealed class DynamoRunGraphTool : ICortexTool
    {
        public string? SettingsPathForTests { get; set; }
        public bool SkipConfirmationForTests { get; set; }

        public string Name => "dynamo_run_graph";
        public string Category => "Dynamo";
        public bool RequiresDocument => true;
        public bool IsDynamic => true;
        public string Description => "Run a Dynamo .dyn graph headless inside Revit and return its output. Use ONLY when no native RevitCortex tool covers the task AND the user explicitly approved a Dynamo/Python approach. REQUIRES EnableDynamo=true in ~/.revitcortex/settings.json.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var settings = CortexSettings.Load(SettingsPathForTests);
            if (!settings.EnableDynamo)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "dynamo_run_graph is disabled in this installation. STOP: do NOT retry this tool. Ask the user to enable Dynamo via Settings > Tools (or \"EnableDynamo\": true in ~/.revitcortex/settings.json).",
                    suggestion: "Do not retry. Ask the user to enable Dynamo, or use native tools.");

            var path = input["dynPath"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "dynPath is required");
            if (!System.IO.File.Exists(path))
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "File not found: " + path);

            if (!SkipConfirmationForTests && !session.RequestConfirmation("run Dynamo graph", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            int year = input["revitYear"]?.Value<int>() ?? 2025;
            var caps = new DynamoCapabilityProbe().Probe(year);
            if (!caps.IsPresent)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Dynamo for Revit is not installed for this Revit version.",
                    suggestion: "Open the .dyn manually in Dynamo, or install Dynamo for Revit.");

            var loader = new DynamoRuntimeLoader(caps.DynamoForRevitDir);
            var loadError = loader.EnsureLoaded();
            if (loadError != null)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    loadError,
                    suggestion: "The .dyn is still valid — open it manually in Dynamo. Check the Dynamo version compatibility.");

            return RunHeadless(input, session, caps);
        }

        private CortexResult<object> RunHeadless(JObject input, CortexSession session, DynamoCapabilities caps)
        {
            // Task 16 replaces this body with the reflection-driven journal launch
            // (JournalKeys + DynamoRevit.ExecuteCommand + ForceRun) verified on live R25.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "Headless run not yet wired (pending live integration, Task 16).",
                suggestion: "Open the generated .dyn manually in Dynamo for now.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            int year = input["revitYear"]?.Value<int>() ?? RevitYear.Current;
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

        /// <summary>
        /// Wired via reflection against DynamoRevitDS. NOT unit-tested (needs live Revit+Dynamo)
        /// — verified manually in Task 16. Every reflection step returns a diagnostic Fail
        /// rather than throwing: a null Type/MemberInfo or a TargetInvocationException produces
        /// a CortexResult.Fail naming exactly which reflection lookup failed, never a crash.
        /// Journal keys are hardcoded string values (stable across Dynamo 2.x/3.x) rather than
        /// reflected off JournalKeys, for robustness.
        /// </summary>
        /// <remarks>REQUIRES LIVE VERIFICATION (Task 16): the reflected member names below must
        /// be confirmed against the DynamoRevitDS.dll actually installed in the live Revit session.</remarks>
        private CortexResult<object> RunHeadless(JObject input, CortexSession session, DynamoCapabilities caps)
        {
            // Final safety net: any unexpected throw becomes a structured Fail, never a crash.
            try
            {
                // --- Step 1: UIApplication from session -----------------------------------
                // RevitCortex.Tools.Dynamo references Nice3point RevitAPIUI (reference-only) for
                // every TFM, so Autodesk.Revit.UI.UIApplication resolves at compile time on both
                // net48 (R23/R24) and net8+ (R25/R26/R27). It is a Revit type, not a Dynamo type.
                var uiapp = session.Store.Get<object>("uiApplication") as Autodesk.Revit.UI.UIApplication;
                if (uiapp == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "UIApplication not available in session",
                        suggestion: "The .dyn is still valid — open it manually in Dynamo. RevitCortex could not obtain a live UIApplication for the headless launch.");

                var dynPath = input["dynPath"]?.Value<string>() ?? "";

                // --- Step 2: journal data (hardcoded key VALUES — stable across 2.x/3.x) ---
                // Dynamo.Applications.JournalKeys: ShowUiKey="dynShowUI", AutomationModeKey="dynAutomation",
                // DynPathKey="dynPath", DynPathExecuteKey="dynPathExecute", ForceManualRunKey="dynForceManualRun".
                IDictionary<string, string> journalData = new Dictionary<string, string>
                {
                    ["dynShowUI"] = "False",
                    ["dynAutomation"] = "True",
                    ["dynPath"] = dynPath,
                    ["dynPathExecute"] = "True",
                    ["dynForceManualRun"] = "False",
                };

                // --- Step 3: DynamoRevitCommandData { Application, JournalData } -----------
                var cmdDataType = ResolveDynamoType("Dynamo.Applications.DynamoRevitCommandData");
                if (cmdDataType == null)
                    return ReflectionFail("resolving DynamoRevitCommandData type",
                        "Type 'Dynamo.Applications.DynamoRevitCommandData' not found in loaded assemblies (expected in DynamoRevitDS).");

                object? cmdData;
                try
                {
                    cmdData = Activator.CreateInstance(cmdDataType);
                }
                catch (Exception ex)
                {
                    return ReflectionFail("constructing DynamoRevitCommandData", Unwrap(ex));
                }
                if (cmdData == null)
                    return ReflectionFail("constructing DynamoRevitCommandData",
                        "Activator.CreateInstance returned null.");

                var applicationProp = cmdDataType.GetProperty("Application",
                    BindingFlags.Public | BindingFlags.Instance);
                if (applicationProp == null || !applicationProp.CanWrite)
                    return ReflectionFail("resolving DynamoRevitCommandData.Application setter",
                        "Property 'Application' not found or not settable.");
                try
                {
                    applicationProp.SetValue(cmdData, uiapp);
                }
                catch (Exception ex)
                {
                    return ReflectionFail("setting DynamoRevitCommandData.Application", Unwrap(ex));
                }

                var journalProp = cmdDataType.GetProperty("JournalData",
                    BindingFlags.Public | BindingFlags.Instance);
                if (journalProp == null || !journalProp.CanWrite)
                    return ReflectionFail("resolving DynamoRevitCommandData.JournalData setter",
                        "Property 'JournalData' not found or not settable.");
                try
                {
                    journalProp.SetValue(cmdData, journalData);
                }
                catch (Exception ex)
                {
                    return ReflectionFail("setting DynamoRevitCommandData.JournalData", Unwrap(ex));
                }

                // --- Step 4: DynamoRevit.ExecuteCommand(cmdData) --------------------------
                var dynamoRevitType = ResolveDynamoType("Dynamo.Applications.DynamoRevit");
                if (dynamoRevitType == null)
                    return ReflectionFail("resolving DynamoRevit type",
                        "Type 'Dynamo.Applications.DynamoRevit' not found in loaded assemblies (expected in DynamoRevitDS).");

                object? dynamoRevit;
                try
                {
                    dynamoRevit = Activator.CreateInstance(dynamoRevitType);
                }
                catch (Exception ex)
                {
                    return ReflectionFail("constructing DynamoRevit", Unwrap(ex));
                }
                if (dynamoRevit == null)
                    return ReflectionFail("constructing DynamoRevit",
                        "Activator.CreateInstance returned null.");

                var executeCommand = dynamoRevitType.GetMethod("ExecuteCommand",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null, types: new[] { cmdDataType }, modifiers: null);
                if (executeCommand == null)
                    return ReflectionFail("resolving DynamoRevit.ExecuteCommand(DynamoRevitCommandData)",
                        "Method 'ExecuteCommand' with a single DynamoRevitCommandData parameter not found.");

                object? execResult;
                try
                {
                    execResult = executeCommand.Invoke(dynamoRevit, new[] { cmdData });
                }
                catch (Exception ex)
                {
                    return ReflectionFail("invoking ExecuteCommand", Unwrap(ex));
                }
                string resultText = execResult?.ToString() ?? "(null)";

                // --- Step 5: static DynamoRevit.RevitDynamoModel --------------------------
                var modelProp = dynamoRevitType.GetProperty("RevitDynamoModel",
                    BindingFlags.Public | BindingFlags.Static);
                if (modelProp == null)
                    return ReflectionFail("resolving static DynamoRevit.RevitDynamoModel",
                        "Static property 'RevitDynamoModel' not found on DynamoRevit.");

                object? model;
                try
                {
                    model = modelProp.GetValue(null);
                }
                catch (Exception ex)
                {
                    return ReflectionFail("reading RevitDynamoModel", Unwrap(ex));
                }
                if (model == null)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        "Dynamo model did not initialize",
                        suggestion: "The .dyn is still valid — open it manually in Dynamo. This may be a Dynamo version/API mismatch; report the failing step.");

                // --- Step 6: model.ForceRun() (best-effort; FlattenHierarchy for base type) -
                // KNOWN PITFALL: on Dynamo 3.0 (Revit 2025+) ForceRun() may SILENTLY no-op on
                // Manual-mode graphs. Our generated graphs are RunType:Automatic so this is
                // mitigated, but for arbitrary user .dyn it may not run. We therefore do NOT
                // treat the return as proof of success — see the note in the response.
                var modelType = model.GetType();
                var forceRun = modelType.GetMethod("ForceRun",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy,
                    binder: null, types: Type.EmptyTypes, modifiers: null);
                string? forceRunNote = null;
                if (forceRun != null)
                {
                    try
                    {
                        forceRun.Invoke(model, null);
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal: evaluation may already have run via ExecuteCommand.
                        forceRunNote = "ForceRun() threw and was ignored: " + Unwrap(ex);
                    }
                }
                else
                {
                    forceRunNote = "ForceRun() not found on the model — relying on ExecuteCommand evaluation.";
                }

                // --- Step 7: read outputs from Watch nodes --------------------------------
                var outputs = new List<object>();
                var skippedNodes = new List<string>();

                var currentWorkspaceProp = modelType.GetProperty("CurrentWorkspace",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                object? workspace = null;
                if (currentWorkspaceProp != null)
                {
                    try { workspace = currentWorkspaceProp.GetValue(model); }
                    catch (Exception ex) { skippedNodes.Add("CurrentWorkspace read failed: " + Unwrap(ex)); }
                }
                else
                {
                    skippedNodes.Add("CurrentWorkspace property not found on the model.");
                }

                if (workspace != null)
                {
                    var nodesProp = workspace.GetType().GetProperty("Nodes",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    System.Collections.IEnumerable? nodes = null;
                    if (nodesProp != null)
                    {
                        try { nodes = nodesProp.GetValue(workspace) as System.Collections.IEnumerable; }
                        catch (Exception ex) { skippedNodes.Add("CurrentWorkspace.Nodes read failed: " + Unwrap(ex)); }
                    }
                    else
                    {
                        skippedNodes.Add("CurrentWorkspace.Nodes property not found.");
                    }

                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                        {
                            if (node == null) continue;
                            // Per-node failures are isolated: skip the node, never fail the run.
                            try
                            {
                                var nodeType = node.GetType();

                                // Identify Watch nodes by ConcreteType or Name containing "Watch".
                                string nodeName = ReadStringProp(node, nodeType, "Name") ?? nodeType.Name;
                                string concreteType = ReadStringProp(node, nodeType, "ConcreteType") ?? "";
                                bool isWatch =
                                    (nodeName.IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                    (concreteType.IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                    (nodeType.FullName ?? "").IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (!isWatch) continue;

                                var cachedValueProp = nodeType.GetProperty("CachedValue",
                                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                                object? cachedValue = cachedValueProp?.GetValue(node);
                                object? data = null;
                                if (cachedValue != null)
                                {
                                    var dataProp = cachedValue.GetType().GetProperty("Data",
                                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                                    data = dataProp?.GetValue(cachedValue);
                                }

                                outputs.Add(new
                                {
                                    nodeName,
                                    value = data?.ToString() ?? "(null)"
                                });
                            }
                            catch (Exception ex)
                            {
                                skippedNodes.Add("Node value read failed: " + Unwrap(ex));
                            }
                        }
                    }
                }

                return CortexResult<object>.Ok(new
                {
                    executed = true,
                    result = resultText,
                    outputs,
                    skippedNodes,
                    forceRunNote,
                    note = "Verify in Dynamo if outputs are empty — Dynamo 3.0 may no-op Manual-mode graphs. REQUIRES LIVE VERIFICATION (Task 16)."
                });
            }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Unexpected error during headless run: " + ex.Message,
                    suggestion: "The .dyn is still valid — open it manually in Dynamo. This may be a Dynamo version/API mismatch; report the failing step.");
            }
        }

        /// <summary>
        /// Scans all assemblies loaded into the current AppDomain for a type by its full name.
        /// Preferred over Type.GetType because DynamoRevitDS is LoadFrom'd (not in the default
        /// probing path), so its types are not reachable by assembly-qualified name alone.
        /// Returns null if not found — callers must guard.
        /// </summary>
        private static Type? ResolveDynamoType(string fullName)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? t = null;
                    try { t = asm.GetType(fullName, throwOnError: false); }
                    catch { t = null; } // ReflectionTypeLoad / other quirks: ignore this assembly
                    if (t != null) return t;
                }
            }
            catch
            {
                // AppDomain enumeration itself failed — treat as "not found".
            }
            return null;
        }

        /// <summary>Builds a consistent diagnostic Fail naming the reflection step that failed.</summary>
        private static CortexResult<object> ReflectionFail(string step, string detail) =>
            CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "Dynamo headless run failed at " + step + ": " + detail,
                suggestion: "The .dyn is still valid — open it manually in Dynamo. This may be a Dynamo version/API mismatch; report the failing step.");

        /// <summary>Unwraps TargetInvocationException to surface the real cause message.</summary>
        private static string Unwrap(Exception ex)
        {
            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
            return inner.GetType().Name + ": " + inner.Message;
        }

        /// <summary>Reads a string property defensively; returns null on any failure.</summary>
        private static string? ReadStringProp(object instance, Type type, string propName)
        {
            try
            {
                var p = type.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                return p?.GetValue(instance)?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Building;
using RevitCortex.Tools.Dynamo.Security;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>
    /// Generates and saves a valid Python-centric .dyn. Static: never loads Dynamo.
    /// Gated by EnableDynamo + PythonSandbox + confirmation, mirroring send_code_to_revit.
    /// </summary>
    public sealed class DynamoGenerateGraphTool : ICortexTool
    {
        public static readonly string DefaultGraphsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcortex", "dynamo-graphs");

        public string? SettingsPathForTests { get; set; }
        public bool SkipConfirmationForTests { get; set; }

        public string Name => "dynamo_generate_graph";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Generate and save a valid Python-centric Dynamo .dyn graph from a Python body + typed inputs/outputs. Use ONLY when no native RevitCortex tool covers the task AND the user explicitly approved a Dynamo/Python approach. REQUIRES EnableDynamo=true in ~/.revitcortex/settings.json.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var settings = CortexSettings.Load(SettingsPathForTests);
            if (!settings.EnableDynamo)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "dynamo_generate_graph is disabled in this installation. STOP: do NOT retry this tool. Ask the user to enable Dynamo via Settings > Tools (or \"EnableDynamo\": true in ~/.revitcortex/settings.json), or solve the task with native tools.",
                    suggestion: "Do not retry. Either ask the user to enable Dynamo in Settings, or use native RevitCortex tools.");

            var pythonCode = input["pythonCode"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(pythonCode))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "pythonCode is required");

            var name = SanitizeName(input["name"]?.Value<string>() ?? "RevitCortexGraph");
            var inputs = ParsePorts(input["inputs"] as JArray);
            var outputs = ParsePorts(input["outputs"] as JArray);
            var engine = input["engine"]?.Value<string>() ?? "CPython3";

            var spec = new DynamoGraphSpec(name, pythonCode!, inputs, outputs, engine);

            var builder = new DynamoGraphBuilder();
            var validation = builder.ValidateSpec(spec);
            if (!validation.IsValid)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Invalid graph spec: " + string.Join("; ", validation.Errors));

            var sandbox = PythonSandbox.Validate(pythonCode!);
            if (sandbox != null) return sandbox;

            if (!SkipConfirmationForTests && !session.RequestConfirmation("generate Dynamo graph", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            string savePath = input["savePath"]?.Value<string>() ?? DefaultSavePath(name);
            try
            {
                var dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = builder.BuildDynJson(spec);
                File.WriteAllText(savePath, json);

                var result = JObject.FromObject(new
                {
                    savedTo = savePath,
                    name = spec.Name,
                    engine = spec.Engine,
                    inputCount = spec.Inputs.Count,
                    outputCount = spec.Outputs.Count,
                    bytes = new FileInfo(savePath).Length
                });
                result["executeRequested"] = input["execute"]?.Value<bool>() ?? false;
                return CortexResult<object>.Ok(result);
            }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.Unknown, "Failed to write .dyn: " + ex.Message);
            }
        }

        private static List<GraphPort> ParsePorts(JArray? arr)
        {
            var list = new List<GraphPort>();
            if (arr == null) return list;
            foreach (var e in arr)
            {
                var n = e["name"]?.Value<string>() ?? "";
                var t = e["type"]?.Value<string>() ?? "String";
                list.Add(new GraphPort(n, t));
            }
            return list;
        }

        private static string DefaultSavePath(string name)
            => UniquePath(Path.Combine(DefaultGraphsFolder, name + ".dyn"));

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(dir, stem + "_" + i + ext);
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string SanitizeName(string name)
        {
            var safe = Regex.Replace(name, @"[^\w\-]", "-").Trim('-');
            if (safe.Length == 0) safe = "RevitCortexGraph";
            return safe.Substring(0, Math.Min(safe.Length, 60));
        }
    }
}

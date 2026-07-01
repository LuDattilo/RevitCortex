using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>
    /// Deterministically builds a valid Python-centric .dyn JSON document.
    /// Never loads any Dynamo DLL — pure string/JSON construction.
    /// </summary>
    public sealed class DynamoGraphBuilder
    {
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Number", "Boolean", "Filename"
        };

        public DynamoValidationResult ValidateSpec(DynamoGraphSpec spec)
        {
            var errors = new List<string>();
            if (spec == null) return DynamoValidationResult.Fail("spec is null");

            if (string.IsNullOrWhiteSpace(spec.PythonCode))
                errors.Add("pythonCode is empty");

            CheckPorts("input", spec.Inputs, errors);
            CheckPorts("output", spec.Outputs, errors);

            return errors.Count == 0
                ? DynamoValidationResult.Ok()
                : DynamoValidationResult.Fail(errors.ToArray());
        }

        private static void CheckPorts(string kind, IReadOnlyList<GraphPort> ports, List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ports)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    errors.Add($"{kind} port has empty name");
                else if (!seen.Add(p.Name))
                    errors.Add($"duplicate {kind} port name: {p.Name}");
                if (!AllowedTypes.Contains(p.Type))
                    errors.Add($"unknown {kind} port type: {p.Type}");
            }
        }

        public string BuildDynJson(DynamoGraphSpec spec)
        {
            var nodes = new JArray();
            var connectors = new JArray();
            var nodeViews = new JArray();
            var topInputs = new JArray();
            var topOutputs = new JArray();

            string pyId = NewId();
            var pyInputPorts = new JArray();
            var pyInputPortIds = new List<string>();
            for (int i = 0; i < spec.Inputs.Count; i++)
            {
                string portId = NewId();
                pyInputPortIds.Add(portId);
                pyInputPorts.Add(Port(portId, "IN" + i, "Input #" + i));
            }
            string pyOutPortId = NewId();
            var pyOutputPorts = new JArray { Port(pyOutPortId, "OUT", "Result of the python script") };

            int y = 0;
            for (int i = 0; i < spec.Inputs.Count; i++)
            {
                var gp = spec.Inputs[i];
                string nodeId = NewId();
                string outPortId = NewId();
                var inputNode = InputNode(gp, nodeId, outPortId);
                nodes.Add(inputNode);
                nodeViews.Add(NodeView(nodeId, gp.Name, 0, y, isInput: true));
                connectors.Add(Connector(outPortId, pyInputPortIds[i]));
                topInputs.Add(TopInput(nodeId, gp));
                y += 150;
            }

            var pyNode = new JObject
            {
                ["ConcreteType"] = DynJsonSchema.PythonNodeConcreteType,
                ["Code"] = NormalizeNewlines(spec.PythonCode),
                ["Engine"] = string.IsNullOrEmpty(spec.Engine) ? DynJsonSchema.EngineCPython3 : spec.Engine,
                ["VariableInputPorts"] = true,
                ["Id"] = pyId,
                ["NodeType"] = DynJsonSchema.PythonNodeType,
                ["Inputs"] = pyInputPorts,
                ["Outputs"] = pyOutputPorts,
                ["Replication"] = "Disabled",
                ["Description"] = "Runs an embedded Python script."
            };
            nodes.Add(pyNode);
            nodeViews.Add(NodeView(pyId, "Python Script", 350, 0, isInput: false));

            int wy = 0;
            foreach (var gp in spec.Outputs)
            {
                string watchId = NewId();
                string watchInId = NewId();
                string watchOutId = NewId();
                var watch = new JObject
                {
                    ["ConcreteType"] = DynJsonSchema.WatchConcreteType,
                    ["Id"] = watchId,
                    ["NodeType"] = DynJsonSchema.WatchNodeType,
                    ["Inputs"] = new JArray { Port(watchInId, "", "Node to evaluate.") },
                    ["Outputs"] = new JArray { Port(watchOutId, "", "Watch contents.") },
                    ["Description"] = "Visualizes a node's output"
                };
                nodes.Add(watch);
                nodeViews.Add(NodeView(watchId, gp.Name, 700, wy, isInput: false, isOutput: true));
                connectors.Add(Connector(pyOutPortId, watchInId));
                topOutputs.Add(new JObject { ["Id"] = watchId, ["Name"] = gp.Name });
                wy += 150;
            }

            var doc = new JObject
            {
                ["Uuid"] = System.Guid.NewGuid().ToString(),
                ["IsCustomNode"] = false,
                ["Description"] = "",
                ["Name"] = spec.Name,
                ["ElementResolver"] = new JObject { ["ResolutionMap"] = new JObject() },
                ["Inputs"] = topInputs,
                ["Outputs"] = topOutputs,
                ["Nodes"] = nodes,
                ["Connectors"] = connectors,
                ["Dependencies"] = new JArray(),
                ["NodeLibraryDependencies"] = new JArray(),
                ["EnableLegacyPolyCurveBehavior"] = true,
                ["Thumbnail"] = "",
                ["GraphDocumentationURL"] = null,
                ["ExtensionWorkspaceData"] = new JArray(),
                ["Author"] = "RevitCortex",
                ["Linting"] = new JObject
                {
                    ["activeLinter"] = "None",
                    ["activeLinterId"] = "7b75fb44-43fd-4631-a878-29f4d5d8399a",
                    ["warningCount"] = 0,
                    ["errorCount"] = 0
                },
                ["Bindings"] = new JArray(),
                ["View"] = new JObject
                {
                    ["Dynamo"] = new JObject
                    {
                        ["ScaleFactor"] = 1.0,
                        ["HasRunWithoutCrash"] = true,
                        ["IsVisibleInDynamoLibrary"] = true,
                        ["Version"] = "3.0.0.0",
                        ["RunType"] = "Automatic",
                        ["RunPeriod"] = "1000"
                    },
                    ["Camera"] = new JObject
                    {
                        ["Name"] = "_Background Preview",
                        ["EyeX"] = -17.0, ["EyeY"] = 24.0, ["EyeZ"] = 50.0,
                        ["LookX"] = 12.0, ["LookY"] = -13.0, ["LookZ"] = -58.0,
                        ["UpX"] = 0.0, ["UpY"] = 1.0, ["UpZ"] = 0.0
                    },
                    ["ConnectorPins"] = new JArray(),
                    ["NodeViews"] = nodeViews,
                    ["Annotations"] = new JArray(),
                    ["X"] = 0.0,
                    ["Y"] = 0.0,
                    ["Zoom"] = 1.0
                }
            };

            return doc.ToString(Formatting.Indented);
        }

        private static string NewId() => System.Guid.NewGuid().ToString("N");

        private static string NormalizeNewlines(string code)
            => (code ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        private static JObject Port(string id, string name, string description) => new JObject
        {
            ["Id"] = id,
            ["Name"] = name,
            ["Description"] = description,
            ["UsingDefaultValue"] = false,
            ["Level"] = 2,
            ["UseLevels"] = false,
            ["KeepListStructure"] = false
        };

        private static JObject Connector(string startPortId, string endPortId) => new JObject
        {
            ["Start"] = startPortId,
            ["End"] = endPortId,
            ["Id"] = NewId(),
            ["IsHidden"] = "False"
        };

        private static JObject NodeView(string id, string name, double x, double y,
            bool isInput = false, bool isOutput = false) => new JObject
        {
            ["Id"] = id,
            ["Name"] = string.IsNullOrEmpty(name) ? "Node" : name,
            ["IsSetAsInput"] = isInput,
            ["IsSetAsOutput"] = isOutput,
            ["Excluded"] = false,
            ["ShowGeometry"] = true,
            ["X"] = x,
            ["Y"] = y
        };

        private static JObject InputNode(GraphPort gp, string nodeId, string outPortId)
        {
            if (string.Equals(gp.Type, "Integer", System.StringComparison.OrdinalIgnoreCase))
            {
                return new JObject
                {
                    ["ConcreteType"] = DynJsonSchema.IntegerSliderConcreteType,
                    ["NumberType"] = "Integer",
                    ["MaximumValue"] = 100,
                    ["MinimumValue"] = 0,
                    ["StepValue"] = 1,
                    ["Id"] = nodeId,
                    ["NodeType"] = DynJsonSchema.NumberInputNodeType,
                    ["Inputs"] = new JArray(),
                    ["Outputs"] = new JArray { Port(outPortId, "", "Int64") },
                    ["Replication"] = "Disabled",
                    ["Description"] = "Produces integer values",
                    ["InputValue"] = 0
                };
            }
            return new JObject
            {
                ["ConcreteType"] = DynJsonSchema.StringInputConcreteType,
                ["NodeType"] = DynJsonSchema.StringInputNodeType,
                ["InputValue"] = "",
                ["Id"] = nodeId,
                ["Inputs"] = new JArray(),
                ["Outputs"] = new JArray { Port(outPortId, "", "String") }
            };
        }

        private static JObject TopInput(string nodeId, GraphPort gp) => new JObject
        {
            ["Id"] = nodeId,
            ["Name"] = gp.Name,
            ["Type"] = gp.Type.ToLowerInvariant(),
            ["Value"] = "",
            ["Description"] = "RevitCortex graph input"
        };
    }
}

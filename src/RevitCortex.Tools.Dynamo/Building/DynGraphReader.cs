using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Dynamo.Building
{
    public sealed class GraphIoEntry
    {
        public string NodeId { get; }
        public string Name { get; }
        public string Type { get; }
        public string Value { get; }
        public GraphIoEntry(string nodeId, string name, string type, string value)
        { NodeId = nodeId; Name = name; Type = type; Value = value; }
    }

    public sealed class DynGraphInfo
    {
        public string Name { get; internal set; } = "";
        public string DynamoVersion { get; internal set; } = "";
        public string PythonEngine { get; internal set; } = "";
        public int PythonNodeCount { get; internal set; }
        public int TotalNodes { get; internal set; }
        public List<GraphIoEntry> Inputs { get; } = new List<GraphIoEntry>();
        public List<GraphIoEntry> Outputs { get; } = new List<GraphIoEntry>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Parses a .dyn (JSON only — never loads Dynamo) to expose its I/O interface.</summary>
    public static class DynGraphReader
    {
        public static DynGraphInfo Read(string dynJson)
        {
            var j = JObject.Parse(dynJson); // throws on invalid json (tested)
            var info = new DynGraphInfo
            {
                Name = (string)j["Name"] ?? "",
                DynamoVersion = (string)(j["View"]?["Dynamo"]?["Version"]) ?? ""
            };

            var nodes = j["Nodes"] as JArray ?? new JArray();
            info.TotalNodes = nodes.Count;
            foreach (var n in nodes)
            {
                var ct = (string)n["ConcreteType"] ?? "";
                if (ct.StartsWith("PythonNodeModels.PythonNode"))
                {
                    info.PythonNodeCount++;
                    var engine = (string)n["Engine"];
                    if (string.IsNullOrEmpty(engine))
                    {
                        engine = "IronPython2";
                        info.Warnings.Add("A Python node has no Engine field; Dynamo interprets it as deprecated IronPython2.");
                    }
                    if (string.IsNullOrEmpty(info.PythonEngine))
                        info.PythonEngine = engine;
                }
            }

            foreach (var inp in (j["Inputs"] as JArray ?? new JArray()))
                info.Inputs.Add(new GraphIoEntry(
                    (string)inp["Id"] ?? "", (string)inp["Name"] ?? "",
                    (string)inp["Type"] ?? "", (string)(inp["Value"]) ?? ""));

            foreach (var outp in (j["Outputs"] as JArray ?? new JArray()))
                info.Outputs.Add(new GraphIoEntry(
                    (string)outp["Id"] ?? "", (string)outp["Name"] ?? "",
                    (string)outp["Type"] ?? "", ""));

            return info;
        }
    }
}

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
                Name = Str(j["Name"]),
                DynamoVersion = Str(j["View"]?["Dynamo"]?["Version"])
            };

            var nodes = j["Nodes"] as JArray ?? new JArray();
            info.TotalNodes = nodes.Count;
            foreach (var n in nodes)
            {
                if (!(n is JObject nodeObj)) continue;
                var ct = Str(nodeObj["ConcreteType"]);
                if (ct.StartsWith("PythonNodeModels.PythonNode"))
                {
                    info.PythonNodeCount++;
                    var engineStr = Str(nodeObj["Engine"]);
                    if (string.IsNullOrEmpty(engineStr))
                    {
                        engineStr = "IronPython2";
                        info.Warnings.Add("A Python node has no Engine field; Dynamo interprets it as deprecated IronPython2.");
                    }
                    if (string.IsNullOrEmpty(info.PythonEngine))
                        info.PythonEngine = engineStr;
                }
            }

            foreach (var inp in (j["Inputs"] as JArray ?? new JArray()))
            {
                if (!(inp is JObject inObj)) continue;
                info.Inputs.Add(new GraphIoEntry(
                    Str(inObj["Id"]), Str(inObj["Name"]),
                    Str(inObj["Type"]), Str(inObj["Value"])));
            }

            foreach (var outp in (j["Outputs"] as JArray ?? new JArray()))
            {
                if (!(outp is JObject outObj)) continue;
                info.Outputs.Add(new GraphIoEntry(
                    Str(outObj["Id"]), Str(outObj["Name"]),
                    Str(outObj["Type"]), ""));
            }

            return info;
        }

        private static string Str(JToken? t)
            => t != null && t.Type == JTokenType.String ? (string)t! : "";
    }
}

using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Building;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>Parses a .dyn file (JSON only, never loads Dynamo) and returns its I/O interface.</summary>
    public sealed class DynamoListGraphIoTool : ICortexTool
    {
        public string Name => "dynamo_list_graph_io";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "List the inputs and outputs of a .dyn Dynamo graph (parses the file, does not run it). Use before dynamo_run_graph to know which inputValues to pass.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var path = input["dynPath"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "dynPath is required");
            if (!File.Exists(path))
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "File not found: " + path);

            try
            {
                var info = DynGraphReader.Read(File.ReadAllText(path!));
                return CortexResult<object>.Ok(new
                {
                    name = info.Name,
                    dynamoVersion = info.DynamoVersion,
                    pythonEngine = info.PythonEngine,
                    pythonNodeCount = info.PythonNodeCount,
                    totalNodes = info.TotalNodes,
                    inputs = info.Inputs,
                    outputs = info.Outputs,
                    warnings = info.Warnings
                });
            }
            catch (System.Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Could not parse .dyn: " + ex.Message,
                    suggestion: "Ensure the file is a valid Dynamo 2.x/3.x JSON graph.");
            }
        }
    }
}

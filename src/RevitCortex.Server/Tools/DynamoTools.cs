using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class DynamoTools
{
    [McpServerTool(Name = "dynamo_get_status"),
     Description("Report Dynamo for Revit status (present, version, CPython3 availability) and whether EnableDynamo is set.")]
    public static async Task<string> DynamoGetStatus(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("dynamo_get_status", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "dynamo_list_graph_io"),
     Description("List inputs/outputs of a .dyn graph (parses the file, does not run it).")]
    public static async Task<string> DynamoListGraphIo(
        RevitConnectionManager revit,
        [Description("Absolute path to the .dyn file")] string dynPath,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dynPath"] = dynPath };
        var result = await revit.ExecuteAsync("dynamo_list_graph_io", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "dynamo_generate_graph"),
     Description("Generate and save a valid Python-centric Dynamo .dyn graph. Use ONLY when no native RevitCortex tool covers the task AND the user approved a Dynamo/Python approach. Requires EnableDynamo=true.")]
    public static async Task<string> DynamoGenerateGraph(
        RevitConnectionManager revit,
        [Description("Graph name (used for the default file name)")] string name,
        [Description("Python body; inputs arrive as list IN, output assigned to OUT")] string pythonCode,
        [Description("JSON array of inputs: [{\"name\":\"folder\",\"type\":\"String\"}]")] string? inputs = null,
        [Description("JSON array of outputs: [{\"name\":\"result\"}]")] string? outputs = null,
        [Description("Optional absolute save path; defaults to ~/.revitcortex/dynamo-graphs/")] string? savePath = null,
        [Description("If true, run the graph headless after saving")] bool execute = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["pythonCode"] = pythonCode,
            ["execute"] = execute
        };
        if (!string.IsNullOrEmpty(inputs)) p["inputs"] = JArray.Parse(inputs);
        if (!string.IsNullOrEmpty(outputs)) p["outputs"] = JArray.Parse(outputs);
        if (!string.IsNullOrEmpty(savePath)) p["savePath"] = savePath;

        var gen = await revit.ExecuteAsync("dynamo_generate_graph", p, ct);

        if (execute && gen["success"]?.Value<bool>() != false)
        {
            var savedTo = gen["savedTo"]?.ToString() ?? gen["data"]?["savedTo"]?.ToString();
            if (!string.IsNullOrEmpty(savedTo))
            {
                var runP = new JObject { ["dynPath"] = savedTo };
                var run = await revit.ExecuteAsync("dynamo_run_graph", runP, ct);
                return new JObject { ["generate"] = gen, ["run"] = run }.ToString();
            }
        }
        return gen.ToString();
    }

    [McpServerTool(Name = "dynamo_run_graph"),
     Description("Run a Dynamo .dyn graph headless inside Revit. Use ONLY when no native RevitCortex tool covers the task AND the user approved a Dynamo/Python approach. Requires EnableDynamo=true.")]
    public static async Task<string> DynamoRunGraph(
        RevitConnectionManager revit,
        [Description("Absolute path to the .dyn file")] string dynPath,
        [Description("Optional JSON object of input values: {\"folder\":\"C:/out\"}")] string? inputValues = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dynPath"] = dynPath };
        if (!string.IsNullOrEmpty(inputValues)) p["inputValues"] = JObject.Parse(inputValues);
        var result = await revit.ExecuteAsync("dynamo_run_graph", p, ct);
        return result.ToString();
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Plugin.Communication;

public class JsonRpcRequest
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("method")]
    public string Method { get; set; } = "";

    [JsonProperty("params")]
    public JObject? Params { get; set; }

    [JsonProperty("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The tool name the caller actually invoked, when it differs from
    /// <see cref="Method"/> — several agent-facing MCP tools (create_wall,
    /// create_door, create_window) route through one shared generic RiveTT
    /// tool. Used only for messages (e.g. the write-lock refusal); routing
    /// always keys off <see cref="Method"/>. See P1.6 in PLAN_CORRECTION.md.
    /// </summary>
    [JsonProperty("publicTool", NullValueHandling = NullValueHandling.Ignore)]
    public string? PublicTool { get; set; }
}

public class JsonRpcResponse
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
    public object? Result { get; set; }

    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Success(string? id, object result)
        => new() { Id = id, Result = result };

    public static JsonRpcResponse Fail(string? id, int code, string message, JToken? data = null)
        => new() { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
}

public class JsonRpcError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// Optional structured error data (JSON-RPC 2.0 §5.1).
    /// Carries the full CortexError object so the server bridge can
    /// reconstruct typed error information without string-parsing the message.
    /// </summary>
    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? Data { get; set; }
}

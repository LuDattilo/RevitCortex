using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.Communication;

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

    public static JsonRpcResponse Fail(string? id, int code, string message)
        => new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
}

public class JsonRpcError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

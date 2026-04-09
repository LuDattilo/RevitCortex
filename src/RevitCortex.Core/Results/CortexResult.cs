using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitCortex.Core.Results;

public class CortexResult<T>
{
    [JsonProperty("success")]
    public bool Success { get; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public T? Data { get; }

    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public CortexError? Error { get; }

    private CortexResult(bool success, T? data, CortexError? error)
    {
        Success = success;
        Data = data;
        Error = error;
    }

    public static CortexResult<T> Ok(T data)
        => new(true, data, null);

    public static CortexResult<T> Fail(CortexErrorCode code, string message,
        string? suggestion = null, Dictionary<string, object>? context = null)
        => new(false, default, new CortexError(code, message, suggestion, context));
}

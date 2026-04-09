using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace RevitCortex.Core.Results;

public class CortexError
{
    [JsonConverter(typeof(StringEnumConverter))]
    [JsonProperty("code")]
    public CortexErrorCode Code { get; }

    [JsonProperty("message")]
    public string Message { get; }

    [JsonProperty("suggestion", NullValueHandling = NullValueHandling.Ignore)]
    public string? Suggestion { get; }

    [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object>? Context { get; }

    public CortexError(CortexErrorCode code, string message,
        string? suggestion = null, Dictionary<string, object>? context = null)
    {
        Code = code;
        Message = message;
        Suggestion = suggestion;
        Context = context;
    }
}

using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

public class SayHelloTool : ICortexTool
{
    public string Name => "say_hello";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var message = input["message"]?.ToString() ?? "Hello from RevitCortex!";

        return CortexResult<object>.Ok(new
        {
            message,
            locale = session.DetectedLocale,
            toolCount = "RevitCortex is running"
        });
    }
}

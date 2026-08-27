using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Meta;

[ToolSafety(true, false)]
public class SayHelloTool : ICortexTool
{
    public string Name => "ping_revit";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Say Hello";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var message = input["message"]?.ToString() ?? "Hello from RiveTT!";

        return CortexResult<object>.Ok(new
        {
            message,
            locale = session.DetectedLocale,
            toolCount = "RiveTT is running"
        });
    }
}

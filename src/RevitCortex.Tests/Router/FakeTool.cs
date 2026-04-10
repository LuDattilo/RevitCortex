using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tests.Router;

public class FakeTool : ICortexTool
{
    public string Name { get; set; } = "fake_tool";
    public string Category { get; set; } = "Test";
    public bool RequiresDocument { get; set; } = false;
    public bool IsDynamic { get; set; } = false;
    public string Description { get; set; } = "A fake tool for testing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        return CortexResult<object>.Ok(new { called = true, toolName = Name });
    }
}

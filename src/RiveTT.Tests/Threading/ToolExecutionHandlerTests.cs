using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Plugin.Threading;
using Xunit;

namespace RiveTT.Tests.Threading;

public class ToolExecutionHandlerTests
{
    private sealed class CountingTool : ICortexTool
    {
        private readonly List<string> _executed;

        public CountingTool(string name, List<string> executed)
        {
            Name = name;
            _executed = executed;
        }

        public string Name { get; }
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Counting test tool";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            _executed.Add(Name);
            return CortexResult<object>.Ok(new { Name });
        }
    }

    [RequiresRevitApiFact]
    public void TryPrepareExecution_RejectsSecondCommandUntilFirstExternalEventDrains()
    {
        var executed = new List<string>();
        var session = new CortexSession(new SessionStore());
        var handler = new ToolExecutionHandler();

        Assert.True(handler.TryPrepareExecution(new CountingTool("first", executed), new JObject(), session));
        Assert.False(handler.TryPrepareExecution(new CountingTool("second", executed), new JObject(), session));

        handler.Execute(null!);

        Assert.Equal(new[] { "first" }, executed);
        Assert.True(handler.TryPrepareExecution(new CountingTool("second", executed), new JObject(), session));
    }
}

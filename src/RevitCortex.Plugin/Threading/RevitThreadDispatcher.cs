using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class RevitThreadDispatcher
{
    private readonly ToolExecutionHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private readonly object _lock = new object();

    public RevitThreadDispatcher(ToolExecutionHandler handler, ExternalEvent externalEvent)
    {
        _handler = handler;
        _externalEvent = externalEvent;
    }

    public CortexResult<object> Execute(ICortexTool tool, JObject input, CortexSession session,
        int timeoutMs = 120000)
    {
        lock (_lock)
        {
            if (!_handler.TryPrepareExecution(tool, input, session))
            {
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Tool '{tool.Name}' could not start because a previous Revit event is still pending or running",
                    suggestion: "Wait for Revit to finish the previous operation, then try again.");
            }

            var raiseResult = _externalEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _handler.ClearPreparedExecution();
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Revit rejected the event request: {raiseResult}",
                    suggestion: "Revit may be busy with another operation. Try again.");
            }

            if (!_handler.WaitForCompletion(timeoutMs))
            {
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Tool '{tool.Name}' timed out after {timeoutMs}ms",
                    suggestion: "The operation took too long. Try with fewer elements.");
            }

            return _handler.Result ?? CortexResult<object>.Fail(
                CortexErrorCode.Unknown, "No result from tool execution");
        }
    }
}

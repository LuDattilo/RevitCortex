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
        // H5: only the prepare + Raise pair must be atomic against other requests; the
        // shared ToolExecutionHandler already rejects a concurrent request via
        // TryPrepareExecution. Holding _lock across WaitForCompletion (up to 120s) would
        // serialize *every* tool call behind a single slow/hung operation, so the wait is
        // done OUTSIDE the lock.
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
        }

        if (!_handler.WaitForCompletion(timeoutMs))
        {
            // H5: clear the prepared/pending state on timeout, otherwise _hasPendingOrRunning
            // stays true and the very next request is refused until Revit eventually fires
            // the deferred event.
            _handler.ClearPreparedExecution();
            return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                $"Tool '{tool.Name}' timed out after {timeoutMs}ms",
                suggestion: "The operation took too long. If it was already running it may still complete inside Revit after this error (it would be logged as completed_after_timeout in the audit log) — verify the model state before retrying. Try with fewer elements.");
        }

        return _handler.Result ?? CortexResult<object>.Fail(
            CortexErrorCode.Unknown, "No result from tool execution");
    }
}

using System;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
    private volatile int _executionId;

    public ICortexTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public CortexSession? PendingSession { get; set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        int myId = _executionId;
        try
        {
            if (PendingTool == null || PendingInput == null || PendingSession == null)
            {
                if (_executionId == myId)
                    Result = CortexResult<object>.Fail(
                        CortexErrorCode.Unknown, "No pending tool execution");
                return;
            }

            var result = PendingTool.Execute(PendingInput, PendingSession);
            // Only store result if this execution is still current (not superseded by timeout)
            if (_executionId == myId)
                Result = result;
        }
        catch (Exception ex)
        {
            if (_executionId == myId)
                Result = CortexResult<object>.Fail(
                    CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
        }
        finally
        {
            // Only signal if still current — stale executions must not wake up the new waiter
            if (_executionId == myId)
                _resetEvent.Set();
        }
    }

    public void PrepareExecution(ICortexTool tool, JObject input, CortexSession session)
    {
        _executionId++;
        PendingTool = tool;
        PendingInput = input;
        PendingSession = session;
        Result = null;
        _resetEvent.Reset();
    }

    public bool WaitForCompletion(int timeoutMs = 120000)
    {
        return _resetEvent.WaitOne(timeoutMs);
    }

    public string GetName() => "RevitCortex Tool Execution";
}

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
    private readonly object _stateLock = new object();
    private int _executionId;
    private bool _hasPendingOrRunning;

    public ICortexTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public CortexSession? PendingSession { get; set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        int myId;
        ICortexTool? tool;
        JObject? input;
        CortexSession? session;

        lock (_stateLock)
        {
            myId = _executionId;
            tool = PendingTool;
            input = PendingInput;
            session = PendingSession;
        }

        try
        {
            if (tool == null || input == null || session == null)
            {
                if (_executionId == myId)
                    Result = CortexResult<object>.Fail(
                        CortexErrorCode.Unknown, "No pending tool execution");
                return;
            }

            var result = tool.Execute(input, session);
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
            lock (_stateLock)
            {
                if (_executionId == myId)
                {
                    PendingTool = null;
                    PendingInput = null;
                    PendingSession = null;
                    _hasPendingOrRunning = false;
                    _resetEvent.Set();
                }
            }
        }
    }

    public bool TryPrepareExecution(ICortexTool tool, JObject input, CortexSession session)
    {
        lock (_stateLock)
        {
            if (_hasPendingOrRunning)
                return false;

            _executionId++;
            PendingTool = tool;
            PendingInput = input;
            PendingSession = session;
            Result = null;
            _hasPendingOrRunning = true;
            _resetEvent.Reset();
            return true;
        }
    }

    public bool WaitForCompletion(int timeoutMs = 120000)
    {
        return _resetEvent.WaitOne(timeoutMs);
    }

    public void ClearPreparedExecution()
    {
        lock (_stateLock)
        {
            PendingTool = null;
            PendingInput = null;
            PendingSession = null;
            _hasPendingOrRunning = false;
            _resetEvent.Set();
        }
    }

    public string GetName() => "RevitCortex Tool Execution";
}

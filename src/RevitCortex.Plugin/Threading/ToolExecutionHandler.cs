using System;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
    private readonly object _stateLock = new object();
    private readonly AuditLogger _auditLogger;
    private int _executionId;
    private bool _hasPendingOrRunning;

    public ToolExecutionHandler(AuditLogger? auditLogger = null)
    {
        _auditLogger = auditLogger ?? new AuditLogger();
    }

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

        var discarded = false;

        try
        {
            if (tool == null || input == null || session == null)
            {
                // Stale Raise: the state was cleared by a timeout and no new request
                // has been prepared. Never touch Result here — overwriting it could
                // clobber the response of a request that just completed but whose
                // dispatcher has not read Result yet.
                return;
            }

            var result = tool.Execute(input, session);
            lock (_stateLock)
            {
                // Only store the result if this execution is still current
                // (not superseded by a timeout + new prepare).
                if (_executionId == myId)
                    Result = result;
                else
                    discarded = true;
            }

            if (discarded)
            {
                // The caller already received Timeout, but the tool ran to completion:
                // the model may differ from what the caller observed. Record the
                // divergence — the audit log is the source of truth.
                _auditLogger.LogWithPerf(tool.Name,
                    "completed_after_timeout (result discarded; model may have changed)",
                    result.Success, result.Error?.Code,
                    errorMessage: result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                if (_executionId == myId)
                    Result = CortexResult<object>.Fail(
                        CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
            }
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

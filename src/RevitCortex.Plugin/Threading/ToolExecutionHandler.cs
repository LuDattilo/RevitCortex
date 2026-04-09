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

    public ICortexTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public CortexSession? PendingSession { get; set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        try
        {
            if (PendingTool == null || PendingInput == null || PendingSession == null)
            {
                Result = CortexResult<object>.Fail(
                    CortexErrorCode.Unknown, "No pending tool execution");
                return;
            }

            Result = PendingTool.Execute(PendingInput, PendingSession);
        }
        catch (Exception ex)
        {
            Result = CortexResult<object>.Fail(
                CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
        }
        finally
        {
            _resetEvent.Set();
        }
    }

    public void PrepareExecution(ICortexTool tool, JObject input, CortexSession session)
    {
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

using System.IO;
using Xunit;

namespace RevitCortex.Tests.Threading;

/// <summary>
/// Source-text assertions for the post-timeout race fixes in the ExternalEvent
/// marshaling (audit 2026-06-11). ToolExecutionHandler implements
/// IExternalEventHandler (needs RevitAPIUI), so the contract is locked at source
/// level; the behavioral test remains [RequiresRevitApiFact]-gated.
///
/// Fixed races:
/// 1. A stale Raise (state cleared by timeout, no new request prepared) used to
///    overwrite Result with Fail("No pending tool execution"), clobbering the
///    response of a request that completed but had not been read yet.
/// 2. Result was assigned outside _stateLock after an unlocked _executionId read,
///    so a concurrent timeout+prepare could interleave between check and write.
/// 3. A result discarded by the generation guard (tool completed after the
///    dispatcher timeout) was dropped with no trace — the model had changed but
///    the caller saw Timeout. It is now recorded in the audit log.
/// </summary>
public class ThreadingRaceFixSourceTests
{
    private static string ReadPlugin(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", "RevitCortex.Plugin" };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void StaleRaise_NeverWritesResult()
    {
        var src = ReadPlugin("Threading", "ToolExecutionHandler.cs");
        Assert.DoesNotContain("No pending tool execution", src);
        Assert.Contains("// Stale Raise", src);
    }

    [Fact]
    public void ResultAssignments_HappenUnderTheStateLock()
    {
        var src = ReadPlugin("Threading", "ToolExecutionHandler.cs");
        // Every generation-guard check must read _executionId under _stateLock.
        // The old form was a bare `if (_executionId == myId)` directly inside
        // try/catch, outside any lock.
        var body = src.Substring(src.IndexOf("public void Execute", System.StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf("public bool TryPrepareExecution", System.StringComparison.Ordinal));
        var idx = 0;
        var checks = 0;
        while ((idx = body.IndexOf("_executionId == myId", idx, System.StringComparison.Ordinal)) >= 0)
        {
            var lockIdx = body.LastIndexOf("lock (_stateLock)", idx, System.StringComparison.Ordinal);
            Assert.True(lockIdx >= 0 && idx - lockIdx < 200,
                "each _executionId check in Execute must sit inside a lock (_stateLock) block");
            checks++;
            idx++;
        }
        Assert.True(checks >= 3, $"expected at least 3 locked generation checks, found {checks}");
    }

    [Fact]
    public void DiscardedResult_IsRecordedInTheAuditLog()
    {
        var src = ReadPlugin("Threading", "ToolExecutionHandler.cs");
        Assert.Contains("completed_after_timeout", src);
        Assert.Contains("AuditLogger", src);
    }

    [Fact]
    public void TimeoutSuggestion_WarnsTheOperationMayStillComplete()
    {
        var src = ReadPlugin("Threading", "RevitThreadDispatcher.cs");
        Assert.Contains("may still complete", src);
    }
}

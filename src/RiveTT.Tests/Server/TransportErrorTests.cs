using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// A transport failure must arrive as data, not as an exception.
///
/// An escaping exception is rendered by the MCP host as "An error occurred
/// invoking '&lt;tool&gt;'", which reads as "this tool is broken". A past session
/// spent several diagnostic cycles on paths, caches and document state chasing
/// what that message was hiding.
/// </summary>
public class TransportErrorTests
{
    [Theory]
    [InlineData("NoRevitSession")]
    public void MissingSession_IsNamedAndActionable(string expectedCode)
    {
        var error = TransportError.Describe(
            "get_project_info",
            new InvalidOperationException("No RiveTT Revit session is available."),
            300);

        Assert.False(error["success"]!.Value<bool>());
        var detail = error["error"]!;
        Assert.Equal(expectedCode, detail["code"]!.Value<string>());
        Assert.Equal("get_project_info", detail["tool"]!.Value<string>());
        Assert.Equal("transport", detail["stage"]!.Value<string>());
        // Nothing reached Revit, so no transaction ran: a retry is safe.
        Assert.False(detail["modelChanged"]!.Value<bool>());
        Assert.False(string.IsNullOrWhiteSpace(detail["suggestion"]!.Value<string>()));
    }

    [Fact]
    public void Timeout_QuotesTheBudgetThatExpired()
    {
        var error = TransportError.Describe("workflow_model_audit", new TimeoutException("Timed out."), 120);

        Assert.Equal("Timeout", error["error"]!["code"]!.Value<string>());
        Assert.Contains("120s", error["error"]!["suggestion"]!.Value<string>());
    }

    [Fact]
    public void ClosedPipe_TellsTheCallerToRecheckTheSession()
    {
        var error = TransportError.Describe("save_document", new IOException("pipe closed"), 300);

        Assert.Equal("PipeClosed", error["error"]!["code"]!.Value<string>());
        Assert.Contains("get_project_info", error["error"]!["suggestion"]!.Value<string>());
    }

    [Fact]
    public void UnknownFailure_StillCarriesACodeAndTheAuditLogPath()
    {
        var error = TransportError.Describe("create_wall", new Exception("boom"), 300);

        Assert.Equal("TransportFailure", error["error"]!["code"]!.Value<string>());
        Assert.Contains("audit.jsonl", error["error"]!["suggestion"]!.Value<string>());
        Assert.Equal("boom", error["error"]!["message"]!.Value<string>());
    }

    [Fact]
    public async Task Cancellation_StillPropagates()
    {
        // A cancelled call is not a transport failure — the host must see the
        // cancellation, not a result object claiming an error.
        var manager = new RevitConnectionManager();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.ExecuteAsync("get_project_info", new JObject(), cancelled.Token));
    }
}

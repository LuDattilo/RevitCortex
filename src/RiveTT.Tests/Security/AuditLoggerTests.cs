using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using Xunit;

namespace RiveTT.Tests.Security;

public class AuditLoggerTests : IDisposable
{
    private readonly string _tempPath;
    private readonly AuditLogger _logger;

    public AuditLoggerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"cortex_audit_test_{Guid.NewGuid()}.jsonl");
        _logger = new AuditLogger(_tempPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public void Log_Success_WritesJsonLine()
    {
        _logger.Log("get_element_parameters", "ids=[123]", true);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"tool\":\"get_element_parameters\"", content);
        Assert.Contains("\"result\":\"ok\"", content);
        Assert.Contains("\"input_summary\":\"ids=[123]\"", content);
    }

    [Fact]
    public void Log_Failure_IncludesErrorCode()
    {
        _logger.Log("delete_element", "ids=[456]", false,
            errorCode: CortexErrorCode.Cancelled);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"result\":\"fail\"", content);
        Assert.Contains("\"error_code\":\"Cancelled\"", content);
    }

    [Fact]
    public void Log_Multiple_AppendsLines()
    {
        _logger.Log("tool_a", "first", true);
        _logger.Log("tool_b", "second", true);

        var lines = File.ReadAllLines(_tempPath);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Log_TruncatesLongInput()
    {
        var longInput = new string('x', 600);
        _logger.Log("test_tool", longInput, true);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("...", content);
        // Should not contain the full 600-char string
        Assert.DoesNotContain(longInput, content);
    }

    [Fact]
    public void LogWithPerf_EmitsSchemaVersion2()
    {
        _logger.LogWithPerf("get_element_parameters", "ids=[123]", true,
            durationMs: 123, responseBytes: 4567);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"v\":2", content);
        Assert.Contains("\"duration_ms\":123", content);
        Assert.Contains("\"response_bytes\":4567", content);
    }

    [Fact]
    public void LogWithPerf_RecordsOutputSummaryAndAffectedCount()
    {
        _logger.LogWithPerf("batch_modify_parameter_values", "scope=selection", true,
            elementsAffected: 103, outputSummary: "modified=103, skipped=0");

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"elements_affected\":103", content);
        Assert.Contains("\"output_summary\":\"modified=103, skipped=0\"", content);
    }

    [Fact]
    public void LogWithPerf_WithCodeHashAndSnippet_PreservesBothFields()
    {
        _logger.LogWithPerf("send_code_to_revit", "code(42 chars)", false,
            errorCode: CortexErrorCode.PermissionDenied,
            codeSnippet: "var doc = document; // some revit code",
            codeHash: "abc123def456");

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"code_snippet\":\"var doc = document; // some revit code\"", content);
        Assert.Contains("\"code_hash\":\"abc123def456\"", content);
    }

    [Fact]
    public void Log_LegacyOverload_DoesNotEmitV2Fields()
    {
        _logger.Log("old_tool", "x", true);

        var content = File.ReadAllText(_tempPath);
        Assert.DoesNotContain("\"v\":2", content);
        Assert.DoesNotContain("duration_ms", content);
        Assert.DoesNotContain("response_bytes", content);
    }

    [Fact]
    public void LogWithPerf_Failure_IncludesErrorMessage()
    {
        _logger.LogWithPerf("batch_modify_parameter_values", "(no params)", false,
            errorCode: CortexErrorCode.Unknown,
            errorMessage: "Failed: Object reference not set to an instance of an object.");

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"error_code\":\"Unknown\"", content);
        Assert.Contains("\"error_message\":\"Failed: Object reference not set to an instance of an object.\"", content);
    }

    [Fact]
    public void LogWithPerf_Success_OmitsErrorMessage()
    {
        _logger.LogWithPerf("get_element_parameters", "ids=[1]", true,
            errorMessage: "ignored when success");

        var content = File.ReadAllText(_tempPath);
        Assert.DoesNotContain("error_message", content);
    }

    [Fact]
    public void LogWithPerf_TruncatesErrorMessageAt200Chars()
    {
        var longMessage = new string('e', 500);
        _logger.LogWithPerf("some_tool", "x", false,
            errorCode: CortexErrorCode.Unknown,
            errorMessage: longMessage);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("...", content);
        Assert.DoesNotContain(longMessage, content);
    }

    [Fact]
    public void Log_TransientSharingViolation_RetriesAndSucceeds()
    {
        // Two RiveTT sessions can share one audit.jsonl (P0.3 in
        // PLAN_CORRECTION.md). Hold an exclusive lock briefly, as a concurrent
        // writer would, and confirm the retry recovers instead of dropping
        // the entry silently.
        var lockHandle = new FileStream(_tempPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Run(() => { Thread.Sleep(40); lockHandle.Dispose(); });

        _logger.Log("locked_tool", "x", true);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("locked_tool", content);
        Assert.Equal(0, _logger.WriteFailureCount);
    }

    [Fact]
    public void Log_PermanentFailure_IncrementsCounterAndWritesVisibleFallback()
    {
        // P0.2: a silent Trace.WriteLine is invisible inside Revit.exe without a
        // debugger attached, which is how audit.jsonl stopped growing unnoticed
        // during the 2026-08-26 campaign. A write that can never succeed must
        // leave a trace a person can actually find.
        var dirAsFilePath = Path.Combine(Path.GetTempPath(), $"cortex_audit_dir_{Guid.NewGuid()}");
        Directory.CreateDirectory(dirAsFilePath);
        try
        {
            var logger = new AuditLogger(dirAsFilePath); // logPath IS a directory: every write fails
            logger.Log("doomed_tool", "x", true);

            Assert.Equal(1, logger.WriteFailureCount);
            var fallbackPath = dirAsFilePath + ".errors.log";
            Assert.True(File.Exists(fallbackPath));
            Assert.Contains("doomed_tool", File.ReadAllText(fallbackPath));
            File.Delete(fallbackPath);
        }
        finally
        {
            Directory.Delete(dirAsFilePath, true);
        }
    }

    [Fact]
    public void Log_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(),
            $"cortex_test_{Guid.NewGuid()}", "sub", "audit.jsonl");
        var logger = new AuditLogger(nestedPath);

        logger.Log("test", "test", true);

        Assert.True(File.Exists(nestedPath));

        // Cleanup
        var root = Path.GetDirectoryName(Path.GetDirectoryName(nestedPath));
        if (root != null) Directory.Delete(root, true);
    }
}

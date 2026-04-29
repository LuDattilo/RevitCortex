using System;
using System.IO;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

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
        // Tests that don't explicitly dispose _logger leave the writer
        // thread running — close it first so the file unlocks before we
        // try to delete it. Idempotent: safe even if a test already
        // disposed.
        try { _logger.Dispose(); } catch { }
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    // Async logger holds the file open for the writer thread; tests
    // must Dispose() the logger before File.ReadAll* so Windows
    // file-sharing semantics don't trip.

    [Fact]
    public void Log_Success_WritesJsonLine()
    {
        _logger.Log("get_element_parameters", "ids=[123]", true,
            durationMs: 17, responseBytes: 256);
        _logger.Dispose();

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"tool\":\"get_element_parameters\"", content);
        Assert.Contains("\"result\":\"ok\"", content);
        Assert.Contains("\"input_summary\":\"ids=[123]\"", content);
        Assert.Contains("\"v\":2", content);
        Assert.Contains("\"duration_ms\":17", content);
        Assert.Contains("\"response_bytes\":256", content);
    }

    [Fact]
    public void Log_Failure_IncludesErrorCode()
    {
        _logger.Log("delete_element", "ids=[456]", false,
            errorCode: CortexErrorCode.Cancelled);
        _logger.Dispose();

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("\"result\":\"fail\"", content);
        Assert.Contains("\"error_code\":\"Cancelled\"", content);
    }

    [Fact]
    public void Log_Multiple_AppendsLines()
    {
        _logger.Log("tool_a", "first", true);
        _logger.Log("tool_b", "second", true);
        _logger.Dispose();

        var lines = File.ReadAllLines(_tempPath);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Log_TruncatesLongInput()
    {
        var longInput = new string('x', 500);
        _logger.Log("test_tool", longInput, true);
        _logger.Dispose();

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("...", content);
        // Should not contain the full 500-char string
        Assert.DoesNotContain(longInput, content);
    }

    [Fact]
    public void Log_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(),
            $"cortex_test_{Guid.NewGuid()}", "sub", "audit.jsonl");
        using (var logger = new AuditLogger(nestedPath))
        {
            logger.Log("test", "test", true);
        }

        Assert.True(File.Exists(nestedPath));

        // Cleanup
        var root = Path.GetDirectoryName(Path.GetDirectoryName(nestedPath));
        if (root != null) Directory.Delete(root, true);
    }
}

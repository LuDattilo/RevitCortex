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
        var longInput = new string('x', 500);
        _logger.Log("test_tool", longInput, true);

        var content = File.ReadAllText(_tempPath);
        Assert.Contains("...", content);
        // Should not contain the full 500-char string
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

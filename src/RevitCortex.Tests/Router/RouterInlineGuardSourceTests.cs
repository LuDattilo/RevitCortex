using System.IO;
using Xunit;

namespace RevitCortex.Tests.Router;

/// <summary>
/// Source-text assertions for the inline UI-thread guard in CortexRouter (audit
/// 2026-06-11). The inline path runs a tool on the UI thread but OUTSIDE a Revit
/// API context (modeless WPF handlers like the Power BI export panel): a tool
/// that opens a Transaction there throws deep inside Revit. The router detected
/// the UI thread by thread-id only and executed ANY tool inline — safe today
/// only because push_to_powerbi happens to be transaction-free. The guard locks
/// that invariant: read-only tools plus an explicit allowlist.
/// The dispatcher==null branch (unit tests) is intentionally NOT guarded.
/// </summary>
public class RouterInlineGuardSourceTests
{
    private static string ReadRouter()
    {
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..", "RevitCortex.Plugin", "CortexRouter.cs")));
    }

    [Fact]
    public void InlinePath_RejectsWriteTools_OutsideTheAllowlist()
    {
        var src = ReadRouter();
        Assert.Contains("InlineUiThreadAllowedTools", src);
        Assert.Contains("onUiThread && !IsReadOnlyTool(toolName)", src);
        Assert.Contains("cannot run inline on the UI thread", src);
    }

    [Fact]
    public void Allowlist_ContainsTheVettedPowerBiTool()
    {
        var src = ReadRouter();
        // push_to_powerbi is the only production caller of the inline path
        // (PowerBiExportWindow); it opens no Transaction (verified at adoption).
        Assert.Contains("\"push_to_powerbi\"", src);
    }
}

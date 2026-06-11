using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the silent-drop cleanups of the 2026-06-11 audit:
/// workflow_room_documentation swallowed per-room view-creation failures in bare
/// `catch { }` (failed rooms vanished from the result), create_structural_framing_system
/// committed a BeamSystem while reporting a spacing that was never applied, and
/// create_room converted x/y from mm but treated z as raw feet.
/// </summary>
public class WorkflowDiagnosticsSourceTests
{
    private static string ReadTool(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", "RevitCortex.Tools" };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void RoomDocumentation_SurfacesViewCreationFailures()
    {
        var src = ReadTool("Workflows", "WorkflowRoomDocumentationTool.cs");
        Assert.Contains("failedCount = failures.Count", src);
        Assert.Contains("failures.Add(new { roomNumber", src);
        // Only the two best-effort view renames may keep a bare silent catch.
        var bareCatches = Regex.Matches(src, @"catch \{ \}");
        Assert.True(bareCatches.Count <= 2,
            $"expected at most 2 bare catches (view renames), found {bareCatches.Count}");
    }

    [Fact]
    public void FramingSystem_ReportsWhenLayoutWasNotApplied()
    {
        var src = ReadTool("Elements", "CreateStructuralFramingSystemTool.cs");
        Assert.Contains("layoutWarning", src);
        Assert.Contains("layoutApplied = layoutWarning == null", src);
        Assert.DoesNotContain("catch { /* layout/type assignment best-effort */ }", src);
    }

    [Fact]
    public void CreateRoom_ConvertsZFromMillimeters()
    {
        // Regression lock: z is converted on a separate line (`zFt /= MmPerFoot;`),
        // easy to miss in a narrow diff — the 2026-06-11 audit initially flagged it
        // as unconverted for exactly that reason.
        var src = ReadTool("Elements", "CreateRoomTool.cs");
        Assert.Contains("zFt /= MmPerFoot;", src);
    }
}

using System;
using System.IO;
using RevitCortex.Tools.Utilities;
using Xunit;

namespace RevitCortex.Tests.Security;

/// <summary>
/// Unit tests for PathSafety.TryResolveSafe — the allowlist gate every
/// caller-supplied file path must pass before tools read or write it.
/// PathSafety has no Revit dependencies, so these run as plain facts.
/// </summary>
public class PathSafetyTests
{
    [Fact]
    public void NullOrWhitespace_IsRejected()
    {
        Assert.False(PathSafety.TryResolveSafe(null, out _, out var err1));
        Assert.NotEmpty(err1);
        Assert.False(PathSafety.TryResolveSafe("", out _, out _));
        Assert.False(PathSafety.TryResolveSafe("   ", out _, out _));
    }

    [Fact]
    public void PathUnderTemp_IsAccepted()
    {
        var path = Path.Combine(Path.GetTempPath(), "revitcortex-test.csv");
        Assert.True(PathSafety.TryResolveSafe(path, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void PathUnderDocuments_IsAccepted()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, "export.xlsx");
        Assert.True(PathSafety.TryResolveSafe(path, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void SystemDirectory_IsRejected()
    {
        Assert.False(PathSafety.TryResolveSafe(@"C:\Windows\System32\config\SAM", out _, out var err));
        Assert.Contains("allowed directories", err);
    }

    [Fact]
    public void TraversalEscapingAllowedRoots_IsRejected()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        // Climb far enough to escape the drive-relative user profile entirely.
        var sneaky = Path.Combine(docs, @"..\..\..\..\..\..\Windows\win.ini");
        Assert.False(PathSafety.TryResolveSafe(sneaky, out _, out _));
    }

    [Fact]
    public void UncPath_IsRejected_ByDefault()
    {
        Assert.False(PathSafety.TryResolveSafe(@"\\server\share\model.rvt", out _, out var err));
        Assert.Contains("UNC", err);
    }

    [Fact]
    public void UncPath_IsAccepted_WhenAllowUnc()
    {
        Assert.True(PathSafety.TryResolveSafe(@"\\server\share\model.rvt", out var resolved, out _, allowUnc: true));
        Assert.Equal(@"\\server\share\model.rvt", resolved);
    }

    [Fact]
    public void ForwardSlashUncPath_IsAccepted_WhenAllowUnc()
    {
        Assert.True(PathSafety.TryResolveSafe("//server/share/links/grid.ifc", out var resolved, out _, allowUnc: true));
        Assert.StartsWith(@"\\server\share", resolved);
    }

    [Fact]
    public void AllowUnc_DoesNotRelaxLocalPaths()
    {
        // allowUnc widens the gate for network shares only; local paths outside
        // the user-owned roots must still be rejected.
        Assert.False(PathSafety.TryResolveSafe(@"C:\Windows\System32\config\SAM", out _, out _, allowUnc: true));
        Assert.False(PathSafety.TryResolveSafe(@"C:\ProgramData\Autodesk\secret.txt", out _, out _, allowUnc: true));
    }

    [Fact]
    public void AllowUnc_StillAcceptsLocalUserPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "linked.rvt");
        Assert.True(PathSafety.TryResolveSafe(path, out _, out _, allowUnc: true));
    }
}

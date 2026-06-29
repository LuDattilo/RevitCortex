using System.IO;
using Xunit;

namespace RevitCortex.Tests.Updates;

/// <summary>
/// Regression guard: the in-app updater must terminate the Revit *process* so the
/// elevated installer (which waits for Revit to close) can proceed without the user
/// closing Revit by hand. Revit is a native Win32 app, so Application.Current.Shutdown()
/// is a no-op there — the flow must post WM_CLOSE to the Revit main window instead.
/// Source-text test (the close path needs a live Revit, so it cannot run at runtime).
/// </summary>
public class UpdateCloseRevitSourceTests
{
    private static string ReadSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..",
            "RevitCortex.Plugin", "UI", "UpdateNotificationWindow.xaml.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void InstallFlow_ClosesRevitViaWmClose()
    {
        var src = ReadSource();
        Assert.Contains("PostMessage", src);
        Assert.Contains("WM_CLOSE", src);
        Assert.Contains("MainWindowHandle", src);
    }
}

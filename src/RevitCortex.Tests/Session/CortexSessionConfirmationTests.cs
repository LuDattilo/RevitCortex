using System;
using System.IO;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

/// <summary>
/// Characterization tests for the destructive-operation confirmation gate on
/// <see cref="CortexSession"/> — specifically the Auto mode flag shipped in v1.0.27.
/// Auto mode auto-approves indefinitely (no 120 s timeout like "Yes to All"),
/// is cleared by the user (Stop Auto) and on document boundary (Reinitialize).
/// </summary>
public class CortexSessionConfirmationTests
{
    private static CortexSession NewSession() => new CortexSession(new SessionStore());

    private static string ReadAutoModeWindowSource()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "UI", "AutoModeWindow.xaml.cs"));
        return File.ReadAllText(path);
    }

    // ── AutoMode auto-approval ───────────────────────────────────────────────

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_AutoApprovesWithoutInvokingCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };
        session.AutoMode = true;

        var result = session.RequestConfirmation("delete", 5);

        Assert.True(result);
        Assert.False(callbackInvoked); // Auto mode short-circuits before the dialog
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOff_InvokesCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return true; };

        session.RequestConfirmation("delete", 5);

        Assert.True(callbackInvoked);
    }

    // ── AutoMode has no timeout (unlike ApproveAll) ──────────────────────────

    [Fact]
    public void AutoMode_HasNoTimeout_StaysTrueWhenSet()
    {
        var session = NewSession();
        session.AutoMode = true;

        // ApproveAll expires after 120 s by wall clock; AutoMode must not.
        // It stays active until Stop Auto/window close or a document boundary.
        Assert.True(session.AutoMode);
    }

    // ── Activity signal (optional UI status hook) ────────────────────────────

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_FiresAutoModeActivity()
    {
        var session = NewSession();
        session.AutoMode = true;
        var activityCount = 0;
        session.AutoModeActivity += () => activityCount++;

        session.RequestConfirmation("delete", 5);
        session.RequestConfirmation("delete", 3);

        Assert.Equal(2, activityCount); // one signal per auto-approved op
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOff_DoesNotFireAutoModeActivity()
    {
        var session = NewSession();
        var activityFired = false;
        session.AutoModeActivity += () => activityFired = true;
        session.ConfirmAction = (_, _, _) => true; // plain Yes, Auto stays off

        session.RequestConfirmation("delete", 5);

        Assert.False(activityFired);
    }

    [Fact]
    public void AutoModeWindow_HasNoInactivityTimer()
    {
        var source = ReadAutoModeWindowSource();

        Assert.DoesNotContain("DispatcherTimer", source);
        Assert.DoesNotContain("InactivitySeconds", source);
        Assert.DoesNotContain("OnInactivityElapsed", source);
    }

    // ── Reset on document boundary ───────────────────────────────────────────

    [Fact]
    public void Reinitialize_ResetsAutoMode()
    {
        var session = NewSession();
        session.AutoMode = true;

        session.Reinitialize(new DocumentCapabilities(), "en");

        Assert.False(session.AutoMode);
    }

    [Fact]
    public void ResetApproveAll_ResetsBothApproveAllAndAutoMode()
    {
        var session = NewSession();
        session.ApproveAll = true;
        session.AutoMode = true;

        session.ResetApproveAll();

        Assert.False(session.ApproveAll);
        Assert.False(session.AutoMode);
    }

    // ── Interaction with ApproveAll ──────────────────────────────────────────

    [Fact]
    public void RequestConfirmation_WhenApproveAllOn_AutoApprovesWithoutCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };
        session.ApproveAll = true;

        var result = session.RequestConfirmation("purge", 3);

        Assert.True(result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_NullCallbackResult_TreatedAsYesToAll_SetsApproveAll()
    {
        var session = NewSession();
        // null return from the dialog encodes "Yes to All" per ConfirmationHelper convention.
        session.ConfirmAction = (_, _, _) => null;

        var result = session.RequestConfirmation("rename", 2);

        Assert.True(result);
        Assert.True(session.ApproveAll); // subsequent ops now auto-approved for 120 s
    }

    // ── Guard: nothing to confirm ────────────────────────────────────────────

    [Fact]
    public void RequestConfirmation_ZeroElements_ReturnsTrueWithoutCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };

        var result = session.RequestConfirmation("delete", 0);

        Assert.True(result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_NoCallbackSet_ProceedsWithoutBlocking()
    {
        var session = NewSession();
        // ConfirmAction left null — tools must not deadlock waiting for a dialog.

        var result = session.RequestConfirmation("delete", 10);

        Assert.True(result);
    }

    [Fact]
    public void RequestConfirmation_CallbackReturnsFalse_Cancels()
    {
        var session = NewSession();
        session.ConfirmAction = (_, _, _) => false;

        var result = session.RequestConfirmation("delete", 10);

        Assert.False(result);
    }
}

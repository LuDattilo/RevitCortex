using System;
using System.IO;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

/// <summary>
/// Characterization tests for destructive-operation confirmation gates.
/// </summary>
public class CortexSessionConfirmationTests
{
    private static CortexSession NewSession()
    {
        return new CortexSession(new SessionStore());
    }

    private static string ReadAutoModeWindowSource()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Plugin", "UI", "AutoModeWindow.xaml.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_AutoApprovesWithoutInvokingCallback()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ConfirmAction = (_, _, _) => { callbackInvoked = true; return false; };
        session.AutoMode = true;

        var result = session.RequestConfirmation("delete", 5);

        Assert.True(result);
        Assert.False(callbackInvoked);
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

    [Fact]
    public void AutoMode_HasNoTimeout_StaysTrueWhenSet()
    {
        var session = NewSession();
        session.AutoMode = true;

        Assert.True(session.AutoMode);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOn_FiresAutoModeActivity()
    {
        var session = NewSession();
        session.AutoMode = true;
        var activityCount = 0;
        session.AutoModeActivity += () => activityCount++;

        session.RequestConfirmation("delete", 5);
        session.RequestConfirmation("delete", 3);

        Assert.Equal(2, activityCount);
    }

    [Fact]
    public void RequestConfirmation_WhenAutoModeOff_DoesNotFireAutoModeActivity()
    {
        var session = NewSession();
        var activityFired = false;
        session.AutoModeActivity += () => activityFired = true;
        session.ConfirmAction = (_, _, _) => true;

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
        session.ConfirmAction = (_, _, _) => null;

        var result = session.RequestConfirmation("rename", 2);

        Assert.True(result);
        Assert.True(session.ApproveAll);
    }

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

    [Fact]
    public void RequestConfirmation_CriticalWithoutCallback_FailsClosed()
    {
        var session = NewSession();

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresApproveAll()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.ApproveAll = true;
        session.ConfirmAction = (_, _, _) =>
        {
            throw new InvalidOperationException("Normal callback must not handle critical confirmations.");
        };
        session.CriticalConfirmAction = (_, _, _) =>
        {
            callbackInvoked = true;
            return false;
        };

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresAutoMode()
    {
        var session = NewSession();
        var callbackInvoked = false;
        session.AutoMode = true;
        session.ConfirmAction = (_, _, _) =>
        {
            throw new InvalidOperationException("Normal callback must not handle critical confirmations.");
        };
        session.CriticalConfirmAction = (_, _, _) =>
        {
            callbackInvoked = true;
            return false;
        };

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void RequestConfirmation_CriticalNullCallbackResult_CancelsAndDoesNotArmApproveAll()
    {
        var session = NewSession();
        session.CriticalConfirmAction = (_, _, _) => null;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
        Assert.False(session.ApproveAll);
    }

    [Fact]
    public void RequestConfirmation_CriticalIgnoresNormalCallback()
    {
        var session = NewSession();
        session.ConfirmAction = (_, _, _) => true;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.False(result);
    }

    [Fact]
    public void RequestConfirmation_CriticalAllowsExplicitCriticalYes()
    {
        var session = NewSession();
        session.CriticalConfirmAction = (_, _, _) => true;

        var result = session.RequestConfirmation("execute C# script", 1, critical: true);

        Assert.True(result);
    }
}

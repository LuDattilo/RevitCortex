using RiveTT.Core.Discovery;
using Xunit;

namespace RiveTT.Tests.Discovery;

public class DocumentCapabilitiesTests
{
    [Fact]
    public void EnableTool_MakesToolAvailable()
    {
        var caps = new DocumentCapabilities();
        Assert.False(caps.IsToolEnabled("list_worksets"));
        caps.EnableTool("list_worksets");
        Assert.True(caps.IsToolEnabled("list_worksets"));
    }

    [Fact]
    public void DisableTool_RemovesTool()
    {
        var caps = new DocumentCapabilities();
        caps.EnableTool("list_worksets");
        caps.DisableTool("list_worksets");
        Assert.False(caps.IsToolEnabled("list_worksets"));
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var caps = new DocumentCapabilities();
        caps.HasWorksets = true;
        caps.HasPhases = true;
        caps.PresentCategories.Add("OST_Walls");
        caps.EnableTool("list_worksets");

        caps.Reset();

        Assert.False(caps.HasWorksets);
        Assert.False(caps.HasPhases);
        Assert.Empty(caps.PresentCategories);
        Assert.False(caps.IsToolEnabled("list_worksets"));
    }

    [Fact]
    public void EnabledTools_ReturnsReadonlyCollection()
    {
        var caps = new DocumentCapabilities();
        caps.EnableTool("a");
        caps.EnableTool("b");
        Assert.Equal(2, caps.EnabledTools.Count);
    }

    [Fact]
    public void KnownDynamicTools_IncludesManageWorksets()
    {
        Assert.Contains("manage_worksets", DocumentCapabilities.KnownDynamicToolNames);
    }
}

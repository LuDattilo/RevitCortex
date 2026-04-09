using RevitCortex.Core.Session;
using Xunit;

namespace RevitCortex.Tests.Session;

public class SessionStoreTests
{
    [Fact]
    public void Get_ReturnsNull_WhenKeyMissing()
    {
        var store = new SessionStore();
        Assert.Null(store.Get<string>("missing"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        var store = new SessionStore();
        store.Set("ids", new[] { 1, 2, 3 });
        var result = store.Get<int[]>("ids");
        Assert.NotNull(result);
        Assert.Equal(3, result!.Length);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var store = new SessionStore();
        store.Set("a", "val");
        store.Set("b", "val2");
        store.Clear();
        Assert.Null(store.Get<string>("a"));
        Assert.Null(store.Get<string>("b"));
    }

    [Fact]
    public void Remove_DeletesSingleKey()
    {
        var store = new SessionStore();
        store.Set("a", "val");
        store.Set("b", "val2");
        store.Remove("a");
        Assert.Null(store.Get<string>("a"));
        Assert.Equal("val2", store.Get<string>("b"));
    }

    [Fact]
    public void ContainsKey_ReturnsCorrectly()
    {
        var store = new SessionStore();
        Assert.False(store.ContainsKey("x"));
        store.Set("x", "y");
        Assert.True(store.ContainsKey("x"));
    }
}

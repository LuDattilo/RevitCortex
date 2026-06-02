using System.Reflection;
using Newtonsoft.Json.Linq;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

/// <summary>
/// Characterization tests pinning the EXACT output of CortexRouter.HashParams (internal static).
/// The hash is the cache key: any change to its output silently invalidates every cached entry
/// (a cross-version cache-drift regression). These golden values lock the current behavior so a
/// refactor of Canonicalize cannot change the produced hash. Reached via reflection because
/// HashParams is internal and the test assembly has no InternalsVisibleTo.
/// </summary>
public class CortexRouterHashStabilityTests
{
    private static string Hash(JObject input)
    {
        var m = typeof(CortexRouter).GetMethod("HashParams",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)m.Invoke(null, new object[] { input })!;
    }

    [Fact]
    public void Hash_EmptyObject_IsStable()
        => Assert.Equal(GOLDEN_EMPTY, Hash(new JObject()));

    [Fact]
    public void Hash_FlatObject_IsStable()
        => Assert.Equal(GOLDEN_FLAT, Hash(new JObject { ["b"] = 2, ["a"] = "x", ["c"] = true }));

    [Fact]
    public void Hash_NestedObjectAndArray_IsStable()
        => Assert.Equal(GOLDEN_NESTED, Hash(new JObject
        {
            ["arr"] = new JArray { 3, 1, new JObject { ["z"] = 1, ["y"] = 2 } },
            ["obj"] = new JObject { ["k"] = "v", ["n"] = 42 },
            ["flag"] = false
        }));

    [Fact]
    public void Hash_KeyOrderInvariant()
    {
        var a = new JObject { ["alpha"] = 1, ["beta"] = new JObject { ["p"] = 1, ["q"] = 2 } };
        var b = new JObject { ["beta"] = new JObject { ["q"] = 2, ["p"] = 1 }, ["alpha"] = 1 };
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void Hash_ArrayOrderSensitive()
    {
        // Arrays are order-significant (canonicalization preserves array order).
        var a = new JObject { ["arr"] = new JArray { 1, 2, 3 } };
        var b = new JObject { ["arr"] = new JArray { 3, 2, 1 } };
        Assert.NotEqual(Hash(a), Hash(b));
    }

    // Golden values captured from the pre-refactor implementation (SHA-256 of the
    // canonical, key-sorted, whitespace-free JSON). Locking these guarantees a
    // Canonicalize refactor produces byte-identical cache keys.
    private const string GOLDEN_EMPTY = "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a";
    private const string GOLDEN_FLAT = "2e6c6b7e3652ea85c432e0cfcff6dc1409948f6efe93177553c1899c7d0772a7";
    private const string GOLDEN_NESTED = "3594eb5e55fd2c73754f9d0263e24b7e63b82a65f47bf580d9086e73bc1264c3";
}

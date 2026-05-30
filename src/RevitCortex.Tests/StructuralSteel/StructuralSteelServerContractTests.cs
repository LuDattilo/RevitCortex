using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

public class StructuralSteelServerContractTests
{
    private static MethodInfo M(string name) =>
        Assert.Single(typeof(StructuralSteelTools).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == name);

    [Fact]
    public void Capabilities_HasRevitAndCtOnly()
    {
        var m = M(nameof(StructuralSteelTools.GetStructuralSteelApiCapabilities));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n), n => Assert.Equal("ct", n));
        Assert.NotNull(m.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void ListHandlers_ExposesMaxResultsAndSummaryOnly()
    {
        var m = M(nameof(StructuralSteelTools.ListSteelConnectionHandlers));
        var names = m.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("maxResults", names);
        Assert.Contains("summaryOnly", names);
    }

    [Fact]
    public void GetSteelElementProperties_ExposesElementId()
    {
        var m = M(nameof(StructuralSteelTools.GetSteelElementProperties));
        Assert.Contains("elementId", m.GetParameters().Select(p => p.Name));
    }
}

using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Rebar;

public class RebarServerContractTests
{
    private static MethodInfo GetMethod(string name) =>
        Assert.Single(typeof(RebarTools).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == name);

    [Fact]
    public void ListRebarBarTypes_HasRevitAndCt()
    {
        var m = GetMethod(nameof(RebarTools.ListRebarBarTypes));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n),
            n => Assert.Equal("ct", n));
        Assert.Equal(typeof(RevitConnectionManager), m.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), m.GetParameters()[1].ParameterType);
        Assert.NotNull(m.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void GetRebarHostData_ExposesHostId()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarHostData));
        Assert.Contains("hostId", m.GetParameters().Select(p => p.Name));
        Assert.Equal(typeof(long), Assert.Single(m.GetParameters(), p => p.Name == "hostId").ParameterType);
    }

    [Fact]
    public void GetRebarGeometry_ExposesBarPositionIndex()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarGeometry));
        Assert.Contains("rebarId", m.GetParameters().Select(p => p.Name));
        Assert.Contains("barPositionIndex", m.GetParameters().Select(p => p.Name));
    }

    [Fact]
    public void GetRebarApiCapabilities_HasRevitAndCtOnly()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarApiCapabilities));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n),
            n => Assert.Equal("ct", n));
    }

    [Fact]
    public void SetRebarVarying_ExposesRebarIdAndEnabled()
    {
        var m = GetMethod(nameof(RebarTools.SetRebarVarying));
        var ps = m.GetParameters();
        Assert.Equal(typeof(RevitConnectionManager), ps[0].ParameterType);
        Assert.Contains("rebarId", ps.Select(p => p.Name));
        Assert.Equal(typeof(long), Assert.Single(ps, p => p.Name == "rebarId").ParameterType);
        // 'enabled' is the varying toggle and must be required (non-nullable bool).
        var enabled = Assert.Single(ps, p => p.Name == "enabled");
        Assert.Equal(typeof(bool), enabled.ParameterType);
        Assert.NotNull(m.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void GetRebarVaryingData_ExposesRebarId()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarVaryingData));
        Assert.Contains("rebarId", m.GetParameters().Select(p => p.Name));
        Assert.Equal(typeof(long), Assert.Single(m.GetParameters(), p => p.Name == "rebarId").ParameterType);
    }
}

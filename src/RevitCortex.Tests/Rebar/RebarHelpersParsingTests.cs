using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Rebar;
using Xunit;

namespace RevitCortex.Tests.Rebar;

public class RebarHelpersParsingTests
{
    [Fact]
    public void ToMm_And_FromMm_RoundTrip()
    {
        Assert.Equal(304.8, RebarToolHelpers.ToMm(1.0), 6);
        Assert.Equal(1.0, RebarToolHelpers.FromMm(304.8), 6);
    }

    [Fact]
    public void ParseLayoutSpec_FixedNumber_Parses()
    {
        var json = JObject.Parse(@"{""rule"":""fixed_number"",""number"":10,""arrayLengthMm"":1500,
            ""barsOnNormalSide"":true,""includeFirstBar"":true,""includeLastBar"":false}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(RebarToolHelpers.LayoutRuleKind.FixedNumber, spec.Rule);
        Assert.Equal(10, spec.Number);
        Assert.Equal(1500, spec.ArrayLengthMm, 6);
        Assert.True(spec.BarsOnNormalSide);
        Assert.True(spec.IncludeFirstBar);
        Assert.False(spec.IncludeLastBar);
    }

    [Fact]
    public void ParseLayoutSpec_UnknownRule_ReturnsError()
    {
        var json = JObject.Parse(@"{""rule"":""banana""}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("rule", err);
    }

    [Fact]
    public void ParseEnum_Valid_And_Invalid()
    {
        var ok = RebarToolHelpers.ParseEnum<RebarLayoutKindProbe>("FixedNumber", "rule", out var err1);
        Assert.Null(err1);
        Assert.Equal(RebarLayoutKindProbe.FixedNumber, ok);

        RebarToolHelpers.ParseEnum<RebarLayoutKindProbe>("nope", "rule", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("rule", err2);
        Assert.Contains("Single", err2);
    }

    [Fact]
    public void ParseEnum_EmptyString_ReturnsRequiredError()
    {
        RebarToolHelpers.ParseEnum<RebarLayoutKindProbe>("", "rule", out var err);
        Assert.NotNull(err);
        Assert.Contains("required", err);
    }
}

public enum RebarLayoutKindProbe { Single, FixedNumber }

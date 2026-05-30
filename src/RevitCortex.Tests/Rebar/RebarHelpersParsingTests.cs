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
    public void ParseLayoutSpec_FixedNumber_MissingArrayLength_ReturnsError()
    {
        // The exact failure cluster from the live test: fixed_number / maximum_spacing without
        // arrayLengthMm used to reach Revit and throw an opaque "arrayLength isn't acceptable".
        var json = JObject.Parse(@"{""rule"":""fixed_number"",""number"":4}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("arrayLengthMm", err);
    }

    [Fact]
    public void ParseLayoutSpec_MaximumSpacing_MissingArrayLength_ReturnsError()
    {
        var json = JObject.Parse(@"{""rule"":""maximum_spacing"",""spacingMm"":300}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("arrayLengthMm", err);
    }

    [Fact]
    public void ParseLayoutSpec_MaximumSpacing_MissingSpacing_ReturnsError()
    {
        var json = JObject.Parse(@"{""rule"":""maximum_spacing"",""arrayLengthMm"":3000}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("spacingMm", err);
    }

    [Fact]
    public void ParseLayoutSpec_NumberWithSpacing_MissingSpacing_ReturnsError()
    {
        var json = JObject.Parse(@"{""rule"":""number_with_spacing"",""number"":5}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("spacingMm", err);
    }

    [Fact]
    public void ParseLayoutSpec_Single_NoNumericInputs_NoError()
    {
        var json = JObject.Parse(@"{""rule"":""single""}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(RebarToolHelpers.LayoutRuleKind.Single, spec.Rule);
    }

    [Fact]
    public void ParseLayoutSpec_MaximumSpacing_Valid_NoError()
    {
        var json = JObject.Parse(@"{""rule"":""maximum_spacing"",""spacingMm"":200,""arrayLengthMm"":3000}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(200, spec.SpacingMm, 6);
        Assert.Equal(3000, spec.ArrayLengthMm, 6);
    }

    [Fact]
    public void ParseLayoutSpec_FixedNumber_One_IsAccepted()
    {
        // Revit's documented range for numberOfBarPositions is 1..1002 — a single-position set is
        // valid. The validator must NOT reject number==1 (an earlier >=2 guard was wrong).
        var json = JObject.Parse(@"{""rule"":""fixed_number"",""number"":1,""arrayLengthMm"":1500}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(1, spec.Number);
    }

    [Fact]
    public void ParseLayoutSpec_FixedNumber_Over1002_ReturnsError()
    {
        // Documented upper bound: numberOfBarPositions <= 1002.
        var json = JObject.Parse(@"{""rule"":""fixed_number"",""number"":1003,""arrayLengthMm"":1500}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("1002", err);
    }

    [Fact]
    public void ParseLayoutSpec_NumberWithSpacing_One_IsAccepted()
    {
        var json = JObject.Parse(@"{""rule"":""number_with_spacing"",""number"":1,""spacingMm"":200}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(1, spec.Number);
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

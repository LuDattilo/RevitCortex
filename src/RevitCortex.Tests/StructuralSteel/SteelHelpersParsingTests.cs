using System;
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.StructuralSteel;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

public class SteelHelpersParsingTests
{
    [Fact]
    public void ToMm_And_FromMm_RoundTrip()
    {
        Assert.Equal(304.8, StructuralSteelToolHelpers.ToMm(1.0), 6);
        Assert.Equal(1.0, StructuralSteelToolHelpers.FromMm(304.8), 6);
    }

    [Fact]
    public void ParseGuid_Valid_And_Invalid()
    {
        var ok = StructuralSteelToolHelpers.ParseGuid("00000000-0000-0000-0000-000000000000", "fabricationGuid", out var err1);
        Assert.Null(err1);
        Assert.Equal(Guid.Empty, ok);
        StructuralSteelToolHelpers.ParseGuid("not-a-guid", "fabricationGuid", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("fabricationGuid", err2);
    }

    [Fact]
    public void ParseEnum_Valid_And_Invalid()
    {
        var ok = StructuralSteelToolHelpers.ParseEnum<SteelInputActionProbe>("AddElementIds", "action", out var err1);
        Assert.Null(err1);
        Assert.Equal(SteelInputActionProbe.AddElementIds, ok);
        StructuralSteelToolHelpers.ParseEnum<SteelInputActionProbe>("nope", "action", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("action", err2);
        Assert.Contains("AddElementIds", err2);
    }

    [Fact]
    public void ParseConnectionInputAction_MapsSnakeCase()
    {
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.AddElementIds,
            StructuralSteelToolHelpers.ParseConnectionInputAction("add_element_ids", out var e1)); Assert.Null(e1);
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.RemoveReferences,
            StructuralSteelToolHelpers.ParseConnectionInputAction("remove_references", out var e2)); Assert.Null(e2);
        StructuralSteelToolHelpers.ParseConnectionInputAction("banana", out var e3);
        Assert.NotNull(e3);
        Assert.Contains("action", e3);
    }

    [Fact]
    public void ParseLongArray_ReadsNumbers_AndRejectsNonNumeric()
    {
        var ids = StructuralSteelToolHelpers.ParseLongArray((JArray)JArray.Parse("[12345, 12346]"), "elementIds", out var err);
        Assert.Null(err);
        Assert.Equal(new long[] { 12345, 12346 }, ids);
        StructuralSteelToolHelpers.ParseLongArray((JArray)JArray.Parse("[\"x\"]"), "elementIds", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("elementIds", err2);
    }

    [Fact]
    public void ParseLongArray_EmptyArray_ReturnsEmptyNoError()
    {
        var ids = StructuralSteelToolHelpers.ParseLongArray(new JArray(), "elementIds", out var err);
        Assert.Null(err);
        Assert.Empty(ids);
    }

    [Fact]
    public void ParseConnectionInputAction_RemoveElementIds_Maps()
    {
        var a = StructuralSteelToolHelpers.ParseConnectionInputAction("remove_element_ids", out var err);
        Assert.Null(err);
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.RemoveElementIds, a);
    }

    [Fact]
    public void ParseConnectionInputAction_AddReferences_Maps()
    {
        var a = StructuralSteelToolHelpers.ParseConnectionInputAction("add_references", out var err);
        Assert.Null(err);
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.AddReferences, a);
    }
}

public enum SteelInputActionProbe { AddElementIds, RemoveElementIds }

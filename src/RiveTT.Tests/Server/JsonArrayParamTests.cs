using System.Text.Json;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// The JsonElement overload exists because a caller — human or model — naturally
/// sends a native JSON array for a parameter whose description says "JSON array,
/// e.g. [...]", and every affected tool is typed to receive exactly that shape
/// now. Measured live on 27/08: get_element_parameters(parameterNames:["Number"])
/// failed with the host's generic "An error occurred invoking" error before this
/// fix, because the parameter was declared string and a JSON array cannot bind to
/// a C# string. See the P1.4 addendum in PLAN_CORRECTION.md.
/// </summary>
public class JsonArrayParamTests
{
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void JsonElement_NativeArrayOfStrings_Parses()
    {
        var ok = JsonArrayParam.TryParse(Element("[\"Number\",\"Name\"]"), out var parsed);

        Assert.True(ok);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Number", parsed[0]!.ToString());
        Assert.Equal("Name", parsed[1]!.ToString());
    }

    [Fact]
    public void JsonElement_NativeArrayOfSingleElement_Parses()
    {
        var ok = JsonArrayParam.TryParse(Element("[\"Number\"]"), out var parsed);

        Assert.True(ok);
        Assert.Single(parsed);
        Assert.Equal("Number", parsed[0]!.ToString());
    }

    [Fact]
    public void JsonElement_NativeArrayOfNumbers_Parses()
    {
        var ok = JsonArrayParam.TryParse(Element("[1,2,3]"), out var parsed);

        Assert.True(ok);
        Assert.Equal(3, parsed.Count);
        Assert.Equal(1, (int)parsed[0]!);
    }

    [Fact]
    public void JsonElement_EmptyNativeArray_IsNotProvided()
    {
        var ok = JsonArrayParam.TryParse(Element("[]"), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_JsonEncodedStringArray_StillParses()
    {
        // The pre-existing fix: an optional array param typed as a JSON-encoded
        // string. Must keep working — this is what most callers that read the
        // description literally as "pass a string" already send correctly.
        var ok = JsonArrayParam.TryParse(Element("\"[\\\"Walls\\\",\\\"Doors\\\"]\""), out var parsed);

        Assert.True(ok);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Walls", parsed[0]!.ToString());
    }

    [Fact]
    public void JsonElement_BareScalarString_BecomesSingleElementArray()
    {
        var ok = JsonArrayParam.TryParse(Element("\"Walls\""), out var parsed);

        Assert.True(ok);
        Assert.Single(parsed);
        Assert.Equal("Walls", parsed[0]!.ToString());
    }

    [Fact]
    public void JsonElement_CommaSeparatedString_SplitsIntoArray()
    {
        var ok = JsonArrayParam.TryParse(Element("\"Walls,Doors\""), out var parsed);

        Assert.True(ok);
        Assert.Equal(2, parsed.Count);
    }

    [Fact]
    public void JsonElement_BareNumber_BecomesSingleElementArray()
    {
        var ok = JsonArrayParam.TryParse(Element("42"), out var parsed);

        Assert.True(ok);
        Assert.Single(parsed);
        Assert.Equal(42, (int)parsed[0]!);
    }

    [Fact]
    public void JsonElement_Null_IsNotProvided()
    {
        JsonElement? value = null;
        var ok = JsonArrayParam.TryParse(value, out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_JsonNullLiteral_IsNotProvided()
    {
        var ok = JsonArrayParam.TryParse(Element("null"), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_NativeArrayOfObjects_Parses()
    {
        // The multi-rule/multi-condition tools (create_view_filter.rules,
        // filter_by_parameter_value.conditions, ...) pass arrays of objects,
        // not scalars.
        var ok = JsonArrayParam.TryParse(
            Element("[{\"parameterName\":\"Mark\",\"rule\":\"equals\",\"value\":\"P1\"}]"),
            out var parsed);

        Assert.True(ok);
        Assert.Single(parsed);
        Assert.Equal("Mark", parsed[0]!["parameterName"]!.ToString());
    }

    [Fact]
    public void InvalidArrayResult_JsonElementOverload_ReportsTheReceivedValue()
    {
        var result = JsonArrayParam.InvalidArrayResult("get_element_parameters", "parameterNames", Element("\"not an array\""));

        Assert.Contains("parameterNames", result);
        Assert.Contains("InvalidInput", result);
    }
}

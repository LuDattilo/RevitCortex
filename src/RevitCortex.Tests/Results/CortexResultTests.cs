using RevitCortex.Core.Results;
using Newtonsoft.Json;
using Xunit;

namespace RevitCortex.Tests.Results;

public class CortexResultTests
{
    [Fact]
    public void Ok_SetsSuccessTrue_AndDataPopulated()
    {
        var result = CortexResult<string>.Ok("hello");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_SetsSuccessFalse_AndErrorPopulated()
    {
        var result = CortexResult<string>.Fail(
            CortexErrorCode.ElementNotFound,
            "Element 123 not found",
            suggestion: "Check element ID");
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
        Assert.Equal(CortexErrorCode.ElementNotFound, result.Error!.Code);
        Assert.Equal("Element 123 not found", result.Error.Message);
        Assert.Equal("Check element ID", result.Error.Suggestion);
    }

    [Fact]
    public void Fail_WithContext_SerializesCorrectly()
    {
        var ctx = new System.Collections.Generic.Dictionary<string, object>
        {
            { "elementId", 606873 }
        };
        var result = CortexResult<object>.Fail(
            CortexErrorCode.ElementNotFound, "Not found", context: ctx);
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"elementId\":606873", json);
        Assert.Contains("\"code\":\"ElementNotFound\"", json);
    }

    [Fact]
    public void Ok_SerializesWithoutErrorField()
    {
        var result = CortexResult<int>.Ok(42);
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"data\":42", json);
        Assert.DoesNotContain("\"error\"", json);
    }
}

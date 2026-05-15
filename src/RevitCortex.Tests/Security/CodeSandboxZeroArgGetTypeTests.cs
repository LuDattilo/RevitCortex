using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

/// <summary>
/// Regression: the .GetType(\S) bypass rule must not catch zero-arg
/// `obj.GetType()` which is a harmless runtime-type check.
/// </summary>
public class CodeSandboxZeroArgGetTypeTests
{
    [Fact]
    public void Validate_ZeroArgGetType_OnInstance_Allowed()
    {
        var code = "var t = wall.GetType();";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_ZeroArgGetType_WithSpaces_Allowed()
    {
        var code = "var t = wall . GetType ( );";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_ZeroArgGetType_OnTypeofExpression_Allowed()
    {
        // typeof(Wall).Name etc. — but here just GetType on something
        var code = "var n = element.GetType().Name;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_GetTypeWithStringArg_StillBlocked()
    {
        // Regression in the opposite direction: ensure the real bypass remains blocked.
        var code = "var asm = typeof(string).Assembly; var t = asm.GetType(\"System.IO.File\");";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_ZeroArgGetMethod_NotAValidCSharpCall_StillAllowed()
    {
        // GetMethod() with no args isn't legal C# (it requires a name), but be safe:
        // make sure we don't reject zero-arg reflection accessor patterns either.
        var code = "var m = type.GetMethod();";
        Assert.Null(CodeSandboxV2.Validate(code));
    }
}

using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

/// <summary>
/// Hardening regressions for the sandbox bypasses found in the 2026-06-04 ultrareview
/// (CodeSandboxV2, criticals C1-C3). Each "Blocked" test encodes a payload that previously
/// passed <see cref="CodeSandboxV2.Validate"/>; each "StillAllowed" test guards a benign
/// pattern that must keep working so the fix introduces no false positives.
/// </summary>
public class CodeSandboxHardeningTests
{
    // --- C2: whitespace between namespace segments defeated the substring check ---

    // Uses System.Runtime.InteropServices / Marshal: a prohibited namespace with NO method
    // pattern, so ONLY the namespace substring check can catch it — isolating the C2 hole.
    [Fact]
    public void Validate_WhitespaceInProhibitedNamespace_Blocked()
    {
        var code = "var h = System . Runtime . InteropServices . Marshal . AllocHGlobal(8);";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_NewlineInProhibitedNamespace_Blocked()
    {
        var code = "var h = System.\n    Runtime.\n    InteropServices.\n    Marshal.AllocHGlobal(8);";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    // --- C1: zero-arg plural reflection enumerators were not matched by any pattern ---

    [Fact]
    public void Validate_GetTypesEnumerator_Blocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate("var ts = asm.GetTypes();"));
    }

    [Fact]
    public void Validate_GetMethodsEnumerator_Blocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate("var ms = t.GetMethods();"));
    }

    // --- C1: .Invoke() on a local variable evaded the case-sensitive MethodInfo.Invoke literal ---

    [Fact]
    public void Validate_VariableInvoke_Blocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate("m.Invoke(null, new object[]{1});"));
    }

    // --- C1/C3: Assembly acquisition is the entry point for a reflection walk ---

    [Fact]
    public void Validate_GetExecutingAssembly_Blocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate("var a = Assembly.GetExecutingAssembly();"));
    }

    // --- C3: environment information disclosure was not blocked ---

    [Fact]
    public void Validate_EnvironmentGetEnvironmentVariable_Blocked()
    {
        Assert.NotNull(CodeSandboxV2.Validate("var p = Environment.GetEnvironmentVariable(\"PATH\");"));
    }

    // --- C1: the full realistic payload (arbitrary file write via reflection) must be blocked ---

    [Fact]
    public void Validate_FullReflectionFileWriteBypass_Blocked()
    {
        var code =
            "var t = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()" +
            ".First(x => x.FullName == \"System.IO.File\");" +
            "var m = t.GetMethods().First(x => x.Name == \"WriteAllText\");" +
            "m.Invoke(null, new object[]{ @\"C:\\evil.txt\", \"payload\" });";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    // --- Regression guards: legitimate Revit code must stay allowed ---

    [Fact]
    public void Validate_SingularGetType_StillAllowed()
    {
        Assert.Null(CodeSandboxV2.Validate("var t = wall.GetType();"));
    }

    [Fact]
    public void Validate_SingularGetTypeWithName_StillAllowed()
    {
        Assert.Null(CodeSandboxV2.Validate("var n = element.GetType().Name;"));
    }

    [Fact]
    public void Validate_RevitCollectorCount_StillAllowed()
    {
        var code = "var n = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_RevitParameterLookup_StillAllowed()
    {
        var code = "var p = wall.LookupParameter(\"Comments\"); p.Set(\"hello\");";
        Assert.Null(CodeSandboxV2.Validate(code));
    }
}

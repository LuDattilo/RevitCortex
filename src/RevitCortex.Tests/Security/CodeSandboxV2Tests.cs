using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

public class CodeSandboxV2Tests
{
    [Fact]
    public void Validate_CommentMentionsProhibited_NotBlocked()
    {
        var code = "// using System.IO would be dangerous\nvar x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_StringLiteralMentionsProhibited_NotBlocked()
    {
        var code = "var note = \"System.IO is off-limits\"; var x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_BlockCommentMentionsProhibited_NotBlocked()
    {
        var code = "/* WebClient is prohibited */ var x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_VerbatimStringMentionsProhibited_NotBlocked()
    {
        var code = "var note = @\"File.Delete is scary\"; var x = 1;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_RealNamespaceUse_Blocked()
    {
        var code = "var f = System.IO.File.ReadAllText(\"x\");";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_TypeGetTypeReflectionBypass_Blocked()
    {
        var code = "var t = Type.GetType(\"System.IO.File\");";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_ActivatorCreateInstance_Blocked()
    {
        var code = "var obj = Activator.CreateInstance(someType);";
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_MethodInfoInvoke_Blocked()
    {
        var code = "var r = method.Invoke(null, null); var t = MethodInfo.Invoke;";
        // The explicit token 'MethodInfo.Invoke' is what we look for
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void Validate_StripPreservesLineNumbers()
    {
        var input = "line1\n/* block\ncomment */\nline4";
        var output = CodeSandboxV2.StripCommentsAndStrings(input);
        Assert.Equal(4, output.Split('\n').Length);
    }

    [Fact]
    public void Validate_SafeRevitApiCode_Allowed()
    {
        var code = @"
            var collector = new FilteredElementCollector(document);
            var walls = collector.OfClass(typeof(Wall)).ToList();
            return walls.Count;";
        Assert.Null(CodeSandboxV2.Validate(code));
    }
}

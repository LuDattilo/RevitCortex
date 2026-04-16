using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using Xunit;

namespace RevitCortex.Tests.Security;

public class CodeSandboxTests
{
    [Fact]
    public void Validate_SafeCode_ReturnsNull()
    {
        var code = @"
            var collector = new FilteredElementCollector(document);
            var walls = collector.OfClass(typeof(Wall)).ToList();
            return walls.Count;";

        Assert.Null(CodeSandbox.Validate(code));
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(@\"C:\\secret.txt\")")]
    [InlineData("using System.IO; File.Delete(\"model.rvt\");")]
    [InlineData("var client = new System.Net.WebClient();")]
    [InlineData("System.Diagnostics.Process.Start(\"cmd.exe\");")]
    [InlineData("Microsoft.Win32.Registry.LocalMachine.OpenSubKey(\"key\");")]
    [InlineData("System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly();")]
    [InlineData("System.Runtime.InteropServices.Marshal.AllocHGlobal(1024);")]
    public void Validate_ProhibitedNamespace_ReturnsPermissionDenied(string code)
    {
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public void Validate_ProcessStart_WithoutNamespace_Blocked()
    {
        var code = "Process.Start(\"calc.exe\");";
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void Validate_FileReadWrite_WithoutNamespace_Blocked()
    {
        var code = "File.ReadAllText(\"passwords.txt\");";
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void Validate_HttpClient_Blocked()
    {
        var code = "var http = new HttpClient(); http.GetAsync(\"http://evil.com\");";
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void Validate_EnvironmentExit_Blocked()
    {
        var code = "Environment.Exit(0);";
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void Validate_AssemblyLoad_Blocked()
    {
        var code = "Assembly.LoadFrom(\"malicious.dll\");";
        var result = CodeSandbox.Validate(code);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public void Validate_EmptyCode_ReturnsNull()
    {
        Assert.Null(CodeSandbox.Validate(""));
        Assert.Null(CodeSandbox.Validate("   "));
    }

    [Fact]
    public void Validate_RevitApiCode_Safe()
    {
        var code = @"
            using (var tx = new Transaction(document, ""Test""))
            {
                tx.Start();
                var wall = Wall.Create(document, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)),
                    new ElementId(311), new ElementId(0), 10.0, 0.0, false, false);
                tx.Commit();
            }";

        Assert.Null(CodeSandbox.Validate(code));
    }

    // ── Regression tests for V2 (post-review) ─────────────────────────────

    [Fact]
    public void Validate_CommentMentionsProhibited_Allowed()
    {
        // V1 would have blocked this — V2 strips the comment before matching
        var code = "// System.IO is prohibited in this project\nreturn document.Title;";
        Assert.Null(CodeSandbox.Validate(code));
    }

    [Fact]
    public void Validate_StringLiteralMentionsProhibited_Allowed()
    {
        var code = "var note = \"do not use System.IO\"; return note;";
        Assert.Null(CodeSandbox.Validate(code));
    }

    [Fact]
    public void Validate_TypeGetTypeReflectionBypass_Blocked()
    {
        var code = "var t = Type.GetType(\"System.IO.File\"); t.GetMethod(\"Delete\");";
        Assert.NotNull(CodeSandbox.Validate(code));
    }

    [Fact]
    public void Validate_ActivatorBypass_Blocked()
    {
        var code = "var obj = Activator.CreateInstance(someType);";
        Assert.NotNull(CodeSandbox.Validate(code));
    }
}

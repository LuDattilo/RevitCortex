using RevitCortex.Core.Security;
using Xunit;
using Xunit.Abstractions;

namespace RevitCortex.Tests.Security;

/// <summary>
/// Exploratory tests trying creative bypasses of CodeSandboxV2.
/// Goal: discover what passes the sandbox today so we can decide if any
/// patterns warrant a new rule.
/// Tests are NOT assertions of desired behavior — they document current behavior.
/// </summary>
public class CodeSandboxBypassExplorationTests
{
    private readonly ITestOutputHelper _out;
    public CodeSandboxBypassExplorationTests(ITestOutputHelper output) => _out = output;

    private void Report(string label, string code)
    {
        var result = CodeSandboxV2.Validate(code);
        var status = result == null ? "ALLOWED" : "BLOCKED: " + result.Error!.Message;
        _out.WriteLine($"[{label}] {status}");
    }

    [Fact] public void Probe_AssemblyGetType_BypassReflectionGuard() =>
        Report("Assembly.GetType",
            "var asm = typeof(string).Assembly; var t = asm.GetType(\"System.IO.File\");");

    [Fact] public void Probe_TypeofThenReflection() =>
        Report("typeof.GetMethod",
            "var m = typeof(System.Environment).GetMethod(\"Exit\"); m.Invoke(null, new object[]{1});");

    [Fact] public void Probe_DynamicKeyword() =>
        Report("dynamic.invoke",
            "dynamic x = somefile; x.WriteAllText(\"path\", \"data\");");

    [Fact] public void Probe_StringConcatNamespace() =>
        Report("string concat NS",
            "var ns = \"System\" + \".\" + \"IO\" + \".File\"; var t = Type.GetType(ns);");

    [Fact] public void Probe_SystemIOFullyQualifiedSpaced() =>
        Report("System . IO . File spaced",
            "var f = System . IO . File . ReadAllText(\"x\");");

    [Fact] public void Probe_GlobalPrefix() =>
        Report("global:: prefix",
            "var f = global::System.IO.File.ReadAllText(\"x\");");

    [Fact] public void Probe_PInvokeNative() =>
        Report("DllImport native",
            "[System.Runtime.InteropServices.DllImport(\"kernel32.dll\")] static extern void DoStuff();");

    [Fact] public void Probe_UnsafePointers() =>
        Report("unsafe pointer",
            "unsafe { int* p = stackalloc int[10]; }");

    [Fact] public void Probe_ProcessStartViaBaseProperty() =>
        Report("Process.GetProcessById then Kill",
            "var p = System.Diagnostics.Process.GetProcessById(1); p.Kill();");

    [Fact] public void Probe_PathClass() =>
        Report("Path.GetTempPath",
            "var t = System.IO.Path.GetTempPath();");

    [Fact] public void Probe_StreamWriter() =>
        Report("new StreamWriter",
            "var w = new System.IO.StreamWriter(\"x\"); w.Write(\"data\");");

    [Fact] public void Probe_FileInfoIndirect() =>
        Report("new FileInfo",
            "var fi = new System.IO.FileInfo(\"x\"); var r = fi.OpenRead();");
}

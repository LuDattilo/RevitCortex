using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Back-compat shim. Delegates to CodeSandboxV2, which strips comments/strings
/// before pattern matching and catches reflection-based bypasses.
/// New callers should prefer CodeSandboxV2 directly.
/// </summary>
public static class CodeSandbox
{
    public static CortexResult<object>? Validate(string code) => CodeSandboxV2.Validate(code);
}

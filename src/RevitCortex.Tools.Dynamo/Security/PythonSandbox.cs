using RevitCortex.Core.Results;
using RevitCortex.Core.Security;

namespace RevitCortex.Tools.Dynamo.Security
{
    /// <summary>
    /// Validates generated Python before it is written into a .dyn. Reuses the same
    /// namespace blocklist as send_code_to_revit (CodeSandboxV2). Returns null when clean.
    /// Note: this guards the automated AI->generate->run channel only; a user who opens
    /// the .dyn by hand in Dynamo can still run anything.
    /// </summary>
    public static class PythonSandbox
    {
        public static CortexResult<object>? Validate(string pythonCode)
            => CodeSandbox.Validate(pythonCode ?? "");
    }
}

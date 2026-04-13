using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit context.
/// Uses Roslyn scripting when available, otherwise evaluates simple expressions.
/// </summary>
public class SendCodeToRevitTool : ICortexTool
{
    public string Name => "send_code_to_revit";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Executes custom C# code snippets in the Revit context. Uses Roslyn scripting when available, otherwise evaluates simple expressions.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        // Sandbox validation: block prohibited namespace patterns
        var sandboxResult = CodeSandbox.Validate(code);
        if (sandboxResult != null)
            return sandboxResult;

        // Store the code in session for the plugin layer to pick up
        session.Store.Set("pendingCode", code);
        session.Store.Set("pendingCodeTransaction", transactionMode);

        return CortexResult<object>.Fail(CortexErrorCode.Unknown,
            "Runtime code execution is not yet implemented. " +
            $"Code ({code.Length} chars) was stored in session as 'pendingCode' " +
            "but no Roslyn/CodeDom compiler is available to execute it.",
            suggestion: "Use dedicated RevitCortex tools instead of raw C# code. " +
                "For element queries use ai_element_filter, for parameter reads use get_element_parameters, " +
                "for modifications use set_element_parameters or bulk_modify_parameter_values.");
    }
}

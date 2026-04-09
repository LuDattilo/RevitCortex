using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;

namespace RevitCortex.Core.Tools;

public interface ICortexTool
{
    /// <summary>Tool name as exposed via MCP (e.g., "get_element_parameters").</summary>
    string Name { get; }

    /// <summary>Domain category for organization (e.g., "Elements", "Views").</summary>
    string Category { get; }

    /// <summary>If true, tool requires an open Revit document.</summary>
    bool RequiresDocument { get; }

    /// <summary>If true, tool is only visible when DocumentCapabilities enables it.</summary>
    bool IsDynamic { get; }

    /// <summary>Execute the tool with the given input and session context.</summary>
    CortexResult<object> Execute(JObject input, CortexSession session);
}

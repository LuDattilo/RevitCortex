using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class InteropTools
{
    [McpServerTool(Name = "cross_app_selection"),
     Description("Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume CortexElementRefs and select/isolate them via show_cross_model_elements composition. Resolution priority: revitUniqueId → ifcGuid → revitElementId.")]
    public static async Task<string> CrossAppSelection(
        RevitConnectionManager revit,
        [Description("Mode: \"export\" or \"import\".")]
        string mode,
        [Description("Import-only: array of CortexElementRef objects produced by an export call (this app or Navis).")]
        JArray? refs = null,
        [Description("Import-only: when true, append to current selection instead of replacing it. Default false.")]
        bool? append = null,
        [Description("Import-only: isolate the resolved elements in the active view. Default true.")]
        bool? isolate = null,
        [Description("Import-only: create a section box framing the resolved elements. Default true.")]
        bool? createSectionBox = null,
        [Description("Import-only: place red DirectShape markers on linked-element matches. Default true.")]
        bool? createLinkedMarkers = null,
        [Description("Import-only: use a post-command isolate flow (slower but more compatible). Default false.")]
        bool? usePostCommandIsolate = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["mode"] = mode };
        if (refs != null) p["refs"] = refs;
        if (append != null) p["append"] = append;
        if (isolate != null) p["isolate"] = isolate;
        if (createSectionBox != null) p["createSectionBox"] = createSectionBox;
        if (createLinkedMarkers != null) p["createLinkedMarkers"] = createLinkedMarkers;
        if (usePostCommandIsolate != null) p["usePostCommandIsolate"] = usePostCommandIsolate;

        var result = await revit.ExecuteAsync("cross_app_selection", p, ct);
        return result.ToString();
    }
}

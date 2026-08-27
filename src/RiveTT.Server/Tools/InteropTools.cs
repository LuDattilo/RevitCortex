using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class InteropTools
{
    [McpServerTool(Name = "sync_navisworks_selection"),
     Description("Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume CortexElementRefs and select/isolate them via show_cross_model_elements composition. Resolution priority: revitUniqueId → ifcGuid → revitElementId.")]
    public static async Task<string> CrossAppSelection(
        RevitConnectionManager revit,
        [Description("Mode: \"export\" or \"import\".")]
        string mode,
        [Description("Import-only: array of CortexElementRef objects produced by an export call (this app or Navis).")]
        JArray? refs = null,
        [Description("Import-only: when true, append to current selection instead of replacing it. Default false.")]
        bool append = false,
        [Description("Import-only: isolate the resolved elements in the active view. Default true.")]
        bool isolate = true,
        [Description("Import-only: create a section box framing the resolved elements. Default true.")]
        bool createSectionBox = true,
        [Description("Import-only: place red DirectShape markers on linked-element matches. Default true.")]
        bool createLinkedMarkers = true,
        [Description("Import-only: use a post-command isolate flow (slower but more compatible). Default false.")]
        bool usePostCommandIsolate = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["mode"] = mode };
        if (refs != null) p["refs"] = refs;
        p["append"] = append;
        p["isolate"] = isolate;
        p["createSectionBox"] = createSectionBox;
        p["createLinkedMarkers"] = createLinkedMarkers;
        p["usePostCommandIsolate"] = usePostCommandIsolate;

        var result = await revit.ExecuteAsync("sync_navisworks_selection", p, ct);
        return result.ToString();
    }
}

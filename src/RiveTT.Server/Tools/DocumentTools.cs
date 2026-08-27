using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "save_document"), Description("Save the active Revit project at its current path. dryRun reports the path, the unsaved-changes state and any predictable blocker without writing.")]
    public static async Task<string> SaveDocument(
        RevitConnectionManager revit,
        [Description("Preview without saving. Default: false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var request = new JObject();
        request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("save_document", request, ct)).ToString();
    }

    [McpServerTool(Name = "save_as_document"), Description("Save the active Revit project to an absolute .rvt path (parameter name: targetPath). This DUPLICATES the open document - it does not create a blank project from a template. dryRun reports source path, target path, overwrite policy and blockers without writing. Every cached read is flushed on success, so a follow-up read returns the new path.")]
    public static async Task<string> SaveAsDocument(
        RevitConnectionManager revit,
        [Description("Absolute output .rvt path")] string? targetPath = null,
        [Description("Replace an existing file. Default false")] bool overwrite = false,
        [Description("Preview without saving. Default: false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        // targetPath is optional at the schema level on purpose: a missing or
        // misnamed path must come back as a structured InvalidInput naming the
        // expected parameter, not as an opaque "error invoking save_as_document".
        var request = new JObject();
        if (targetPath != null) request["targetPath"] = targetPath;
        request["overwrite"] = overwrite;
        request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("save_as_document", request, ct)).ToString();
    }

    [McpServerTool(Name = "open_family"), Description("Opens a .rfa family file and makes it the active document in Revit, for visual editing (type parameters, geometry). The active document CHANGES - every later tool call targets the family until you switch back with open_document. The family stays open: call close_document when done, or it accumulates for the rest of the session. To load a family INTO the current project instead, use load_family.")]
    public static async Task<string> OpenFamily(
        RevitConnectionManager revit,
        [Description("Absolute .rfa path")] string filePath,
        [Description("Preview without opening. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var request = new JObject { ["filePath"] = filePath, ["dryRun"] = dryRun };
        return (await revit.ExecuteAsync("open_family", request, ct)).ToString();
    }

    [McpServerTool(Name = "open_template"), Description("Opens a .rte template file and makes it the active document in Revit, to edit the TEMPLATE itself (levels, types, view templates). To start a new PROJECT from a template instead, use create_document - that reads the template without touching it. The active document changes: every later tool call targets the template until you switch back.")]
    public static async Task<string> OpenTemplate(
        RevitConnectionManager revit,
        [Description("Absolute .rte path")] string filePath,
        [Description("Preview without opening. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var request = new JObject { ["filePath"] = filePath, ["dryRun"] = dryRun };
        return (await revit.ExecuteAsync("open_template", request, ct)).ToString();
    }

    [McpServerTool(Name = "close_document"), Description("Closes an open document (project, family, or template). Defaults to the active document; pass filePath to close a different one open in the background. saveModified controls whether unsaved changes are saved first (default: false, discarded). Closing the ACTIVE document requires another open document to switch to first - if none is open, the call is refused rather than guessed at.")]
    public static async Task<string> CloseDocument(
        RevitConnectionManager revit,
        [Description("Absolute path of the open document to close. Omit to close the active document")] string? filePath = null,
        [Description("Save unsaved changes before closing. Default: false (discarded)")] bool saveModified = false,
        [Description("Preview without closing. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var request = new JObject { ["saveModified"] = saveModified, ["dryRun"] = dryRun };
        if (filePath != null) request["filePath"] = filePath;
        return (await revit.ExecuteAsync("close_document", request, ct)).ToString();
    }
}

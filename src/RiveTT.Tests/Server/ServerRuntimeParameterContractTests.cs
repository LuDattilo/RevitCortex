using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// Guards the contract between the two halves of the connector: every parameter the
/// MCP surface publishes must be READ by the runtime tool it is forwarded to.
///
/// This is the class of bug that cost the most in the field. create_sheet published
/// titleBlockId while CreateSheetTool read titleBlockTypeId, so every sheet came out
/// as a bare 210x297 mm sheet with no frame — no error, no warning, and no way to
/// produce a real presentation sheet at all. export_schedule published `format` and
/// read `delimiter`; export_shared_parameter_file published `outputPath` and read
/// `filePath`; get_current_view_elements published `categoryFilter` and read nothing
/// of the sort. A published parameter that nothing reads is worse than a missing
/// one: the caller believes it took effect.
///
/// The check is source-level on purpose. Reflection cannot see which JSON keys a
/// method writes into its request object, and a runtime check would need a live
/// Revit session.
/// </summary>
public class ServerRuntimeParameterContractTests
{
    private static readonly Regex McpToolAttribute =
        new(@"\[McpServerTool\(Name\s*=\s*""([a-z0-9_]+)""\)", RegexOptions.Compiled);

    /// <summary>Keys written into a request JObject: ["key"] = ...</summary>
    private static readonly Regex RequestKeyWrite =
        new(@"\[\s*""([A-Za-z0-9_]+)""\s*\]\s*=", RegexOptions.Compiled);

    /// <summary>The runtime tool a wrapper forwards to.</summary>
    private static readonly Regex ExecuteTarget =
        new(@"ExecuteAsync\(\s*""([a-z0-9_]+)""", RegexOptions.Compiled);

    private static readonly Regex RuntimeToolName =
        new(@"public\s+string\s+Name\s*=>\s*""([a-z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>Any indexer read of a JSON key: token["key"].</summary>
    private static readonly Regex KeyRead =
        new(@"\w+\s*\[\s*""([A-Za-z0-9_]+)""\s*\]", RegexOptions.Compiled);

    /// <summary>Helper-style read: Apply(input, "key", ...).</summary>
    private static readonly Regex HelperKeyRead =
        new(@"input\s*,\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>Envelope keys that are structural, not tool parameters.</summary>
    private static readonly HashSet<string> StructuralKeys =
        new(StringComparer.Ordinal) { "data", "dryRun" };

    /// <summary>
    /// Parameters that are deliberately consumed by the MCP server itself and never
    /// sent to Revit. Each one needs a reason; anything else is a bug.
    /// </summary>
    private static readonly Dictionary<string, string> ClientSideOnly = new(StringComparer.Ordinal)
    {
        ["list_coordination_models.compact"] = "response-shaping flag applied by ToolResponseShaper, never forwarded"
    };

    private static string RepoPath(params string[] parts)
    {
        var all = new List<string> { "..", "..", "..", ".." };
        all.AddRange(parts);
        return Path.GetFullPath(Path.Combine(all.ToArray()));
    }

    private static IEnumerable<string> CsFiles(params string[] parts)
    {
        var root = RepoPath(parts);
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();
    }

    [Fact]
    public void EveryPublishedParameter_IsReadByTheRuntimeToolItIsSentTo()
    {
        // ── runtime side: which keys each tool's source reads, plus shared helpers
        var readsByFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var fileByToolName = new Dictionary<string, string>(StringComparer.Ordinal);
        var helperReads = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in CsFiles("RiveTT.Tools"))
        {
            var source = File.ReadAllText(file);
            var reads = new HashSet<string>(
                KeyRead.Matches(source).Select(m => m.Groups[1].Value)
                    .Concat(HelperKeyRead.Matches(source).Select(m => m.Groups[1].Value)),
                StringComparer.Ordinal);

            readsByFile[file] = reads;

            var toolNames = RuntimeToolName.Matches(source).Select(m => m.Groups[1].Value).ToList();
            foreach (var name in toolNames)
                fileByToolName[name] = file;

            // A file with no ICortexTool is a shared helper (ElementScopeResolver,
            // TransactionFailureHandling, CurveSpecHelpers...) and its reads count
            // for every tool that delegates to it.
            var fileName = Path.GetFileName(file);
            if (toolNames.Count == 0 || fileName.Contains("Helper") ||
                fileName.Contains("Resolver") || fileName.Contains("Handling"))
            {
                helperReads.UnionWith(reads);
            }
        }

        Assert.NotEmpty(fileByToolName);

        // ── MCP side: which keys each wrapper sends, and to which runtime tools
        var offenders = new List<string>();

        foreach (var file in CsFiles("RiveTT.Server", "Tools"))
        {
            var source = File.ReadAllText(file);
            var sections = McpToolAttribute.Split(source);

            // sections[0] is the preamble, then (name, body) pairs.
            for (var i = 1; i < sections.Length - 1; i += 2)
            {
                var toolName = sections[i];
                var body = sections[i + 1];

                var sentKeys = RequestKeyWrite.Matches(body)
                    .Select(m => m.Groups[1].Value)
                    .Where(key => !StructuralKeys.Contains(key))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (sentKeys.Count == 0) continue;

                // A typed alias (create_wall, create_door...) forwards to a generic
                // runtime tool, so its keys must be read THERE.
                var targets = ExecuteTarget.Matches(body)
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (targets.Count == 0) targets.Add(toolName);

                var allowed = new HashSet<string>(helperReads, StringComparer.Ordinal);
                var resolvedAny = false;
                foreach (var target in targets)
                {
                    if (!fileByToolName.TryGetValue(target, out var targetFile)) continue;
                    resolvedAny = true;
                    allowed.UnionWith(readsByFile[targetFile]);
                }

                // No runtime counterpart at all is the job of ToolCatalogParitySourceTests.
                if (!resolvedAny) continue;

                foreach (var key in sentKeys)
                {
                    if (allowed.Contains(key)) continue;
                    if (ClientSideOnly.ContainsKey($"{toolName}.{key}")) continue;
                    offenders.Add($"{toolName}.{key} (forwarded to {string.Join("/", targets)}, read by none of them)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "MCP parameters that no runtime tool reads — they would be silently ignored:\n  " +
            string.Join("\n  ", offenders.OrderBy(text => text, StringComparer.Ordinal)));
    }

    [Fact]
    public void ClientSideOnlyWaivers_AllCarryAReason()
    {
        Assert.All(ClientSideOnly, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
    }

    [Fact]
    public void CreateSheet_ForwardsTitleBlockIdAndTheRuntimeReadsIt()
    {
        // The specific regression: a sheet with no title block is unusable as a
        // presentation sheet, and nothing in the response said so.
        var wrapper = File.ReadAllText(RepoPath("RiveTT.Server", "Tools", "ViewTools.cs"));
        var runtime = File.ReadAllText(RepoPath("RiveTT.Tools", "Project", "CreateSheetTool.cs"));

        Assert.Contains("p[\"titleBlockId\"] = titleBlockId", wrapper);
        Assert.Contains("input[\"titleBlockId\"]", runtime);
        // And the response must state what was actually placed.
        Assert.Contains("hasTitleBlock", runtime);
    }

    [Fact]
    public void ExportElementsData_SupportsExplicitElementIdsBeforePagination()
    {
        var runtime = File.ReadAllText(RepoPath("RiveTT.Tools", "Elements", "ExportElementsDataTool.cs"));

        Assert.Contains("input[\"elementIds\"]", runtime);
        Assert.Contains("CollectById", runtime);
        // Ids must be honored before the 100-element truncation, otherwise asking
        // for one element returns the first 100 elements of the model.
        var idIndex = runtime.IndexOf("CollectById", StringComparison.Ordinal);
        var truncateIndex = runtime.IndexOf("elements.Take(maxElements)", StringComparison.Ordinal);
        Assert.True(idIndex > 0 && truncateIndex > idIndex);
    }

    [Fact]
    public void SaveAsDocument_PreviewsAndNamesItsOwnParameter()
    {
        var runtime = File.ReadAllText(RepoPath("RiveTT.Tools", "Project", "DocumentLifecycleTools.cs"));

        // A misnamed path must produce a structured InvalidInput naming targetPath,
        // not an opaque "error occurred invoking save_as_document".
        Assert.Contains("targetPath is required", runtime);
        Assert.Contains("input[\"filePath\"]", runtime);
        Assert.Contains("dryRun", runtime);
    }

    [Fact]
    public void OpeningADocument_AnswersRevitDialogsAndReportsThem()
    {
        var lifecycle = ReadRepo("RiveTT.Tools", "Project", "DocumentCreationTools.cs");
        var answer = ReadRepo("RiveTT.Tools", "Project", "OpenDialogAutoAnswer.cs");

        // A modal dialog during the open blocks the UI thread, which is the thread
        // the ExternalEvent runs on: the pipe waits for a human to click. Opening the
        // sandbox model raised "Revit could not find or read 1 references".
        Assert.Contains("DialogBoxShowing", answer);
        Assert.Contains("OverrideResult", answer);
        Assert.Contains("new OpenDialogAutoAnswer(uiApplication)", lifecycle);
        // Answering on the caller's behalf must never be invisible.
        Assert.Contains("dismissedDialogs", lifecycle);
    }

    private static string ReadRepo(string project, params string[] parts)
    {
        var all = new List<string> { "..", "..", "..", "..", project };
        all.AddRange(parts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(all.ToArray())));
    }

    [Fact]
    public void DeletingAGroupMember_IsReportedAsAnExclusion()
    {
        var runtime = ReadRepo("RiveTT.Tools", "Elements", "DeleteElementTool.cs");
        var groups = ReadRepo("RiveTT.Tools", "Elements", "EditGroupMembersTool.cs");
        var inventory = ReadRepo("RiveTT.Tools", "Elements", "ManageModelGroupsTool.cs");

        // Deleting a group member is Revit's EXCLUSION, a first-class feature:
        // measured on a real model, one instance kept 26 members while a sibling kept
        // 27, both under the same type with the same instance count — and Revit renamed
        // the instance "(membre exclu)". It must be reported, not refused.
        Assert.Contains("groupExclusionIds", runtime);
        Assert.Contains("Restore Excluded Members", runtime);
        Assert.DoesNotContain("allowGroupMemberDeletion", runtime);
        // Deleting a whole group instance is not a member exclusion.
        Assert.Contains("element is Group", runtime);

        // Removing members goes through exclusion (type preserved); only ADDING needs
        // the ungroup/regroup rebuild.
        Assert.Contains("exclusionOnly", groups);
        Assert.Contains("typeRecreated = false", groups);

        // Instances of one type may legitimately differ, so the inventory reports each
        // instance rather than assuming the first one holds the full definition.
        Assert.Contains("instancesWithExclusions", inventory);
        Assert.Contains("hasExcludedMembers", inventory);
    }

    [Fact]
    public void PlacingAViewport_ReportsItsFootprintAndWhetherItFits()
    {
        var runtime = ReadRepo("RiveTT.Tools", "Views", "PlaceViewportTool.cs");

        // An uncropped view produces a viewport far larger than the sheet, and its
        // drawing then lands outside the frame — visible only by opening the sheet.
        // The tool must report the footprint instead of leaving it to be discovered.
        Assert.Contains("GetBoxOutline", runtime);
        Assert.Contains("fitsOnSheet", runtime);
        Assert.Contains("viewportOutlineMm", runtime);
        // Omitting the position must centre the viewport rather than pile everything
        // into the bottom-left corner.
        Assert.Contains("centreOnSheet", runtime);
        // The frame reference is the title block box: the sheet origin is not the
        // frame corner (the French A1 title block sits 650 mm inside it), so a
        // position derived from the sheet size lands off the paper.
        Assert.Contains("frameOutlineMm", runtime);

        // That measurement now lives in SheetFrame, shared with batch_create_sheets and
        // workflow_sheet_set — which is the point: this tool held the correct version
        // while the other two carried a broken clone. The guarantee is unchanged, so the
        // assertion follows it to its new home instead of pinning the copy back here.
        Assert.Contains("SheetFrame.Measure(doc, sheet)", runtime);
        Assert.Contains("OST_TitleBlocks",
            ReadRepo("RiveTT.Tools", "Utilities", "SheetFrame.cs"));
    }

    [Fact]
    public void SaveAs_InvalidatesEveryCachedRead()
    {
        var watcher = File.ReadAllText(RepoPath("RiveTT.Plugin", "Caching", "DocumentChangeWatcher.cs"));
        var invalidator = File.ReadAllText(RepoPath("RiveTT.Core", "Caching", "CacheInvalidator.cs"));
        var router = File.ReadAllText(RepoPath("RiveTT.Plugin", "CortexRouter.cs"));

        // Save As raises DocumentSavedAs, not DocumentSaved: without this hook,
        // get_project_info kept answering with the pre-Save-As path in 0 ms.
        Assert.Contains("DocumentSavedAs", watcher);
        Assert.Contains("OnActiveDocumentReplaced", invalidator);
        Assert.Contains("LifecycleWriteTools", router);
        // And a cached answer must admit that it is cached.
        Assert.Contains("[\"cached\"] = true", router);
    }
}

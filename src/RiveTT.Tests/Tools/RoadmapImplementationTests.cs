using System;
using System.IO;
using Xunit;

namespace RiveTT.Tests.Tools;

public sealed class RoadmapImplementationTests
{
    [Fact]
    public void EveryCreateLevelWriteActionChecksDryRunBeforeTransaction()
    {
        var source = ReadSource("RiveTT.Tools", "Elements", "CreateLevelTool.cs");
        AssertDryRunBeforeTransaction(source, "SetLevel", "RenameLevel");
        AssertDryRunBeforeTransaction(source, "RenameLevel", "DeleteLevel");
        AssertDryRunBeforeTransaction(source, "DeleteLevel", "ResolveLevel");
    }

    [Fact]
    public void WallCreationPreviewsAndReturnsActualConstraintGeometry()
    {
        var source = ReadSource("RiveTT.Tools", "Elements", "CreateLineBasedElementTool.cs");
        Assert.Contains("ToolHelpers.GetDryRun(input)", source);
        Assert.Contains("resultingBaseElevationMm", source);
        Assert.Contains("actualBaseOffset", source);
        Assert.Contains("coordinates = \"absolute_project_coordinates_mm\"", source);
        Assert.Contains("offsets = \"relative_to_constraint_level_mm\"", source);
    }

    [Fact]
    public void ElementSearchImplementsVersionedPaginationAndResponseModes()
    {
        var source = ReadSource("RiveTT.Tools", "Elements", "AIElementFilterTool.cs");
        Assert.Contains("nextCursor", source);
        Assert.Contains("appliedLimit", source);
        Assert.Contains("EncodeCursor(session.DocumentVersion", source);
        Assert.Contains("summary\" or \"idsonly\" or \"details", source);
    }

    [Fact]
    public void BulkScopesSupportStableSelectionTokensAndExplicitIds()
    {
        var resolver = ReadSource("RiveTT.Tools", "Utilities", "ElementScopeResolver.cs");
        Assert.Contains("selectionToken", resolver);
        Assert.Contains("savedSelectionName", resolver);
        Assert.Contains("last_filter", resolver);
        Assert.Contains("elementIds", resolver);
        Assert.Contains("expiresAtUtc", ReadSource("RiveTT.Tools", "Elements", "CaptureSelectionTool.cs"));
    }

    [Fact]
    public void CsvSyncUsesCentralParameterResolverAndCompactDiagnostics()
    {
        var source = ReadSource("RiveTT.Tools", "Parameters", "SyncCsvParametersTool.cs");
        Assert.Contains("ParameterLookup.FindParameter", source);
        Assert.Contains("parameterMap", source);
        Assert.Contains("unmatchedHeaders", source);
        Assert.Contains("includeDetails", source);
        Assert.Contains("sampleLimit", source);
    }

    [Fact]
    public void NewStoreyWallAndGroupToolsAreDedicatedAndPreviewFirst()
    {
        foreach (var parts in new[]
                 {
                     new[] { "Project", "DuplicateStoreyTool.cs" },
                     new[] { "Elements", "DetachWallConstraintTool.cs" },
                     new[] { "Elements", "ManageModelGroupsTool.cs" }
                 })
        {
            var source = ReadSource("RiveTT.Tools", parts);
            Assert.Contains("ToolHelpers.GetDryRun(input)", source);
            Assert.Contains("[ToolSafety(false, true)]", source);
        }
    }

    [Fact]
    public void DocumentLifecycleOperationsAreExposedWithoutTheFalseDeadlockClaim()
    {
        var caps = ReadSource("RiveTT.Tools", "Meta", "GetServerCapabilitiesTool.cs");

        // open_document IS exposed: the restriction on switching documents
        // applies to API *event* handlers (Idling, DocumentChanged), not to the
        // ExternalEvent handler every tool runs in. Autodesk's guidance is that an
        // External Event is the supported and safe way to open-and-activate.
        // edit_family shipped as part of P4.1 in PLAN_CORRECTION.md, once the
        // earlier false claim that Document.EditFamily deadlocks from this
        // dispatcher — the reason the capability was abandoned in the first
        // place — was corrected.
        Assert.Contains("edit_family", caps);
        Assert.DoesNotContain("Document.EditFamily deadlocked", caps);
        Assert.DoesNotContain("edit_family (opening the family document) is not exposed", caps);
        Assert.DoesNotContain("open_document is not exposed", caps);
    }

    [Fact]
    public void PreviouslyImpossibleOperationsAreExposedWithTheirRealConstraints()
    {
        var stair = ReadSource("RiveTT.Tools", "Elements", "CreateStairTool.cs");
        var lifecycle = ReadSource("RiveTT.Tools", "Project", "DocumentCreationTools.cs");
        var groups = ReadSource("RiveTT.Tools", "Elements", "EditGroupMembersTool.cs");

        // A component stair goes through StairsEditScope, which opens no UI. The
        // scope must be started outside a transaction and committed with a failure
        // preprocessor, or a warning would open a modal dialog and freeze the pipe.
        Assert.Contains("new StairsEditScope(doc", stair);
        Assert.Contains("StairsRun.CreateStraightRun", stair);
        Assert.Contains("scope.Commit(scopeFailures)", stair);
        Assert.Contains("scope?.Cancel()", stair);

        // A new project comes from the template, not from duplicating the open model.
        Assert.Contains("NewProjectDocument", lifecycle);
        Assert.Contains("OpenAndActivateDocument", lifecycle);
        // The in-memory document must never be left open holding a file lock.
        Assert.Contains("created?.Close(false)", lifecycle);

        // Group members cannot be edited in place; the divergence this creates must
        // be refused by default rather than silently produced.
        Assert.Contains("UngroupMembers()", groups);
        Assert.Contains("allowMultiInstance", groups);
        Assert.Contains("otherInstancesNotUpdated", groups);
    }

    private static void AssertDryRunBeforeTransaction(string source, string method, string nextMethod)
    {
        var start = source.IndexOf($"private static CortexResult<object> {method}(", StringComparison.Ordinal);
        var end = source.IndexOf("\n    private static ", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate {method}");
        var body = source.Substring(start, end - start);
        Assert.True(body.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal) >= 0);
        Assert.True(body.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal) <
                    body.IndexOf("new Transaction(", StringComparison.Ordinal));
    }

    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 5];
        parts[0] = parts[1] = parts[2] = parts[3] = "..";
        parts[4] = project;
        relativeParts.CopyTo(parts, 5);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts)));
    }
}

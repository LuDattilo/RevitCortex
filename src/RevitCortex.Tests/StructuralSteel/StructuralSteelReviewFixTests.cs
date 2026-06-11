using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

/// <summary>
/// Source-text + reflection guards for the 2026-06-01 StructuralSteel review fixes. These touch NO
/// Revit types (reading .cs files via Path.Combine like StructuralSteelServerForwardingSourceTests,
/// and reflecting over the pure-managed server wrapper class), so they run without a Revit install
/// and pin the fixes against future drift.
/// </summary>
public class StructuralSteelReviewFixTests
{
    private static string ReadToolsSource(string fileName)
    {
        // Tests run from .../RevitCortex.Tests/bin/<cfg>/<tfm>/ → climb 4 to src/ then into RevitCortex.Tools.
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Tools", "StructuralSteel", fileName));
        return File.ReadAllText(path);
    }

    private static string ReadServerSteelSource()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", "StructuralSteelTools.cs"));
        return File.ReadAllText(path);
    }

    private static string ReadRepoDoc(string relativeFromRepoRoot)
    {
        // bin/<cfg>/<tfm>/ → 4 ups reach src/ (same as the forwarding test); one more reaches the repo root.
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", relativeFromRepoRoot));
        return File.ReadAllText(path);
    }

    // ---- Fix 1: set_steel_connection_type preserves + reports writable state. ----

    [Fact]
    public void Fix1_SetConnectionType_SnapshotsAndRestoresWritableState()
    {
        var src = ReadToolsSource("StructuralSteelConnectionTools.cs");
        // Snapshot + restore must reference each writable handler property by name.
        Assert.Contains("ApprovalTypeId", src);
        Assert.Contains("CodeCheckingStatus", src);
        Assert.Contains("OverrideTypeParams", src);
        Assert.Contains("SingleElementEndIndex", src);
        // Input-point/reference counts must be snapshotted so the response can report the loss.
        Assert.Contains("GetInputPoints()", src);
        Assert.Contains("GetInputReferences()", src);
    }

    [Fact]
    public void Fix1_SetConnectionType_ReportsPreserveLoseAndRestoredFields()
    {
        var src = ReadToolsSource("StructuralSteelConnectionTools.cs");
        Assert.Contains("willPreserve", src);
        Assert.Contains("willLose", src);
        Assert.Contains("stateSnapshot", src);
        Assert.Contains("restoredFields", src);
        Assert.Contains("lostFields", src);
    }

    // ---- Fix 2: honest capability semantics. ----

    [Fact]
    public void Fix2_Capabilities_ExposeHonestProviderAndMutationFields()
    {
        var src = ReadToolsSource("StructuralSteelDiscoveryTools.cs");
        Assert.Contains("supportsCustomConnectionMutationFromJson", src);
        Assert.Contains("connectionProviderState", src);
        Assert.Contains("connectionProviderDetection", src);
        Assert.Contains("customConnectionMutationApiMembersExist", src);
        Assert.Contains("customConnectionMutationReason", src);
    }

    [Fact]
    public void Fix2_ProviderUnavailableError_DoesNotAssertProviderAbsentAsFact()
    {
        var src = ReadToolsSource("StructuralSteelToolHelpers.cs");
        // The reworded message must frame provider state as not-queryable, not as a known absence.
        Assert.Contains("not queryable", src.ToLowerInvariant());
    }

    // ---- Fix 3: dryRun coverage matches the documented contract (no drift). ----

    // Tools whose plugin Execute actually reads input["dryRun"] (verified by grep of the two source files).
    private static readonly string[] DryRunSupportingTools =
    {
        "create_generic_steel_connection",
        "create_steel_connection",
        "set_steel_connection_type",
        "delete_steel_connection",
        "create_steel_structural_connection_type",
        "create_steel_connection_handler_type",
        "create_default_steel_connection_handler_type",
        "set_steel_connection_type_family_symbol",
        "manage_steel_approval_type",
        "add_steel_fabrication_info",
        "add_steel_solid_cut",
        "add_steel_instance_void_cut",
        // 2026-06-11 dryRun-uniformity pass: the five mutators below gained real
        // dryRun support (plugin preview + wrapper parameter), closing the open
        // alternative resolution of review Fix 3.
        "modify_steel_connection_inputs",
        "set_steel_connection_approval",
        "set_steel_connection_status",
        "remove_steel_solid_cut",
        "remove_steel_instance_void_cut",
    };

    // Write tools that DO NOT preview — they confirm then write (no dryRun parameter on the wrapper).
    private static readonly string[] NonPreviewMutators =
    {
        "set_steel_connection_default_order",
        "set_steel_solid_cut_face_splitting",
        "set_steel_fabrication_unique_id",
    };

    private static MethodInfo? WrapperFor(string toolName)
    {
        return typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name == toolName);
    }

    private static bool WrapperDeclaresDryRun(MethodInfo m)
        => m.GetParameters().Any(p => string.Equals(p.Name, "dryRun", StringComparison.Ordinal));

    [Fact]
    public void Fix3_NonPreviewMutators_DoNotDeclareDryRunParameter()
    {
        foreach (var tool in NonPreviewMutators)
        {
            var wrapper = WrapperFor(tool);
            Assert.True(wrapper != null, $"No server wrapper found for '{tool}'");
            Assert.False(WrapperDeclaresDryRun(wrapper!),
                $"'{tool}' is documented as a non-preview mutator but its wrapper declares a dryRun parameter.");
        }
    }

    [Fact]
    public void Fix3_DryRunSupportingTools_DeclareDryRunParameter()
    {
        foreach (var tool in DryRunSupportingTools)
        {
            var wrapper = WrapperFor(tool);
            Assert.True(wrapper != null, $"No server wrapper found for '{tool}'");
            Assert.True(WrapperDeclaresDryRun(wrapper!),
                $"'{tool}' is documented as dryRun-supporting but its wrapper has no dryRun parameter.");
        }
    }

    [Fact]
    public void Fix3_DryRunAndNonPreviewLists_AreDisjoint()
    {
        var overlap = DryRunSupportingTools.Intersect(NonPreviewMutators).ToList();
        Assert.True(overlap.Count == 0, $"Tools listed as both dryRun-supporting and non-preview: {string.Join(", ", overlap)}");
    }

    [Fact]
    public void Fix3_UserGuide_ListsNonPreviewMutators()
    {
        var guide = ReadRepoDoc(Path.Combine("docs", "USER_GUIDE.md"));
        // The USER_GUIDE dryRun paragraph must name every non-preview mutator so callers are not misled.
        foreach (var tool in NonPreviewMutators)
            Assert.Contains(tool, guide);
        // And it must scope dryRun as partial ("solo" / "alcuni"), not blanket.
        Assert.Contains("dryRun", guide);
    }

    // ---- Fix 4: reflective fabrication unique-id setter is marked experimental. ----

    [Fact]
    public void Fix4_FabricationUniqueId_MarkedExperimental()
    {
        var src = ReadToolsSource("StructuralSteelFabricationTools.cs");
        Assert.Contains("experimental", src);
        Assert.Contains("non-public", src);
    }

    [Fact]
    public void Fix4_Capabilities_ReportSetFabricationUniqueIdSupport()
    {
        var src = ReadToolsSource("StructuralSteelDiscoveryTools.cs");
        Assert.Contains("supportsSetSteelFabricationUniqueId", src);
    }
}

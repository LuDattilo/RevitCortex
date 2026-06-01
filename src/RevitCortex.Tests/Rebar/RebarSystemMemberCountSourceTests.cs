using System.IO;
using Xunit;

namespace RevitCortex.Tests.Rebar;

/// <summary>
/// Regression guard (source-text) for the area/path reinforcement member-count bug.
///
/// <para>
/// <c>AreaReinforcement.Create</c> / <c>PathReinforcement.Create</c> return the system element
/// immediately, but Revit only materializes the in-system bars during a regeneration. Reading
/// <c>GetRebarInSystemIds()</c> right after Create — before <c>doc.Regenerate()</c> — yields an
/// empty list, so the create response reported a misleading <c>memberCount: 0</c> even though the
/// system actually generated members (verified live on Snowdon Towers: create returned 0, an
/// immediate <c>get_area_reinforcement_data</c> on the same id returned 18).
/// </para>
///
/// <para>
/// The sibling <c>create_fabric_area</c> already does the right thing (regenerates before reading
/// <c>GetFabricSheetElementIds()</c>); these tools must match that pattern. We assert at the source
/// level because the tool bodies reference <c>Autodesk.Revit.DB.Structure</c> types and cannot be
/// invoked outside a Revit install (see the project's unit-test-cannot-touch-Revit-types rule).
/// </para>
/// </summary>
public class RebarSystemMemberCountSourceTests
{
    private static string ReadRebarSystemTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Tools", "Rebar", "RebarSystemTools.cs"));
        return File.ReadAllText(path);
    }

    private static string ReadFabricReinforcementTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Tools", "Rebar", "FabricReinforcementTools.cs"));
        return File.ReadAllText(path);
    }

    /// <summary>
    /// In create_area_reinforcement the doc.Regenerate() call must appear AFTER AreaReinforcement.Create
    /// and BEFORE the GetRebarInSystemIds() read, so the reported memberCount/memberIds are truthful.
    /// </summary>
    [Fact]
    public void CreateAreaReinforcement_RegeneratesBeforeReadingMembers()
    {
        var src = ReadRebarSystemTools();

        int createIdx = src.IndexOf("AreaReinforcement.Create(", System.StringComparison.Ordinal);
        int regenIdx = src.IndexOf("doc.Regenerate()", System.StringComparison.Ordinal);
        int readIdx = src.IndexOf("GetRebarInSystemIds()", System.StringComparison.Ordinal);

        Assert.True(createIdx >= 0, "Expected an AreaReinforcement.Create(...) call");
        Assert.True(regenIdx > createIdx,
            "create_area_reinforcement must call doc.Regenerate() AFTER AreaReinforcement.Create(...) "
            + "so the in-system bars are materialized before they are read.");
        Assert.True(readIdx > regenIdx,
            "create_area_reinforcement must read GetRebarInSystemIds() AFTER doc.Regenerate(), "
            + "otherwise it reports a misleading memberCount: 0.");
    }

    /// <summary>
    /// In create_path_reinforcement the doc.Regenerate() call must appear AFTER PathReinforcement.Create
    /// and BEFORE the GetRebarInSystemIds() read.
    /// </summary>
    [Fact]
    public void CreatePathReinforcement_RegeneratesBeforeReadingMembers()
    {
        var src = ReadRebarSystemTools();

        int createIdx = src.IndexOf("PathReinforcement.Create(", System.StringComparison.Ordinal);
        int regenIdx = src.IndexOf("doc.Regenerate()", createIdx, System.StringComparison.Ordinal);
        // The path read is the second GetRebarInSystemIds() in the file (the first is in the area tool).
        int readIdx = src.IndexOf("GetRebarInSystemIds()",
            createIdx >= 0 ? createIdx : 0, System.StringComparison.Ordinal);

        Assert.True(createIdx >= 0, "Expected a PathReinforcement.Create(...) call");
        Assert.True(regenIdx > createIdx,
            "create_path_reinforcement must call doc.Regenerate() AFTER PathReinforcement.Create(...).");
        Assert.True(readIdx > regenIdx,
            "create_path_reinforcement must read GetRebarInSystemIds() AFTER doc.Regenerate().");
    }

    /// <summary>
    /// The proven reference: create_fabric_area already regenerates before reading its sheet ids.
    /// If this ever regresses, the bug above is back in the whole module.
    /// </summary>
    [Fact]
    public void CreateFabricArea_StillRegeneratesBeforeReadingSheets()
    {
        var src = ReadFabricReinforcementTools();

        int createIdx = src.IndexOf("FabricArea.Create(", System.StringComparison.Ordinal);
        int regenIdx = src.IndexOf("doc.Regenerate()", System.StringComparison.Ordinal);
        int readIdx = src.IndexOf("GetFabricSheetElementIds()", System.StringComparison.Ordinal);

        Assert.True(createIdx >= 0, "Expected a FabricArea.Create(...) call");
        Assert.True(regenIdx > createIdx && readIdx > regenIdx,
            "create_fabric_area must regenerate between FabricArea.Create(...) and GetFabricSheetElementIds().");
    }
}

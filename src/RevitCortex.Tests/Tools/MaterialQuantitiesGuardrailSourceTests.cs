using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions for the get_material_quantities guardrails. The tool runs
/// GetMaterialArea/GetMaterialVolume (geometry-backed) per element on the Revit UI
/// thread; without a cap a full-model pass on a large model freezes Revit far past
/// the 120s dispatcher timeout (audit 2026-06-11, diagnosis "(a)"). Partial sums are
/// silently wrong, so both guardrails fail structurally instead of truncating.
/// </summary>
public class MaterialQuantitiesGuardrailSourceTests
{
    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", project };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void Plugin_EnforcesMaxElementsCap_AsStructuredFailure()
    {
        var src = ReadSource("RevitCortex.Tools", "Project", "GetMaterialQuantitiesTool.cs");
        // The cap must be an input the caller can raise deliberately…
        Assert.Contains("input[\"maxElements\"]", src);
        // …and over-cap must fail before the loop, not truncate the sums.
        Assert.Contains("elements.Count > maxElements", src);
        Assert.Contains("categoryFilters or selectedElementsOnly", src);
    }

    [Fact]
    public void Plugin_HasTimeBudgetEarlyExit_UnderTheDispatcherTimeout()
    {
        var src = ReadSource("RevitCortex.Tools", "Project", "GetMaterialQuantitiesTool.cs");
        Assert.Contains("Stopwatch.StartNew()", src);
        // Budget must stay under the 120s dispatcher timeout so the caller receives
        // this structured error instead of the generic dispatcher Timeout.
        Assert.Contains("TimeBudgetMs = 90", src);
        Assert.Contains("CortexErrorCode.Timeout", src);
    }

    [Fact]
    public void ServerWrapper_ForwardsMaxElements()
    {
        var src = ReadSource("RevitCortex.Server", "Tools", "MaterialTools.cs");
        Assert.Contains("maxElements", src);
        Assert.Contains("p[\"maxElements\"]", src);
    }
}

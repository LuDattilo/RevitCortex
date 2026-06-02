using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Source-text assertions that the delete-in-loop tools surface per-item failures
/// (failedCount/failures) instead of swallowing them in an empty catch. These tools
/// touch Revit types so they cannot be exercised in a unit test without Revit; the
/// source facts lock the additive diagnostics contract and prevent a regression back
/// to the silent `catch { }` form. Verified live on Revit separately.
/// </summary>
public class SilentDropDiagnosticsSourceTests
{
    private static string ReadTool(params string[] relativeParts)
    {
        var parts = new System.Collections.Generic.List<string> { "..", "..", "..", "..", "RevitCortex.Tools" };
        parts.AddRange(relativeParts);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts.ToArray())));
    }

    [Fact]
    public void PurgeUnused_SurfacesDeleteFailures()
    {
        var src = ReadTool("Project", "PurgeUnusedTool.cs");
        Assert.Contains("failedCount = failures.Count", src);
        Assert.Contains("failures = failures.Take(50).ToList()", src);
        // The delete helper must record the reason on failure, not swallow it.
        Assert.Contains("catch (Exception ex) { failures.Add(", src);
        // And must NOT have reverted to the silent form on the delete calls.
        Assert.DoesNotContain("deletedTypes++; } catch { }", src);
    }

    [Fact]
    public void WipeEmptyTags_SurfacesDeleteFailures()
    {
        var src = ReadTool("Annotations", "WipeEmptyTagsTool.cs");
        Assert.Contains("failedCount = failures.Count", src);
        Assert.Contains("failures = failures.Take(50).ToList()", src);
        Assert.Contains("catch (Exception ex) { failures.Add(", src);
        Assert.DoesNotContain("deleted++; } catch { }", src);
    }

    [Fact]
    public void ExportToExcel_GatesAutoFitAndSurfacesTruncation()
    {
        var src = ReadTool("Elements", "ExportToExcelTool.cs");
        // AdjustToContents must be gated behind a row-count threshold, not unconditional:
        // the AdjustToContents call must be guarded by the autoFit flag.
        Assert.Contains("AutoFitRowThreshold", src);
        Assert.Contains("if (autoFit)", src);
        Assert.Contains("AdjustToContents(1, 50)", src);
        // Truncation must be surfaced, not silent.
        Assert.Contains("truncated", src);
        Assert.Contains("Take(maxElements + 1)", src);
    }

    [Fact]
    public void BulkModify_DoesNotMarkUnassignableValuesAsModified_AndSurfacesFailures()
    {
        var src = ReadTool("Parameters", "BulkModifyParameterValuesTool.cs");
        // Result exposes the failure diagnostics.
        Assert.Contains("failedCount = failures.Count", src);
        Assert.Contains("failures = failures.Take(50).ToList()", src);
        // An unparsable numeric value is counted as skipped + recorded, NOT modified.
        Assert.Contains("bool assignable", src);
        Assert.Contains("if (!assignable)", src);
        // The per-element Set is now guarded so one failure cannot abort the batch.
        Assert.Contains("catch (Exception ex)", src);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates a grid system with specified counts, spacing, and labeling.
/// </summary>
public class CreateGridTool : ICortexTool
{
    public string Name => "create_grid";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a grid system, or renames/deletes an existing grid. Actions: create (default), rename, delete.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "create").ToLowerInvariant();
        if (action == "rename") return RenameGrid(doc, input, session);
        if (action == "delete") return DeleteGrid(doc, input, session);
        if (action != "create")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown action: {action}", suggestion: "Use: create, rename, delete");

        var xCount = input["xCount"]?.Value<int>() ?? 0;
        var yCount = input["yCount"]?.Value<int>() ?? 0;
        var xSpacingMm = input["xSpacing"]?.Value<double>() ?? 5000;
        var ySpacingMm = input["ySpacing"]?.Value<double>() ?? 5000;
        var xStartLabel = input["xStartLabel"]?.Value<string>() ?? "A";
        var yStartLabel = input["yStartLabel"]?.Value<string>() ?? "1";
        var xNaming = input["xNamingStyle"]?.Value<string>() ?? "alphabetic";
        var yNaming = input["yNamingStyle"]?.Value<string>() ?? "numeric";
        var elevationMm = input["elevation"]?.Value<double>() ?? 0;
        var xExtentMinMm = input["xExtentMin"]?.Value<double>() ?? -5000;
        var xExtentMaxMm = input["xExtentMax"]?.Value<double>() ?? (yCount > 0 ? yCount * ySpacingMm + 5000 : 30000);
        var yExtentMinMm = input["yExtentMin"]?.Value<double>() ?? -5000;
        var yExtentMaxMm = input["yExtentMax"]?.Value<double>() ?? (xCount > 0 ? xCount * xSpacingMm + 5000 : 30000);

        if (xCount <= 0 && yCount <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "At least one of xCount or yCount must be > 0");

        try
        {
            var createdGrids = new List<object>();
            var warnings = new List<string>();
            var z = elevationMm / MmPerFoot;

            // Collect existing grid names for conflict detection
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(Grid))
                    .Cast<Grid>().Select(g => g.Name),
                StringComparer.OrdinalIgnoreCase);

            using var tx = new Transaction(doc, "RevitCortex: Create Grid");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            // X grids (vertical lines, labeled alphabetically by default)
            for (int i = 0; i < xCount; i++)
            {
                var x = i * xSpacingMm / MmPerFoot;
                var start = new XYZ(x, xExtentMinMm / MmPerFoot, z);
                var end = new XYZ(x, xExtentMaxMm / MmPerFoot, z);
                var line = Line.CreateBound(start, end);
                var grid = Grid.Create(doc, line);
                var label = GenerateLabel(xStartLabel, i, xNaming);
                if (existingNames.Contains(label))
                    warnings.Add($"Grid label '{label}' already exists, auto-assigned '{grid.Name}'.");
                else if (TrySetName(grid, label))
                    existingNames.Add(label);
                createdGrids.Add(new { id = ToolHelpers.GetElementIdValue(grid.Id), axis = "X", name = grid.Name, requestedLabel = label, position = i * xSpacingMm });
            }

            // Y grids (horizontal lines, labeled numerically by default)
            for (int i = 0; i < yCount; i++)
            {
                var y = i * ySpacingMm / MmPerFoot;
                var start = new XYZ(yExtentMinMm / MmPerFoot, y, z);
                var end = new XYZ(yExtentMaxMm / MmPerFoot, y, z);
                var line = Line.CreateBound(start, end);
                var grid = Grid.Create(doc, line);
                var label = GenerateLabel(yStartLabel, i, yNaming);
                if (existingNames.Contains(label))
                    warnings.Add($"Grid label '{label}' already exists, auto-assigned '{grid.Name}'.");
                else if (TrySetName(grid, label))
                    existingNames.Add(label);
                createdGrids.Add(new { id = ToolHelpers.GetElementIdValue(grid.Id), axis = "Y", name = grid.Name, requestedLabel = label, position = i * ySpacingMm });
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                createdCount = createdGrids.Count,
                grids = createdGrids,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create grid: {ex.Message}");
        }
    }

    private static string GenerateLabel(string start, int index, string style)
    {
        if (style == "alphabetic")
        {
            // A..Z, AA..AZ, BA..
            int charIndex = 0;
            if (start.Length == 1 && char.IsLetter(start[0]))
                charIndex = char.ToUpper(start[0]) - 'A';
            int total = charIndex + index;
            if (total < 26) return ((char)('A' + total)).ToString();
            return ((char)('A' + total / 26 - 1)).ToString() + ((char)('A' + total % 26)).ToString();
        }
        // numeric
        if (int.TryParse(start, out var startNum))
            return (startNum + index).ToString();
        return (index + 1).ToString();
    }

    private static bool TrySetName(Grid grid, string name)
    {
        try { grid.Name = name; return true; }
        catch { return false; }
    }

    private static CortexResult<object> RenameGrid(Document doc, JObject input, CortexSession session)
    {
        var (grid, error) = ResolveGrid(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newName is required for rename");

        var clash = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
            .FirstOrDefault(g => g.Id != grid!.Id && g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"A grid named '{newName}' already exists");

        if (!session.RequestConfirmation("rename grid", 1, grid!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var oldName = grid.Name;
        using var tx = new Transaction(doc, "RevitCortex: Rename Grid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        grid.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new { action = "rename", gridId = ToolHelpers.GetElementIdValue(grid.Id), oldName, newName });
    }

    private static CortexResult<object> DeleteGrid(Document doc, JObject input, CortexSession session)
    {
        var (grid, error) = ResolveGrid(doc, input);
        if (error != null) return error;

        if (!session.RequestConfirmation("delete grid", 1, grid!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var name = grid!.Name;
        using var tx = new Transaction(doc, "RevitCortex: Delete Grid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        doc.Delete(grid.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new { action = "delete", deletedGrid = name });
    }

    /// <summary>Resolves a grid by gridId or name from the input.</summary>
    private static (Grid?, CortexResult<object>?) ResolveGrid(Document doc, JObject input)
    {
        var gridIdLong = input["gridId"]?.Value<long?>() ?? 0;
        var name = input["name"]?.Value<string>();

        Grid? grid = null;
        if (gridIdLong > 0)
            grid = doc.GetElement(ToolHelpers.ToElementId(gridIdLong)) as Grid;
        if (grid == null && !string.IsNullOrEmpty(name))
            grid = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (grid == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Grid not found", suggestion: "Provide a valid gridId or name"));

        return (grid, null);
    }
}

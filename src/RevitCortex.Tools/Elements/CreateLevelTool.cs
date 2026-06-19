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
/// Creates a new level at the specified elevation, optionally with floor/ceiling plan views.
/// </summary>
public class CreateLevelTool : ICortexTool
{
    public string Name => "create_level";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates, edits, renames, or deletes levels. Actions: create (default), set (elevation/isBuildingStory), rename, delete.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "create").ToLowerInvariant();

        try
        {
            return action switch
            {
                "create" => CreateLevel(doc, input),
                "set"    => SetLevel(doc, input, session),
                "rename" => RenameLevel(doc, input, session),
                "delete" => DeleteLevel(doc, input, session),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: create, set, rename, delete")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to manage level: {ex.Message}");
        }
    }

    private static CortexResult<object> CreateLevel(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var elevationMm = input["elevation"]?.Value<double>() ?? 0;
        var isBuildingStory = input["isBuildingStory"]?.Value<bool>() ?? true;
        var createFloorPlan = input["createFloorPlan"]?.Value<bool>() ?? false;
        var createCeilingPlan = input["createCeilingPlan"]?.Value<bool>() ?? false;

        {
            // Check for duplicate name
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Level '{name}' already exists at elevation {existing.Elevation * MmPerFoot:F0} mm");

            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RevitCortex: Create Level");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var level = Level.Create(doc, elevationMm / MmPerFoot);
            level.Name = name;

            var storyParam = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            if (storyParam != null && !storyParam.IsReadOnly)
                storyParam.Set(isBuildingStory ? 1 : 0);

            long? floorPlanId = null;
            long? ceilingPlanId = null;

            if (createFloorPlan)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (vft != null)
                {
                    var view = ViewPlan.Create(doc, vft.Id, level.Id);
                    floorPlanId = ToolHelpers.GetElementIdValue(view.Id);
                }
                else warnings.Add("Floor plan ViewFamilyType not found");
            }

            if (createCeilingPlan)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);
                if (vft != null)
                {
                    var view = ViewPlan.Create(doc, vft.Id, level.Id);
                    ceilingPlanId = ToolHelpers.GetElementIdValue(view.Id);
                }
                else warnings.Add("Ceiling plan ViewFamilyType not found");
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                levelId = ToolHelpers.GetElementIdValue(level.Id),
                name = level.Name,
                elevationMm,
                isBuildingStory,
                floorPlanId,
                ceilingPlanId,
                warnings
            });
        }
    }

    private static CortexResult<object> SetLevel(Document doc, JObject input, CortexSession session)
    {
        var (level, error) = ResolveLevel(doc, input);
        if (error != null) return error;

        if (!session.RequestConfirmation("modify level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var changed = new List<string>();
        using var tx = new Transaction(doc, "RevitCortex: Set Level");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var elevationMm = input["elevation"]?.Value<double?>();
        if (elevationMm.HasValue)
        {
            level.Elevation = elevationMm.Value / MmPerFoot;
            changed.Add("elevation");
        }

        var isBuildingStory = input["isBuildingStory"]?.Value<bool?>();
        if (isBuildingStory.HasValue)
        {
            var storyParam = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            if (storyParam != null && !storyParam.IsReadOnly)
            {
                storyParam.Set(isBuildingStory.Value ? 1 : 0);
                changed.Add("isBuildingStory");
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            action = "set",
            levelId = ToolHelpers.GetElementIdValue(level.Id),
            name = level.Name,
            elevationMm = level.Elevation * MmPerFoot,
            changedFields = changed
        });
    }

    private static CortexResult<object> RenameLevel(Document doc, JObject input, CortexSession session)
    {
        var (level, error) = ResolveLevel(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newName is required for rename");

        var clash = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => l.Id != level!.Id && l.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"A level named '{newName}' already exists");

        if (!session.RequestConfirmation("rename level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var oldName = level.Name;
        using var tx = new Transaction(doc, "RevitCortex: Rename Level");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        level.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new { action = "rename", levelId = ToolHelpers.GetElementIdValue(level.Id), oldName, newName });
    }

    private static CortexResult<object> DeleteLevel(Document doc, JObject input, CortexSession session)
    {
        var (level, error) = ResolveLevel(doc, input);
        if (error != null) return error;

        if (!session.RequestConfirmation("delete level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var name = level!.Name;
        using var tx = new Transaction(doc, "RevitCortex: Delete Level");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        doc.Delete(level.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new { action = "delete", deletedLevel = name });
    }

    /// <summary>Resolves a level by levelId or name from the input.</summary>
    private static (Level?, CortexResult<object>?) ResolveLevel(Document doc, JObject input)
    {
        var levelIdLong = input["levelId"]?.Value<long?>() ?? 0;
        var name = input["name"]?.Value<string>();

        Level? level = null;
        if (levelIdLong > 0)
            level = doc.GetElement(ToolHelpers.ToElementId(levelIdLong)) as Level;
        if (level == null && !string.IsNullOrEmpty(name))
            level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (level == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Level not found", suggestion: "Provide a valid levelId or name"));

        return (level, null);
    }
}

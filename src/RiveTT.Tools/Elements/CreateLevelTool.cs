using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a new level at the specified elevation, optionally with floor/ceiling plan views.
/// </summary>
[ToolSafety(false, true)]
public class CreateLevelTool : ICortexTool
{
    public string Name => "create_level";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates, edits, renames, or deletes levels. Actions: create (default), set (elevation/isBuildingStory), rename, delete.";

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
                "create" => CreateLevel(doc, input, session),
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

    private static CortexResult<object> CreateLevel(Document doc, JObject input, CortexSession session)
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

            // Preview-first: dryRun (default true) reports what would be created without
            // touching the model. Only after this do we confirm and open a transaction.
            if (ToolHelpers.GetDryRun(input))
                return CortexResult<object>.Ok(new
                {
                    dryRun = true,
                    action = "create",
                    name,
                    elevationMm,
                    isBuildingStory,
                    createFloorPlan,
                    createCeilingPlan
                });

            if (!session.RequestConfirmation("create level", 1, name))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RiveTT: Create Level");
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

        var elevationMm = input["elevation"]?.Value<double?>();
        var isBuildingStory = input["isBuildingStory"]?.Value<bool?>();
        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                action = "set",
                levelId = ToolHelpers.GetElementIdValue(level!.Id),
                name = level.Name,
                currentElevationMm = level.Elevation * MmPerFoot,
                requestedElevationMm = elevationMm,
                requestedIsBuildingStory = isBuildingStory
            });

        if (!session.RequestConfirmation("modify level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var changed = new List<string>();
        using var tx = new Transaction(doc, "RiveTT: Set Level");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        if (elevationMm.HasValue)
        {
            level.Elevation = elevationMm.Value / MmPerFoot;
            changed.Add("elevation");
        }

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

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                action = "rename",
                levelId = ToolHelpers.GetElementIdValue(level!.Id),
                oldName = level.Name,
                newName
            });

        if (!session.RequestConfirmation("rename level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var oldName = level.Name;
        using var tx = new Transaction(doc, "RiveTT: Rename Level");
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

        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                action = "delete",
                levelId = ToolHelpers.GetElementIdValue(level!.Id),
                name = level.Name,
                dependentElementCount = level.GetDependentElements(null).Count
            });

        if (!session.RequestConfirmation("delete level", 1, level!.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var name = level!.Name;
        using var tx = new Transaction(doc, "RiveTT: Delete Level");
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

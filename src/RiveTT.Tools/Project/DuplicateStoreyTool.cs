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

namespace RiveTT.Tools.Project;

[ToolSafety(false, true)]
public sealed class DuplicateStoreyTool : ICortexTool
{
    public string Name => "duplicate_storey";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Preview or transactionally duplicate one storey's model elements to a target elevation, with conservative group and constraint handling.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");
        var source = ResolveLevel(doc, input["sourceLevelId"]?.Value<long>(),
            input["sourceLevelName"]?.Value<string>());
        if (source == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Source level was not found");
        var targetElevationMm = input["targetElevationMm"]?.Value<double?>();
        if (!targetElevationMm.HasValue)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "targetElevationMm is required");
        var targetName = input["targetLevelName"]?.Value<string>()
            ?? $"{source.Name} Copy";
        var targetTopLevelId = input["targetTopLevelId"]?.Value<long>() ?? 0;
        var shiftUpperMm = input["moveUpperLevelsByMm"]?.Value<double>() ?? 0;
        var copyGroups = input["copyGroups"]?.Value<bool>() ?? true;
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var includeDetails = input["includeDetails"]?.Value<bool>() ?? false;
        var sampleLimit = Math.Clamp(input["sampleLimit"]?.Value<int>() ?? 50, 0, 500);
        var dryRun = ToolHelpers.GetDryRun(input);

        var targetElevationFt = targetElevationMm.Value / MmPerFoot;
        var deltaFt = targetElevationFt - source.Elevation;
        if (Math.Abs(deltaFt) < 1e-9)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Target elevation must differ from source elevation");

        var analysis = AnalyzeCandidates(doc, source, categories, copyGroups);
        var upperLevels = Math.Abs(shiftUpperMm) > 1e-9
            ? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(level => level.Id != source.Id && level.Elevation >= targetElevationFt - 1e-9)
                .OrderBy(level => level.Elevation).ToList()
            : new List<Level>();

        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                sourceLevel = LevelInfo(source),
                targetLevel = new { name = targetName, elevationMm = targetElevationMm.Value },
                verticalOffsetMm = deltaFt * MmPerFoot,
                copyableCount = analysis.Copyable.Count,
                skippedViewSpecificCount = analysis.ViewSpecific.Count,
                groupedMemberCount = analysis.GroupedMembers.Count,
                groupInstanceCount = analysis.GroupInstances.Count,
                constrainedWallCount = analysis.ConstrainedWalls.Count,
                upperLevelsToMove = upperLevels.Select(LevelInfo).ToList(),
                moveUpperLevelsByMm = shiftUpperMm,
                blockingDependencies = new
                {
                    viewSpecificElementIds = analysis.ViewSpecific.Select(e => ToolHelpers.GetElementIdValue(e.Id)).ToArray(),
                    groupedMemberIds = analysis.GroupedMembers.Select(e => ToolHelpers.GetElementIdValue(e.Id)).Take(sampleLimit).ToArray(),
                    constrainedWallIds = analysis.ConstrainedWalls.Select(e => ToolHelpers.GetElementIdValue(e.Id)).Take(sampleLimit).ToArray()
                },
                candidates = includeDetails ? analysis.Copyable.Take(sampleLimit).Select(ElementInfo).ToList() : null
            });

        if (analysis.Copyable.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No copyable model elements were found on the source level");

        using var group = new TransactionGroup(doc, "RiveTT: Duplicate Storey");
        group.Start();
        try
        {
            if (upperLevels.Count > 0)
            {
                using var moveTx = new Transaction(doc, "RiveTT: Shift Upper Levels");
                var moveFailures = TransactionFailureHandling.FromInput(moveTx, input);
                moveTx.Start();
                ElementTransformUtils.MoveElements(doc, upperLevels.Select(level => level.Id).ToList(),
                    new XYZ(0, 0, shiftUpperMm / MmPerFoot));
                if (moveTx.Commit() != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return TransactionFailureHandling.ToFailure(moveFailures,
                        "Upper-level shift was rolled back",
                        "Detach blocking constraints and groups, then retry the preview before execution.");
                }
            }

            Level targetLevel;
            var existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(level => Math.Abs(level.Elevation - targetElevationFt) < 1e-6);
            using (var levelTx = new Transaction(doc, "RiveTT: Create Target Level"))
            {
                var levelFailures = TransactionFailureHandling.FromInput(levelTx, input);
                levelTx.Start();
                targetLevel = existing ?? Level.Create(doc, targetElevationFt);
                if (existing == null) targetLevel.Name = targetName;
                if (levelTx.Commit() != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return TransactionFailureHandling.ToFailure(levelFailures,
                        "Target level creation was rolled back",
                        "Choose a unique level name and elevation.");
                }
            }

            ICollection<ElementId> copiedIds;
            var reboundLevelParameters = 0;
            using (var copyTx = new Transaction(doc, "RiveTT: Copy Storey Elements"))
            {
                var copyFailures = TransactionFailureHandling.FromInput(copyTx, input);
                copyTx.Start();
                copiedIds = ElementTransformUtils.CopyElements(doc,
                    analysis.Copyable.Select(element => element.Id).ToList(),
                    new XYZ(0, 0, deltaFt));

                foreach (var copiedElement in copiedIds.Select(doc.GetElement).Where(element => element != null).Cast<Element>())
                {
                    reboundLevelParameters += RebindLevelParameters(copiedElement, source.Id, targetLevel.Id);
                    if (copiedElement is Wall copiedWall)
                    {
                        var baseConstraint = copiedWall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                        if (baseConstraint != null && !baseConstraint.IsReadOnly &&
                            baseConstraint.AsElementId() != targetLevel.Id)
                        {
                            baseConstraint.Set(targetLevel.Id);
                            reboundLevelParameters++;
                        }
                        if (targetTopLevelId > 0)
                        {
                            var topConstraint = copiedWall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                            if (topConstraint != null && !topConstraint.IsReadOnly)
                                topConstraint.Set(ToolHelpers.ToElementId(targetTopLevelId));
                        }
                    }
                }

                if (copyTx.Commit() != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return TransactionFailureHandling.ToFailure(copyFailures,
                        "Storey element copy was rolled back",
                        "Exclude view-specific or constrained categories and retry in smaller batches.");
                }
            }

            if (group.Assimilate() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "The duplicate_storey transaction group could not be assimilated");

            return CortexResult<object>.Ok(new
            {
                sourceLevelId = ToolHelpers.GetElementIdValue(source.Id),
                targetLevelId = ToolHelpers.GetElementIdValue(targetLevel.Id),
                targetLevelName = targetLevel.Name,
                targetElevationMm = targetLevel.Elevation * MmPerFoot,
                usedExistingLevel = existing != null,
                processed = analysis.Copyable.Count,
                copied = copiedIds.Count,
                reboundLevelParameters,
                skipped = analysis.ViewSpecific.Count + analysis.GroupedMembers.Count,
                copiedElementIds = includeDetails
                    ? copiedIds.Take(sampleLimit).Select(ToolHelpers.GetElementIdValue).ToArray()
                    : null,
                movedUpperLevelIds = upperLevels.Select(level => ToolHelpers.GetElementIdValue(level.Id)).ToArray()
            });
        }
        catch (Exception ex)
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"duplicate_storey failed and was rolled back: {ex.Message}",
                suggestion: "Run dryRun again with fewer categories and inspect grouped or constrained elements.",
                context: new Dictionary<string, object>
                {
                    ["warnings"] = Array.Empty<string>(),
                    ["errors"] = new[] { ex.Message },
                    ["rolledBack"] = true,
                    ["failedElementIds"] = Array.Empty<long>(),
                    ["repairHints"] = new[] { "Exclude blocking categories or detach constraints first." }
                });
        }
    }

    private static CandidateAnalysis AnalyzeCandidates(Document doc, Level source,
        List<string> categories, bool copyGroups)
    {
        var all = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
        var onLevel = all.Where(element => IsOnLevel(element, source.Id)).ToList();
        if (categories.Count > 0)
            onLevel = onLevel.Where(element => element.Category != null &&
                categories.Any(category => CategoryResolver.CategoryMatches(doc, element, category))).ToList();

        var viewSpecific = onLevel.Where(element => element.ViewSpecific).ToList();
        var groupedMembers = onLevel.Where(element => element.GroupId != ElementId.InvalidElementId && element is not Group).ToList();
        var groupIds = groupedMembers.Select(element => element.GroupId).Distinct().ToHashSet();
        var groupInstances = copyGroups
            ? groupIds.Select(doc.GetElement).OfType<Group>().Where(group => IsOnLevel(group, source.Id)).ToList()
            : new List<Group>();
        var copyable = onLevel.Where(element => !element.ViewSpecific && element is not Level &&
                                               element.GroupId == ElementId.InvalidElementId && element is not Group)
            .Concat(groupInstances).GroupBy(element => element.Id).Select(group => group.First()).ToList();
        var constrainedWalls = onLevel.OfType<Wall>()
            .Where(wall =>
            {
                var top = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();
                return (top != null && top != ElementId.InvalidElementId) ||
                       wall.GetAttachmentIds(AttachmentLocation.Top).Count > 0 ||
                       wall.GetAttachmentIds(AttachmentLocation.Base).Count > 0;
            }).ToList();
        return new CandidateAnalysis(copyable, viewSpecific, groupedMembers, groupInstances,
            constrainedWalls);
    }

    private static int RebindLevelParameters(Element element, ElementId sourceLevelId,
        ElementId targetLevelId)
    {
        var changed = 0;
        foreach (var bip in new[]
                 {
                     BuiltInParameter.FAMILY_LEVEL_PARAM,
                     BuiltInParameter.LEVEL_PARAM,
                     BuiltInParameter.SCHEDULE_LEVEL_PARAM
                 })
        {
            var parameter = element.get_Parameter(bip);
            if (parameter?.StorageType != StorageType.ElementId || parameter.IsReadOnly ||
                parameter.AsElementId() != sourceLevelId) continue;
            parameter.Set(targetLevelId);
            changed++;
        }
        return changed;
    }

    private static bool IsOnLevel(Element element, ElementId levelId)
    {
        if (element.LevelId == levelId) return true;
        foreach (var bip in new[]
                 {
                     BuiltInParameter.WALL_BASE_CONSTRAINT,
                     BuiltInParameter.FAMILY_LEVEL_PARAM,
                     BuiltInParameter.LEVEL_PARAM,
                     BuiltInParameter.SCHEDULE_LEVEL_PARAM
                 })
        {
            var parameter = element.get_Parameter(bip);
            if (parameter?.StorageType == StorageType.ElementId && parameter.AsElementId() == levelId)
                return true;
        }
        return false;
    }

    private static Level? ResolveLevel(Document doc, long? id, string? name)
    {
        if (id is > 0 && doc.GetElement(ToolHelpers.ToElementId(id.Value)) is Level byId) return byId;
        return string.IsNullOrWhiteSpace(name) ? null :
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(level => level.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static object LevelInfo(Level level) => new
    {
        levelId = ToolHelpers.GetElementIdValue(level.Id), level.Name,
        elevationMm = level.Elevation * MmPerFoot
    };

    private static object ElementInfo(Element element) => new
    {
        elementId = ToolHelpers.GetElementIdValue(element.Id),
        category = element.Category?.Name,
        elementType = element.GetType().Name,
        grouped = element.GroupId != ElementId.InvalidElementId,
        element.Pinned
    };

    private sealed record CandidateAnalysis(
        List<Element> Copyable, List<Element> ViewSpecific,
        List<Element> GroupedMembers, List<Group> GroupInstances,
        List<Wall> ConstrainedWalls);
}

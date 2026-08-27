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
/// Adds or removes members of a model group.
///
/// The Revit API has NO equivalent of the interactive "Edit Group" mode — that
/// is a documented API gap, not an oversight of this connector. The only
/// supported path is ungroup / modify / regroup, which is exactly what this tool
/// does, in one transaction, with the consequences reported rather than hidden:
///
///   * the group TYPE is recreated, so its id changes;
///   * other instances of the original type are NOT updated (Revit cannot
///     propagate a member change), so the tool refuses a multi-instance type
///     unless the caller explicitly accepts that;
///   * a mirrored or rotated instance may not come back with the same
///     transform, which is why the placement point is reported before and after.
///
/// The alternative — deleting or modifying an element inside a group without
/// ungrouping — was measured on a real model: the API accepts it and does NOT
/// propagate. After deleting one member, the edited instance reported 26 members
/// while its 51 siblings still reported 27, Revit still listing them as a single
/// type. The UI arbitrates that case through a dialog; the API leaves the model
/// divergent, which Autodesk reports as crash-prone on large models. delete_element
/// therefore refuses a multi-instance group member unless the caller opts in.
/// </summary>
[ToolSafety(false, true)]
public sealed class EditGroupMembersTool : ICortexTool
{
    public string Name => "edit_group_members";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Adds or removes members of a model group. REMOVING only performs Revit's exclusion: the members " +
        "leave THIS instance, the type and its other instances are untouched, and the instance is renamed " +
        "\"(membre exclu)\" — reversible from the ribbon. ADDING requires rebuilding the group " +
        "(ungroup/regroup): a NEW type is created and the other instances keep the old definition, so a " +
        "multi-instance type is refused unless allowMultiInstance=true. Each instance owns its own copies of " +
        "the members: pass ids read from THAT instance. Preview with dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var groupId = input["groupId"]?.Value<long>() ?? 0;
        var addIds = input["addElementIds"]?.ToObject<long[]>() ?? Array.Empty<long>();
        var removeIds = input["removeElementIds"]?.ToObject<long[]>() ?? Array.Empty<long>();
        var newTypeName = input["newTypeName"]?.Value<string>();
        var allowMultiInstance = input["allowMultiInstance"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (groupId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "groupId is required",
                suggestion: "List the model groups with manage_model_groups.");

        if (addIds.Length == 0 && removeIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide addElementIds and/or removeElementIds",
                suggestion: "Nothing to change otherwise; use manage_model_groups to inspect the group.");

        if (doc.GetElement(ToolHelpers.ToElementId(groupId)) is not Group group)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Element {groupId} is not a model group instance");

        var groupType = group.GroupType;
        var instanceCount = 0;
        try { instanceCount = groupType?.Groups?.Size ?? 0; } catch { }

        var originalTypeName = groupType?.Name ?? group.Name;
        var currentMembers = group.GetMemberIds().ToList();
        var placement = (group.Location as LocationPoint)?.Point;

        // Resolve the requested changes against reality before touching anything.
        var memberSet = new HashSet<ElementId>(currentMembers);
        var notInGroup = new List<long>();
        var alreadyInGroup = new List<long>();
        var invalid = new List<long>();
        var groupedElsewhere = new List<long>();

        foreach (var rawId in removeIds)
        {
            var id = ToolHelpers.ToElementId(rawId);
            if (!memberSet.Contains(id)) notInGroup.Add(rawId);
        }

        foreach (var rawId in addIds)
        {
            var id = ToolHelpers.ToElementId(rawId);
            var element = doc.GetElement(id);
            if (element == null)
            {
                invalid.Add(rawId);
                continue;
            }

            if (memberSet.Contains(id))
            {
                alreadyInGroup.Add(rawId);
                continue;
            }

            // An element that already belongs to another group cannot join this one.
            if (element.GroupId != ElementId.InvalidElementId &&
                element.GroupId != group.Id)
            {
                groupedElsewhere.Add(rawId);
            }
        }

        if (invalid.Count > 0 || groupedElsewhere.Count > 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Some elements cannot be added" +
                (invalid.Count > 0 ? $"; not found: {string.Join(", ", invalid)}" : "") +
                (groupedElsewhere.Count > 0
                    ? $"; already members of another group: {string.Join(", ", groupedElsewhere)}"
                    : ""),
                suggestion: "Ungroup the other group first, or drop those ids from addElementIds.",
                context: new Dictionary<string, object>
                {
                    ["invalidIds"] = invalid,
                    ["groupedElsewhereIds"] = groupedElsewhere
                });

        var plannedMembers = new HashSet<ElementId>(currentMembers);
        foreach (var rawId in removeIds) plannedMembers.Remove(ToolHelpers.ToElementId(rawId));
        foreach (var rawId in addIds) plannedMembers.Add(ToolHelpers.ToElementId(rawId));

        if (plannedMembers.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "The change would leave the group empty",
                suggestion: "Use manage_model_groups to ungroup it instead.");

        // Removal only: Revit's own answer is EXCLUSION — drop those elements from
        // this instance and leave the type alone. That keeps the type id, the other
        // instances and their own copies, and a human can restore them from the
        // ribbon. Rebuilding the group through ungroup/regroup is only needed to ADD
        // a member, which the API cannot do in place.
        var exclusionOnly = addIds.Length == 0 && removeIds.Length > 0;
        if (exclusionOnly)
        {
            var toExclude = removeIds
                .Where(rawId => memberSet.Contains(ToolHelpers.ToElementId(rawId)))
                .ToList();

            if (toExclude.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "None of the removeElementIds belong to this group instance",
                    suggestion: "Each instance owns its OWN copies of the members: read the ids from this " +
                                "instance (manage_model_groups includeMembers=true), not from a sibling.",
                    context: new Dictionary<string, object> { ["notInGroup"] = notInGroup });

            if (dryRun)
                return CortexResult<object>.Ok(new
                {
                    message = $"DryRun: {toExclude.Count} member(s) would be EXCLUDED from group " +
                              $"'{originalTypeName}' instance {groupId}. The type keeps its " +
                              $"{instanceCount} instance(s) and its definition; only this instance loses them.",
                    mode = "exclude",
                    groupId,
                    groupTypeName = originalTypeName,
                    instanceCount,
                    currentMemberCount = currentMembers.Count,
                    memberCountAfter = currentMembers.Count - toExclude.Count,
                    wouldExclude = toExclude,
                    ignoredNotInGroup = notInGroup,
                    restoreHint = "Select the instance in Revit and use Restore Excluded Members; the API " +
                                  "exposes no restore call."
                });

            try
            {
                using var excludeTx = new Transaction(doc, "RiveTT: Exclude Group Members");
                var excludeFailures = TransactionFailureHandling.SuppressWarnings(excludeTx);
                excludeTx.Start();
                doc.Delete(toExclude.Select(ToolHelpers.ToElementId).ToList());

                if (excludeTx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the exclusion: {TransactionFailureHandling.Describe(excludeFailures)}");
            }
            catch (Exception exception)
            {
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    $"Failed to exclude the member(s): {exception.Message}");
            }

            var remaining = (doc.GetElement(ToolHelpers.ToElementId(groupId)) as Group)?
                .GetMemberIds().Count ?? 0;

            return CortexResult<object>.Ok(new
            {
                message = $"Excluded {toExclude.Count} member(s) from instance {groupId} of " +
                          $"'{originalTypeName}'. The group type and its other instances are unchanged.",
                mode = "exclude",
                groupId,
                groupTypeId = ToolHelpers.GetElementIdValue(groupType?.Id ?? ElementId.InvalidElementId),
                groupTypeName = originalTypeName,
                instanceCount,
                excludedIds = toExclude,
                memberCountAfter = remaining,
                typeRecreated = false,
                warnings = new[]
                {
                    "Revit marks this instance's name \"(membre exclu)\". Restore the members by selecting " +
                    "the instance in Revit and using Restore Excluded Members — the API has no restore call."
                }
            });
        }

        if (instanceCount > 1 && !allowMultiInstance)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Group type '{originalTypeName}' has {instanceCount} instances. Editing members recreates the " +
                "type, and Revit cannot propagate the change to the other instances: they would keep the old " +
                "definition.",
                suggestion: "Set allowMultiInstance=true to accept that divergence, or edit the group in the " +
                            "Revit UI where propagation is handled by the application.",
                context: new Dictionary<string, object>
                {
                    ["instanceCount"] = instanceCount,
                    ["groupTypeId"] = ToolHelpers.GetElementIdValue(groupType?.Id ?? ElementId.InvalidElementId)
                });

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DryRun: group '{originalTypeName}' would go from {currentMembers.Count} to " +
                          $"{plannedMembers.Count} member(s) via ungroup/regroup. A NEW group type is created.",
                groupId,
                groupTypeName = originalTypeName,
                instanceCount,
                currentMemberCount = currentMembers.Count,
                plannedMemberCount = plannedMembers.Count,
                wouldAdd = addIds.Where(id => !alreadyInGroup.Contains(id)).ToList(),
                wouldRemove = removeIds.Where(id => !notInGroup.Contains(id)).ToList(),
                ignoredAlreadyInGroup = alreadyInGroup,
                ignoredNotInGroup = notInGroup,
                placementMm = placement == null
                    ? null
                    : new { x = placement.X * MmPerFoot, y = placement.Y * MmPerFoot, z = placement.Z * MmPerFoot },
                members = Describe(doc, currentMembers.Take(30)),
                warnings = BuildWarnings(instanceCount)
            });
        }

        try
        {
            using var tx = new Transaction(doc, "RiveTT: Edit Group Members");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            // UngroupMembers releases the members of THIS instance only; the type
            // survives if other instances remain.
            var released = group.UngroupMembers().ToList();

            var finalMembers = new HashSet<ElementId>(released);
            foreach (var rawId in removeIds) finalMembers.Remove(ToolHelpers.ToElementId(rawId));
            foreach (var rawId in addIds) finalMembers.Add(ToolHelpers.ToElementId(rawId));
            finalMembers.RemoveWhere(id => doc.GetElement(id) == null);

            if (finalMembers.Count == 0)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No valid member left after the change; the group was not modified.");
            }

            var newGroup = doc.Create.NewGroup(finalMembers.ToList());
            if (newGroup == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit refused to create the new group from the resulting member set.");
            }

            // Keep the original name when possible. It is still taken when other
            // instances of the old type remain, so fall back to an explicit suffix
            // rather than failing the whole operation.
            var appliedName = newTypeName ?? originalTypeName;
            string? nameError = null;
            try
            {
                newGroup.GroupType.Name = appliedName;
            }
            catch
            {
                try
                {
                    appliedName = $"{appliedName} (MCP)";
                    newGroup.GroupType.Name = appliedName;
                }
                catch (Exception exception)
                {
                    nameError = exception.Message;
                    appliedName = newGroup.GroupType.Name;
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the group edit: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Some members may be pinned, in another group, or in a different workset.");

            var newPlacement = (newGroup.Location as LocationPoint)?.Point;

            return CortexResult<object>.Ok(new
            {
                message = $"Group rebuilt as '{appliedName}' with {finalMembers.Count} member(s) " +
                          $"(was {currentMembers.Count}).",
                newGroupId = ToolHelpers.GetElementIdValue(newGroup.Id),
                newGroupTypeId = ToolHelpers.GetElementIdValue(newGroup.GroupType.Id),
                newGroupTypeName = appliedName,
                previousGroupId = groupId,
                previousGroupTypeName = originalTypeName,
                memberCountBefore = currentMembers.Count,
                memberCountAfter = finalMembers.Count,
                nameError,
                placementBeforeMm = placement == null
                    ? null
                    : new { x = placement.X * MmPerFoot, y = placement.Y * MmPerFoot, z = placement.Z * MmPerFoot },
                placementAfterMm = newPlacement == null
                    ? null
                    : new { x = newPlacement.X * MmPerFoot, y = newPlacement.Y * MmPerFoot, z = newPlacement.Z * MmPerFoot },
                otherInstancesNotUpdated = Math.Max(0, instanceCount - 1),
                warnings = BuildWarnings(instanceCount)
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to edit the group members: {exception.Message}",
                suggestion: "Check that no member is pinned or attached to another group, and that the group " +
                            "instance is not in a design option that forbids the change.");
        }
    }

    private static string[] BuildWarnings(int instanceCount)
    {
        var warnings = new List<string>
        {
            "Ungroup/regroup is the only member edit the Revit API supports: the group type id changes, and " +
            "a mirrored or rotated instance may not return with the same transform."
        };

        if (instanceCount > 1)
            warnings.Add($"{instanceCount - 1} other instance(s) of the original type keep the old definition — " +
                         "Revit cannot propagate a member change.");

        return warnings.ToArray();
    }

    private static List<object> Describe(Document doc, IEnumerable<ElementId> ids)
    {
        return ids
            .Select(doc.GetElement)
            .Where(element => element != null)
            .Select(element => (object)new
            {
                elementId = ToolHelpers.GetElementIdValue(element!.Id),
                name = element!.Name,
                category = element.Category?.Name,
                categoryBic = CategoryResolver.DescribeBuiltInCategory(element.Category)
            })
            .ToList();
    }
}

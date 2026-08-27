using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

[ToolSafety(false, true)]
public sealed class ManageModelGroupsTool : ICortexTool
{
    public string Name => "manage_model_groups";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Inventory model groups, duplicate a group type for isolated changes, swap selected instances, or ungroup selected instances.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");
        var action = (input["action"]?.Value<string>() ?? "inventory").ToLowerInvariant();
        return action switch
        {
            "inventory" => Inventory(doc, input),
            "duplicate_type" => DuplicateType(doc, input),
            "ungroup" => Ungroup(doc, input),
            _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action must be inventory, duplicate_type, or ungroup")
        };
    }

    private static CortexResult<object> Inventory(Document doc, JObject input)
    {
        var includeMembers = input["includeMembers"]?.Value<bool>() ?? false;
        var sampleLimit = Math.Clamp(input["sampleLimit"]?.Value<int>() ?? 20, 0, 200);
        // groupTypeId was published and ignored here: asking for one type returned
        // all twenty, and includeMembers then multiplied the response for nothing.
        var onlyTypeId = input["groupTypeId"]?.Value<long>() ?? 0;
        var types = new FilteredElementCollector(doc).OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .Where(type => type.Category?.BuiltInCategory == BuiltInCategory.OST_IOSModelGroups)
            .Where(type => onlyTypeId <= 0 ||
                           ToolHelpers.GetElementIdValue(type.Id) == onlyTypeId)
            .Select(type =>
            {
                var instances = type.Groups.Cast<Group>().ToList();

                // Per-instance member counts, because instances of ONE type are
                // allowed to differ: excluding a member removes it from that instance
                // alone (Revit renames it "(membre exclu)"), and a grouped wall can be
                // taller in one instance through its own level constraints. Reading
                // only the first instance hid that, and could even report an
                // incomplete member list as if it were the definition.
                var perInstance = instances
                    .Select(instance => new
                    {
                        Instance = instance,
                        Members = instance.GetMemberIds()
                    })
                    .ToList();

                var fullest = perInstance.Count == 0
                    ? null
                    : perInstance.OrderByDescending(entry => entry.Members.Count).First();
                var fullCount = fullest?.Members.Count ?? 0;

                var instanceDetails = perInstance
                    .Select(entry => new
                    {
                        groupId = ToolHelpers.GetElementIdValue(entry.Instance.Id),
                        instanceName = entry.Instance.Name,
                        memberCount = entry.Members.Count,
                        excludedCount = Math.Max(0, fullCount - entry.Members.Count),
                        // Two independent signals: fewer members than the fullest
                        // instance, and the suffix Revit puts on the instance name.
                        hasExcludedMembers = entry.Members.Count < fullCount ||
                                             !string.Equals(entry.Instance.Name, type.Name,
                                                 StringComparison.Ordinal)
                    })
                    .ToList();

                var sample = fullest?.Instance;
                var memberIds = fullest?.Members ?? (IList<ElementId>)Array.Empty<ElementId>();
                return new
                {
                    groupTypeId = ToolHelpers.GetElementIdValue(type.Id),
                    name = type.Name,
                    instanceCount = instances.Count,
                    groupIds = instances.Select(g => ToolHelpers.GetElementIdValue(g.Id)).ToArray(),
                    // The full definition, read from the instance that has the most
                    // members — not from the first one, which may have exclusions.
                    memberCount = memberIds.Count,
                    instancesWithExclusions = instanceDetails.Count(detail => detail.hasExcludedMembers),
                    instances = instanceDetails,
                    members = includeMembers
                        ? memberIds.Take(sampleLimit).Select(id =>
                        {
                            var e = doc.GetElement(id);
                            return new { elementId = ToolHelpers.GetElementIdValue(id), category = e?.Category?.Name, name = e?.Name };
                        }).ToList()
                        : null
                };
            }).ToList();
        return CortexResult<object>.Ok(new
        {
            groupTypeCount = types.Count,
            groupInstanceCount = types.Sum(t => t.instanceCount),
            groupTypes = types
        });
    }

    private static CortexResult<object> DuplicateType(Document doc, JObject input)
    {
        var typeId = input["groupTypeId"]?.Value<long>() ?? 0;
        var name = input["newName"]?.Value<string>();
        var type = doc.GetElement(ToolHelpers.ToElementId(typeId)) as GroupType;
        if (type == null || string.IsNullOrWhiteSpace(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "A valid groupTypeId and newName are required");
        var groupIds = input["groupIds"]?.ToObject<List<long>>() ?? new List<long>();
        var dryRun = ToolHelpers.GetDryRun(input);
        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                sourceGroupTypeId = typeId,
                sourceName = type.Name,
                newName = name,
                groupsToSwap = groupIds
            });

        using var tx = new Transaction(doc, "RiveTT: Duplicate Model Group Type");
        var failures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var duplicate = type.Duplicate(name!) as GroupType;
        if (duplicate == null)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "Revit did not create the duplicated group type");
        }
        var swapped = new List<long>();
        foreach (var id in groupIds)
        {
            if (doc.GetElement(ToolHelpers.ToElementId(id)) is Group group && group.GroupType.Id == type.Id)
            {
                group.GroupType = duplicate;
                swapped.Add(id);
            }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return TransactionFailureHandling.ToFailure(failures,
                "Group type duplication was rolled back",
                "Check group consistency and retry with fewer instances.");
        return CortexResult<object>.Ok(new
        {
            sourceGroupTypeId = typeId,
            newGroupTypeId = ToolHelpers.GetElementIdValue(duplicate.Id),
            newName = duplicate.Name,
            swappedGroupIds = swapped
        });
    }

    private static CortexResult<object> Ungroup(Document doc, JObject input)
    {
        var groupIds = input["groupIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (groupIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "groupIds is required");
        var groups = groupIds.Distinct().Select(id => doc.GetElement(ToolHelpers.ToElementId(id)) as Group)
            .Where(g => g != null).Cast<Group>().ToList();
        var preview = groups.Select(group => new
        {
            groupId = ToolHelpers.GetElementIdValue(group.Id),
            groupType = group.GroupType.Name,
            attached = group.IsAttached,
            memberIds = group.GetMemberIds().Select(ToolHelpers.GetElementIdValue).ToArray()
        }).ToList();
        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new { dryRun = true, processed = groups.Count, groups = preview });

        using var tx = new Transaction(doc, "RiveTT: Ungroup Model Groups");
        var failures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var released = new HashSet<long>();
        var attachedCount = groups.Count(group => group.IsAttached);
        var groupsToUngroup = groups.Where(group => !group.IsAttached).ToList();
        foreach (var group in groupsToUngroup)
            foreach (var id in group.UngroupMembers()) released.Add(ToolHelpers.GetElementIdValue(id));
        if (tx.Commit() != TransactionStatus.Committed)
            return TransactionFailureHandling.ToFailure(failures,
                "Ungroup operation was rolled back",
                "Do not include attached detail groups and resolve inconsistent group types first.");
        return CortexResult<object>.Ok(new
        {
            processed = groups.Count,
            ungrouped = groupsToUngroup.Count,
            skippedAttached = attachedCount,
            releasedElementIds = released.OrderBy(id => id).ToArray()
        });
    }
}

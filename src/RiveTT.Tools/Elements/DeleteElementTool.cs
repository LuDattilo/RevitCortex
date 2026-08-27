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

/// <summary>
/// Deletes one or more elements from the model.
/// Defaults to dryRun=true for safety — preview what would be deleted before committing.
/// Mirrors the fork's DeleteElementEventHandler logic.
/// </summary>
[ToolSafety(false, true)]
public class DeleteElementTool : ICortexTool
{
    public string Name => "delete_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Deletes one or more elements. Defaults to dryRun=true: the preview reports the real cascade and any " +
        "group membership. Deleting a group MEMBER performs Revit's exclusion — the element leaves that " +
        "instance only, the type and the other instances are untouched, and the instance is renamed " +
        "\"(membre exclu)\". That is legitimate and reversible from the Revit ribbon; the response says so.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Parse inputs
        var elementIdsToken = input["elementIds"];
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (elementIdsToken == null || elementIdsToken.Type == JTokenType.Null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Provide an array of element ID numbers: {\"elementIds\": [123, 456], \"dryRun\": true}");

        long[] rawIds;
        try
        {
            rawIds = elementIdsToken.ToObject<long[]>() ?? Array.Empty<long>();
        }
        catch
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds must be an array of numbers");
        }

        if (rawIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds array must not be empty");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // Validate IDs and separate valid from invalid
        var validElements = new List<(ElementId Id, Element Elem)>();
        var invalidIds = new List<long>();

        foreach (var rawId in rawIds)
        {
            var elementId = new ElementId(rawId);
            var elem = doc.GetElement(elementId);
            if (elem != null)
                validElements.Add((elementId, elem));
            else
                invalidIds.Add(rawId);
        }

        // Deleting a MEMBER of a grouped instance performs Revit's EXCLUSION: the
        // element is removed from that instance only, the group type is untouched, the
        // other instances keep their own copies, and Revit marks the instance name
        // "(membre exclu)". This is a first-class Revit feature — instances of one type
        // are allowed to differ, which is also how a grouped wall can be taller in one
        // instance through its level constraints. It is reversible from the ribbon
        // ("Restore Excluded Members"), so it is NOT refused; it is reported.
        //
        // Measured on a real model: after excluding one member, that instance reported
        // 26 members while a sibling still reported 27, both still listed under the
        // same type with the same instance count.
        var groupMembers = new List<object>();
        var excludedIds = new List<long>();

        foreach (var (_, element) in validElements)
        {
            if (element is Group) continue;   // deleting a whole group instance is fine
            var groupId = element.GroupId;
            if (groupId == ElementId.InvalidElementId) continue;

            var group = doc.GetElement(groupId) as Group;
            var groupType = group?.GroupType;
            var instanceCount = 0;
            try { instanceCount = groupType?.Groups?.Size ?? 0; } catch { }

            groupMembers.Add(new
            {
                elementId = GetElementIdLong(element),
                name = element.Name,
                groupId = GetElementIdLongFromId(groupId),
                groupTypeName = groupType?.Name,
                instancesOfThatType = instanceCount,
                effect = "excluded from this group instance only; the type and the other instances are untouched"
            });

            excludedIds.Add(GetElementIdLong(element));
        }

        // Build preview info for each valid element
        var validInfo = validElements.Select(ve => new
        {
            elementId = GetElementIdLong(ve.Elem),
            name      = ve.Elem.Name,
            category  = ve.Elem.Category?.Name,
            // French Revit calls the viewport category "Fenêtres " — the same label
            // as windows. The OST_ code is the only unambiguous identification.
            categoryBic = CategoryResolver.DescribeBuiltInCategory(ve.Elem.Category),
            uniqueId  = ve.Elem.UniqueId
        }).ToList();

        // DryRun — return preview without touching the model
        if (dryRun)
        {
            // Probe the real cascade with the tx-sandbox pattern: doc.Delete returns
            // every element the deletion would drag along (dependent views, tags,
            // sketches...), RollBack discards the change. Without this, previewing a
            // Level deletion reported 1 element while the real delete removed ~100.
            var dependentCount = 0;
            List<object>? dependentSample = null;
            string? cascadePreviewError = null;

            if (validElements.Count > 0)
            {
                try
                {
                    List<ElementId> wouldDeleteIds;
                    using (var probeTx = new Transaction(doc, "RiveTT: Delete Preview"))
                    {
                        TransactionFailureHandling.SuppressWarnings(probeTx);
                        probeTx.Start();
                        wouldDeleteIds = doc.Delete(validElements.Select(ve => ve.Id).ToList()).ToList();
                        probeTx.RollBack();
                    }

                    // Elements are restored after RollBack, so names resolve again.
                    var requested = new HashSet<ElementId>(validElements.Select(ve => ve.Id));
                    var dependents = wouldDeleteIds.Where(id => !requested.Contains(id)).ToList();
                    dependentCount = dependents.Count;
                    dependentSample = dependents.Take(20)
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null)
                        .Select(e => (object)new
                        {
                            elementId = GetElementIdLong(e!),
                            name = e!.Name,
                            category = e.Category?.Name
                        })
                        .ToList();
                }
                catch (Exception ex)
                {
                    cascadePreviewError = ex.Message;
                }
            }

            return CortexResult<object>.Ok(new
            {
                message = cascadePreviewError == null
                    ? $"DryRun: {validElements.Count} element(s) requested; deletion would cascade to {dependentCount} dependent element(s) ({validElements.Count + dependentCount} total). Set dryRun=false to execute."
                    : $"DryRun: {validElements.Count} element(s) would be deleted (cascade preview unavailable). Set dryRun=false to execute.",
                dryRun     = true,
                wouldDelete = validInfo,
                dependentCount,
                totalWouldDelete = validElements.Count + dependentCount,
                dependentSample,
                cascadePreviewError,
                invalidIds,
                validCount  = validElements.Count,
                invalidCount = invalidIds.Count,
                groupMembers,
                groupExclusionIds = excludedIds,
                warnings = excludedIds.Count == 0
                    ? Array.Empty<string>()
                    : new[]
                    {
                        $"{excludedIds.Count} element(s) are group members: deleting them EXCLUDES them from " +
                        "their own instance (Revit's own behaviour, reversible). The group type, the other " +
                        "instances and their copies of those elements are untouched."
                    }
            });
        }

        // Actual deletion
        if (validElements.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No valid elements to delete",
                context: invalidIds.Count > 0
                    ? new Dictionary<string, object> { ["invalidIds"] = invalidIds }
                    : null);

        if (!session.RequestConfirmation("delete", validElements.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            ICollection<ElementId> deletedIds;
            var cascadeInfo = new List<object>();
            using var tx = new Transaction(doc, "RiveTT: Delete Elements");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            try
            {
                // Probe the cascade first, inside a rolled-back sub-transaction, so the
                // implicitly removed elements (tags, sketches, dependent views) can be
                // named while they still exist. deletedCount used to report 2 while
                // deletedElements listed 1, with no way to know what the extra was.
                var requestedIds = validElements.Select(ve => ve.Id).ToList();
                try
                {
                    using var probe = new SubTransaction(doc);
                    probe.Start();
                    var probed = doc.Delete(requestedIds);
                    probe.RollBack();

                    var requestedSet = new HashSet<ElementId>(requestedIds);
                    cascadeInfo = probed
                        .Where(id => !requestedSet.Contains(id))
                        .Select(id => doc.GetElement(id))
                        .Where(element => element != null)
                        .Select(element => (object)new
                        {
                            elementId = GetElementIdLong(element!),
                            name = element!.Name,
                            category = element.Category?.Name,
                            categoryBic = CategoryResolver.DescribeBuiltInCategory(element.Category),
                            reason = "removed as a dependency of a requested element"
                        })
                        .ToList();
                }
                catch
                {
                    // A probe failure must not block the delete the caller asked for.
                }

                deletedIds = doc.Delete(requestedIds);
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the deletion: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            var deletedElementIds = deletedIds.Select(GetElementIdLongFromId).ToList();

            return CortexResult<object>.Ok(new
            {
                message      = cascadeInfo.Count == 0
                    ? $"Deleted {deletedIds.Count} element(s) successfully."
                    : $"Deleted {deletedIds.Count} element(s): {validInfo.Count} requested plus " +
                      $"{cascadeInfo.Count} dependent element(s).",
                dryRun       = false,
                deletedCount = deletedIds.Count,
                // deletedCount == requestedElements.Count + cascadedElements.Count,
                // and deletedElementIds is the full list Revit actually removed.
                deletedElementIds,
                requestedElements = validInfo,
                cascadedElements = cascadeInfo,
                cascadedCount = cascadeInfo.Count,
                invalidIds,
                invalidCount = invalidIds.Count,
                groupMembers,
                groupExclusionIds = excludedIds,
                warnings = excludedIds.Count == 0
                    ? Array.Empty<string>()
                    : new[]
                    {
                        $"{excludedIds.Count} element(s) were group members and are now EXCLUDED from their " +
                        "instance — Revit suffixes that instance's name with \"(membre exclu)\". Nothing else " +
                        "changed: same type, same instance count, other instances intact. To bring them back, " +
                        "select the instance in Revit and use Restore Excluded Members (the API exposes no " +
                        "restore call)."
                    }
            });
        }
        catch (Exception ex)
        {
            // Document.Delete throws a bare ArgumentException ("One or more of the
            // elementIds cannot be deleted") with no indication of which element or
            // why. Name the likely cause so the caller is not left guessing.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to delete elements: {ex.Message}",
                suggestion: ex is ArgumentException
                    ? "Revit refuses this deletion. Common causes: the element is the last sheet/view of its " +
                      "kind, it is pinned, it is referenced by another element (a viewport, a dimension, a " +
                      "group), or the document needs a regeneration after a previous delete. Delete the " +
                      "referencing elements first, or re-read the element to confirm it still exists."
                    : null,
                context: new Dictionary<string, object>
                {
                    ["requestedIds"] = validElements.Select(ve => GetElementIdLong(ve.Elem)).ToList(),
                    ["exceptionType"] = ex.GetType().Name
                });
        }
    }

    private static long GetElementIdLongFromId(ElementId id)
    {
        return id.Value;
    }

    private static long GetElementIdLong(Element elem)
    {
        return elem.Id.Value;
    }
}

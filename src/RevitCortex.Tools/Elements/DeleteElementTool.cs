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
/// Deletes one or more elements from the model.
/// Defaults to dryRun=true for safety — preview what would be deleted before committing.
/// Mirrors the fork's DeleteElementEventHandler logic.
/// </summary>
public class DeleteElementTool : ICortexTool
{
    public string Name => "delete_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Deletes one or more elements from the model. Defaults to dryRun=true for safety — preview what would be deleted before committing. Mirrors the fork's DeleteElementEventHandler logic.";
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
#if REVIT2024_OR_GREATER
            var elementId = new ElementId(rawId);
#else
            var elementId = new ElementId((int)rawId);
#endif
            var elem = doc.GetElement(elementId);
            if (elem != null)
                validElements.Add((elementId, elem));
            else
                invalidIds.Add(rawId);
        }

        // Build preview info for each valid element
        var validInfo = validElements.Select(ve => new
        {
            elementId = GetElementIdLong(ve.Elem),
            name      = ve.Elem.Name,
            category  = ve.Elem.Category?.Name,
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
                    using (var probeTx = new Transaction(doc, "RevitCortex: Delete Preview"))
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
                invalidCount = invalidIds.Count
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
            using var tx = new Transaction(doc, "RevitCortex: Delete Elements");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            try
            {
                deletedIds = doc.Delete(validElements.Select(ve => ve.Id).ToList());
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

            return CortexResult<object>.Ok(new
            {
                message      = $"Deleted {deletedIds.Count} element(s) successfully.",
                dryRun       = false,
                deletedCount = deletedIds.Count,
                deletedElements = validInfo,
                invalidIds,
                invalidCount = invalidIds.Count
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to delete elements: {ex.Message}");
        }
    }

    private static long GetElementIdLong(Element elem)
    {
#if REVIT2024_OR_GREATER
        return elem.Id.Value;
#else
        return elem.Id.IntegerValue;
#endif
    }
}

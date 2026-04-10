using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

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
            return CortexResult<object>.Ok(new
            {
                message    = $"DryRun: {validElements.Count} element(s) would be deleted. Set dryRun=false to execute.",
                dryRun     = true,
                wouldDelete = validInfo,
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
            tx.Start();
            try
            {
                deletedIds = doc.Delete(validElements.Select(ve => ve.Id).ToList());
                tx.Commit();
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

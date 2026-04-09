using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class ChangeElementTypeTool : ICortexTool
{
    public string Name => "change_element_type";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Provide [\"elementIds\": [123, 456]]");

        var targetTypeId   = input["targetTypeId"]?.Value<long?>() ?? 0;
        var targetTypeName = input["targetTypeName"]?.Value<string>() ?? "";
        var targetFamilyName = input["targetFamilyName"]?.Value<string>() ?? "";

        if (targetTypeId == 0 && string.IsNullOrWhiteSpace(targetTypeName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Specify at least targetTypeId or targetTypeName",
                suggestion: "Use targetTypeId for exact match, or targetTypeName + optional targetFamilyName for name-based lookup");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        try
        {
            // Resolve the target type ElementId
            var targetTypeElemId = ResolveTargetType(doc, targetTypeId, targetTypeName, targetFamilyName);
            if (targetTypeElemId == null || targetTypeElemId == ElementId.InvalidElementId)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Target type not found. targetTypeId={targetTypeId}, targetTypeName='{targetTypeName}', targetFamilyName='{targetFamilyName}'",
                    suggestion: "Verify the type exists in the document using ai_element_filter with includeTypes=true");

            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;

            using var tx = new Transaction(doc, "RevitCortex: Change Element Type");
            tx.Start();
            try
            {
                foreach (var id in elementIds)
                {
#if REVIT2024_OR_GREATER
                    var elemId = new ElementId(id);
#else
                    var elemId = new ElementId((int)id);
#endif
                    var element = doc.GetElement(elemId);
                    if (element == null)
                    {
                        results.Add(new { elementId = id, success = false, message = $"Element {id} not found" });
                        failCount++;
                        continue;
                    }

                    try
                    {
                        element.ChangeTypeId(targetTypeElemId);
                        successCount++;
                        results.Add(new { elementId = id, success = true, message = "Type changed successfully" });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { elementId = id, success = false, message = ex.Message });
                        failCount++;
                    }
                }

                if (successCount > 0)
                    tx.Commit();
                else
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                }
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            var resolvedType = doc.GetElement(targetTypeElemId);
            var resolvedTypeName = resolvedType?.Name ?? targetTypeName;

            return CortexResult<object>.Ok(new
            {
                message = $"Changed type for {successCount}/{elementIds.Length} elements to '{resolvedTypeName}'",
                targetTypeName = resolvedTypeName,
                successCount,
                failCount,
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Change element type failed: {ex.Message}");
        }
    }

    private static ElementId ResolveTargetType(Document doc, long targetTypeId, string targetTypeName, string targetFamilyName)
    {
        // Priority 1: by explicit ElementId
        if (targetTypeId > 0)
        {
#if REVIT2024_OR_GREATER
            var typeId = new ElementId(targetTypeId);
#else
            var typeId = new ElementId((int)targetTypeId);
#endif
            if (doc.GetElement(typeId) != null)
                return typeId;
        }

        // Priority 2 & 3: by name (with optional family name)
        if (!string.IsNullOrWhiteSpace(targetTypeName))
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsElementType();

            // Priority 2: exact type name + family name match
            if (!string.IsNullOrWhiteSpace(targetFamilyName))
            {
                var match = collector.Cast<ElementType>()
                    .FirstOrDefault(t =>
                        t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase) &&
                        t.FamilyName.Equals(targetFamilyName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Id;
            }

            // Priority 3: type name only
            var byName = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .FirstOrDefault(t => t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName.Id;
        }

        return ElementId.InvalidElementId;
    }
}

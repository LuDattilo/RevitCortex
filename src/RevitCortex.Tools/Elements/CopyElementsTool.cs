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
/// Copies elements within the same document, optionally between views.
/// Offsets are in mm and converted to internal units (feet).
/// Mirrors the fork's CopyElementsEventHandler logic.
/// </summary>
public class CopyElementsTool : ICortexTool
{
    public string Name => "copy_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIdsToken = input["elementIds"];
        if (elementIdsToken == null || elementIdsToken.Type == JTokenType.Null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Provide an array of element ID numbers: {\"elementIds\": [123, 456]}");

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

        var sourceViewId = input["sourceViewId"]?.Value<long?>() ?? 0;
        var targetViewId = input["targetViewId"]?.Value<long?>() ?? 0;
        var offsetX = input["offsetX"]?.Value<double>() ?? 0.0;
        var offsetY = input["offsetY"]?.Value<double>() ?? 0.0;
        var offsetZ = input["offsetZ"]?.Value<double>() ?? 0.0;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // MM → internal units (feet)
        var translation = new XYZ(offsetX / 304.8, offsetY / 304.8, offsetZ / 304.8);
        var transform = Transform.CreateTranslation(translation);

        var ids = rawIds.Select(ToElementId).ToList();

        try
        {
            ICollection<ElementId> copiedIds;

            if (sourceViewId > 0 || targetViewId > 0)
            {
                // View-to-view copy — both view IDs are required together
                if (sourceViewId <= 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "sourceViewId is required when targetViewId is provided");
                if (targetViewId <= 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "targetViewId is required when sourceViewId is provided");

                var sourceView = doc.GetElement(ToElementId(sourceViewId)) as View;
                var targetView = doc.GetElement(ToElementId(targetViewId)) as View;

                if (sourceView == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Source view with ID {sourceViewId} not found");
                if (targetView == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Target view with ID {targetViewId} not found");

                using var tx = new Transaction(doc, "RevitCortex: Copy Elements Between Views");
                tx.Start();
                try
                {
                    copiedIds = ElementTransformUtils.CopyElements(
                        sourceView, ids, targetView, transform, new CopyPasteOptions());
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    throw;
                }
            }
            else
            {
                // Simple in-place copy with optional offset
                using var tx = new Transaction(doc, "RevitCortex: Copy Elements");
                tx.Start();
                try
                {
                    copiedIds = ElementTransformUtils.CopyElements(doc, ids, translation);
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    throw;
                }
            }

            var copiedElements = copiedIds.Select(id =>
            {
                var elem = doc.GetElement(id);
                return (object)new
                {
#if REVIT2024_OR_GREATER
                    id = id.Value,
#else
                    id = id.IntegerValue,
#endif
                    name = elem?.Name ?? "",
                    category = elem?.Category?.Name ?? ""
                };
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                message = $"Copied {copiedIds.Count} element(s) successfully",
                copiedCount = copiedIds.Count,
                copiedElements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Copy elements failed: {ex.Message}");
        }
    }

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }
}

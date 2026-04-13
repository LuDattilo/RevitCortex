using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Tags IFC-imported elements that cannot be rebuilt as native Revit elements.
/// Sets a value in the Comments parameter to mark them for manual review.
/// </summary>
public class IfcTagUnreconstructableElementsTool : ICortexTool
{
    public string Name => "ifc_tag_unreconstructable_elements";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tag IFC elements that cannot be rebuilt, marking them for manual review";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var tagValue = input["tagValue"]?.Value<string>() ?? "IFC_UNRECONSTRUCTABLE";

        List<DirectShape> targets;
        if (elementIds != null && elementIds.Length > 0)
        {
            targets = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            // Tag all DirectShapes with unknown/mesh geometry or low rebuild confidence
            targets = IfcGeometryHelper.GetDirectShapes(doc!)
                .Where(ds =>
                {
                    var geomType = IfcGeometryHelper.DetectGeometryType(ds);
                    return geomType == "mesh" || geomType == "unknown";
                })
                .ToList();
        }

        if (targets.Count == 0)
            return CortexResult<object>.Ok(new
            {
                tagged = 0,
                message = "No elements to tag",
            });

        if (!session.RequestConfirmation("tag unreconstructable elements", targets.Count,
            $"Set Comments to '{tagValue}' on {targets.Count} elements"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        int tagged = 0;
        var results = new List<object>();

        using var tx = new Transaction(doc!, "RevitCortex: Tag Unreconstructable");
        tx.Start();

        foreach (var ds in targets)
        {
            try
            {
                var commentsParam = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentsParam != null && !commentsParam.IsReadOnly)
                {
                    commentsParam.Set(tagValue);
                    tagged++;
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "tagged",
                    });
                }
                else
                {
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "skipped",
                        reason = "Comments parameter not writable",
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    elementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            tagged,
            tagValue,
            total = targets.Count,
            results,
        });
    }
}

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
/// Creates a linear or radial array of elements by copying.
/// </summary>
public class CreateArrayTool : ICortexTool
{
    public string Name => "create_array";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>();
        if (elementIds == null || elementIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds array is required");

        var arrayType = input["arrayType"]?.Value<string>() ?? "linear";
        var count = input["count"]?.Value<int>() ?? 1;
        if (count <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "count must be > 0");

        try
        {
            var sourceIds = elementIds.Select(id =>
            {
#if REVIT2024_OR_GREATER
                return new ElementId(id);
#else
                return new ElementId((int)id);
#endif
            }).ToList();

            // Validate elements exist
            foreach (var eid in sourceIds)
            {
                if (doc.GetElement(eid) == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Element {GetIdLong(eid)} not found");
            }

            var createdElements = new List<object>();

            using var tx = new Transaction(doc, "RevitCortex: Create Array");
            tx.Start();

            if (arrayType == "radial")
            {
                var centerX = input["centerX"]?.Value<double>() ?? 0;
                var centerY = input["centerY"]?.Value<double>() ?? 0;
                var totalAngle = input["totalAngle"]?.Value<double>() ?? 360;

                var center = new XYZ(centerX / MmPerFoot, centerY / MmPerFoot, 0);
                var axis = Line.CreateBound(center, center + XYZ.BasisZ * 10);

                for (int i = 1; i <= count; i++)
                {
                    var angle = (totalAngle / (count + 1)) * i * Math.PI / 180.0;
                    var copied = ElementTransformUtils.CopyElements(doc, sourceIds, XYZ.Zero);
                    foreach (var copiedId in copied)
                    {
                        ElementTransformUtils.RotateElement(doc, copiedId, axis, angle);
                        var elem = doc.GetElement(copiedId);
                        createdElements.Add(new
                        {
                            id = GetIdLong(copiedId),
                            name = elem?.Name ?? "",
                            category = elem?.Category?.Name ?? ""
                        });
                    }
                }
            }
            else // linear
            {
                var spacingX = input["spacingX"]?.Value<double>() ?? 0;
                var spacingY = input["spacingY"]?.Value<double>() ?? 0;
                var spacingZ = input["spacingZ"]?.Value<double>() ?? 0;
                var offset = new XYZ(spacingX / MmPerFoot, spacingY / MmPerFoot, spacingZ / MmPerFoot);

                for (int i = 1; i <= count; i++)
                {
                    var translation = offset * i;
                    var copied = ElementTransformUtils.CopyElements(doc, sourceIds, translation);
                    foreach (var copiedId in copied)
                    {
                        var elem = doc.GetElement(copiedId);
                        createdElements.Add(new
                        {
                            id = GetIdLong(copiedId),
                            name = elem?.Name ?? "",
                            category = elem?.Category?.Name ?? ""
                        });
                    }
                }
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                arrayType,
                copyCount = count,
                createdElementCount = createdElements.Count,
                createdElements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create array: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

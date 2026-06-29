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
/// Creates a linear or radial array of elements by copying.
/// </summary>
[ToolSafety(false, false)]
public class CreateArrayTool : ICortexTool
{
    public string Name => "create_array";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a linear or radial array of elements. By default builds a real associative Revit ArrayElement (a group with an editable count); set associative=false for loose independent copies.";
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
        // Real Revit ArrayElement (default) vs. loose independent copies.
        var associative = input["associative"]?.Value<bool>() ?? true;

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
                        $"Element {ToolHelpers.GetElementIdValue(eid)} not found");
            }

            var createdElements = new List<object>();

            // ── Associative Revit ArrayElement (default) ──────────────────────
            if (associative)
            {
                var view = doc.ActiveView;
                if (view == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "Associative arrays need an active view. Activate a view, or pass associative=false for loose copies.");

                using var atx = new Transaction(doc, "RevitCortex: Create Array (associative)");
                var atxFailures = TransactionFailureHandling.SuppressWarnings(atx);
                atx.Start();
                ElementId arrayId;
                if (arrayType == "radial")
                {
                    var centerX = input["centerX"]?.Value<double>() ?? 0;
                    var centerY = input["centerY"]?.Value<double>() ?? 0;
                    var totalAngle = (input["totalAngle"]?.Value<double>() ?? 360) * Math.PI / 180.0;
                    var center = new XYZ(centerX / MmPerFoot, centerY / MmPerFoot, 0);
                    var axis = Line.CreateBound(center, center + XYZ.BasisZ * 10);
                    var ra = RadialArray.Create(doc, view, sourceIds, count, axis, totalAngle, ArrayAnchorMember.Last);
                    arrayId = ra.Id;
                }
                else
                {
                    var spacingX = input["spacingX"]?.Value<double>() ?? 0;
                    var spacingY = input["spacingY"]?.Value<double>() ?? 0;
                    var spacingZ = input["spacingZ"]?.Value<double>() ?? 0;
                    // LinearArray translation is the vector from the first to the LAST member.
                    var totalVec = new XYZ(spacingX / MmPerFoot, spacingY / MmPerFoot, spacingZ / MmPerFoot) * (count - 1);
                    var la = LinearArray.Create(doc, view, sourceIds, count, totalVec, ArrayAnchorMember.Last);
                    arrayId = la.Id;
                }
                if (atx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(atxFailures)}",
                        suggestion: "Fix the reported model errors and retry.");

                return CortexResult<object>.Ok(new
                {
                    arrayType,
                    associative = true,
                    count,
                    arrayElementId = ToolHelpers.GetElementIdValue(arrayId),
                    message = $"Created associative {arrayType} array of {count} (ArrayElement {ToolHelpers.GetElementIdValue(arrayId)})"
                });
            }

            using var tx = new Transaction(doc, "RevitCortex: Create Array");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (arrayType == "radial")
            {
                var centerX = input["centerX"]?.Value<double>() ?? 0;
                var centerY = input["centerY"]?.Value<double>() ?? 0;
                var totalAngle = input["totalAngle"]?.Value<double>() ?? 360;

                var center = new XYZ(centerX / MmPerFoot, centerY / MmPerFoot, 0);
                var axis = Line.CreateBound(center, center + XYZ.BasisZ * 10);

                // H13: match the associative RadialArray semantics — `count` is the TOTAL
                // number of members (the un-copied source is member 1). The old divisor
                // (count+1) under-filled every partial sweep. Step depends on whether the
                // sweep is a closed full circle:
                //   - closed (~360°): members evenly spaced by totalAngle/count, so the
                //     count-th position would coincide with the source at 0° — skip it.
                //   - open (e.g. 90°): the last copy must land exactly at totalAngle, so
                //     step by totalAngle/(count-1).
                bool closedLoop = Math.Abs((totalAngle % 360.0 + 360.0) % 360.0) < 1e-6;
                double stepDeg = count > 1
                    ? (closedLoop ? totalAngle / count : totalAngle / (count - 1))
                    : totalAngle;
                for (int i = 1; i <= count - 1; i++)
                {
                    var angle = stepDeg * i * Math.PI / 180.0;
                    var copied = ElementTransformUtils.CopyElements(doc, sourceIds, XYZ.Zero);
                    foreach (var copiedId in copied)
                    {
                        ElementTransformUtils.RotateElement(doc, copiedId, axis, angle);
                        var elem = doc.GetElement(copiedId);
                        createdElements.Add(new
                        {
                            id = ToolHelpers.GetElementIdValue(copiedId),
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
                            id = ToolHelpers.GetElementIdValue(copiedId),
                            name = elem?.Name ?? "",
                            category = elem?.Category?.Name ?? ""
                        });
                    }
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

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
}

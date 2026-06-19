using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Highlights an element inside a linked model by selecting the link instance,
/// creating a section box around the element, and zooming to it.
/// </summary>
public class HighlightLinkedElementTool : ICortexTool
{
    public string Name => "highlight_linked_element";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Highlights an element inside a linked model: selects the link instance, creates a section box around the target element, and zooms to it.";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long>() ?? 0;
        var linkedElementId = input["linkedElementId"]?.Value<long>() ?? 0;
        var createSectionBox = input["createSectionBox"]?.Value<bool>() ?? true;
        var offsetMm = input["offset"]?.Value<double>() ?? 1000;

        if (instanceId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "instanceId is required");
        if (linkedElementId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "linkedElementId is required");

        try
        {
#if REVIT2024_OR_GREATER
            var element = doc.GetElement(new ElementId(instanceId));
#else
            var element = doc.GetElement(new ElementId((int)instanceId));
#endif
            var linkInstance = element as RevitLinkInstance;
            if (linkInstance == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Element {instanceId} is not a RevitLinkInstance");

            var linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Linked document is not loaded");

            // Find the element in the linked document
#if REVIT2024_OR_GREATER
            var linkedElement = linkDoc.GetElement(new ElementId(linkedElementId));
#else
            var linkedElement = linkDoc.GetElement(new ElementId((int)linkedElementId));
#endif
            if (linkedElement == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Element {linkedElementId} not found in linked document");

            var bb = linkedElement.get_BoundingBox(null);
            if (bb == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Element has no bounding box geometry");

            // Transform the bounding box from link space to host space
            var linkTransform = linkInstance.GetTotalTransform();
            var hostMin = linkTransform.OfPoint(bb.Min);
            var hostMax = linkTransform.OfPoint(bb.Max);

            // Correct min/max after transform (rotation can swap min/max)
            var correctedMin = new XYZ(
                Math.Min(hostMin.X, hostMax.X),
                Math.Min(hostMin.Y, hostMax.Y),
                Math.Min(hostMin.Z, hostMax.Z));
            var correctedMax = new XYZ(
                Math.Max(hostMin.X, hostMax.X),
                Math.Max(hostMin.Y, hostMax.Y),
                Math.Max(hostMin.Z, hostMax.Z));

            var offset = offsetMm / MmPerFoot;

            // Select the link instance in the UI
            var uiDoc = new UIDocument(doc);
            uiDoc.Selection.SetElementIds(new List<ElementId> { linkInstance.Id });

            string? sectionBoxViewName = null;

            if (createSectionBox)
            {
                // Find or use a 3D view
                View3D? targetView = doc.ActiveView as View3D;
                if (targetView == null || targetView.IsTemplate || targetView.IsLocked)
                {
                    targetView = new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked);
                }

                if (targetView != null)
                {
                    using var tx = new Transaction(doc, "RevitCortex: Highlight Linked Element");
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();

                    targetView.IsSectionBoxActive = true;
                    targetView.SetSectionBox(new BoundingBoxXYZ
                    {
                        Min = new XYZ(correctedMin.X - offset, correctedMin.Y - offset, correctedMin.Z - offset),
                        Max = new XYZ(correctedMax.X + offset, correctedMax.Y + offset, correctedMax.Z + offset)
                    });

                    if (tx.Commit() != TransactionStatus.Committed)
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                            $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                            suggestion: "Fix the reported model errors and retry.");

                    uiDoc.ActiveView = targetView;
                    sectionBoxViewName = targetView.Name;
                }
            }

            uiDoc.ShowElements(linkInstance.Id);

            return CortexResult<object>.Ok(new
            {
                instanceId,
                linkedElementId,
                linkedElementName = linkedElement.Name,
                linkedElementCategory = linkedElement.Category?.Name ?? "",
                hostBoundingBox = new
                {
                    min = new { x = Math.Round(correctedMin.X * MmPerFoot, 1), y = Math.Round(correctedMin.Y * MmPerFoot, 1), z = Math.Round(correctedMin.Z * MmPerFoot, 1) },
                    max = new { x = Math.Round(correctedMax.X * MmPerFoot, 1), y = Math.Round(correctedMax.Y * MmPerFoot, 1), z = Math.Round(correctedMax.Z * MmPerFoot, 1) }
                },
                sectionBoxViewName,
                message = sectionBoxViewName != null
                    ? $"Highlighted '{linkedElement.Name}' with section box in '{sectionBoxViewName}'"
                    : $"Selected link instance containing '{linkedElement.Name}'"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Aligns a link instance to the host project's internal origin or shared coordinates.
/// </summary>
public class AlignLinkToHostTool : ICortexTool
{
    public string Name => "align_link_to_host";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Aligns a link instance to the host project's internal origin (resets transform to identity) or to shared coordinates.";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long>() ?? 0;
        var alignMode = input["alignMode"]?.Value<string>() ?? "origin";

        if (instanceId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "instanceId is required");

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

            if (linkInstance.Pinned)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "Link instance is pinned. Unpin it first using pin_unpin_link_instance.");

            if (!session.RequestConfirmation("align link instance", 1, $"Align '{linkInstance.Name}' to {alignMode}"))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            var currentTransform = linkInstance.GetTotalTransform();
            var oldOriginMm = new
            {
                x = Math.Round(currentTransform.Origin.X * MmPerFoot, 1),
                y = Math.Round(currentTransform.Origin.Y * MmPerFoot, 1),
                z = Math.Round(currentTransform.Origin.Z * MmPerFoot, 1)
            };

            using var tx = new Transaction(doc, "RevitCortex: Align Link To Host");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (alignMode.Equals("shared", StringComparison.OrdinalIgnoreCase))
            {
                // H42: shared-coordinate alignment means the link's shared origin must sit on
                // top of the host's shared origin. The old code moved the link to the host's
                // survey-point offset expressed in internal feet, which is not a shared
                // alignment at all. The correct delta is computed from BOTH models' project
                // positions (survey-point displacements), which requires the link document.
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "Shared-coordinate alignment requires the linked model to be loaded.",
                        suggestion: "Reload the link, or use alignMode='origin' to reset to the internal origin.");
                }

                // Host and link shared-coordinate displacement of their respective internal
                // origins (the survey point offset of each model, in internal feet).
                var hostPos = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
                var linkPos = linkDoc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);

                var hostShared = new XYZ(hostPos.EastWest, hostPos.NorthSouth, hostPos.Elevation);
                var linkShared = new XYZ(linkPos.EastWest, linkPos.NorthSouth, linkPos.Elevation);

                // To make the link's shared origin coincide with the host's, the link
                // instance must be offset by the difference of the two survey displacements,
                // on top of clearing its current placement.
                var sharedDelta = hostShared - linkShared;
                var delta = sharedDelta - currentTransform.Origin;
                if (delta.GetLength() > 0.001)
                    ElementTransformUtils.MoveElement(doc, linkInstance.Id, delta);
            }
            else
            {
                // Default: align to internal origin (0,0,0)
                var delta = XYZ.Zero - currentTransform.Origin;
                if (delta.GetLength() > 0.001)
                    ElementTransformUtils.MoveElement(doc, linkInstance.Id, delta);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            var newTransform = linkInstance.GetTotalTransform();
            return CortexResult<object>.Ok(new
            {
                instanceId,
                name = linkInstance.Name,
                alignMode,
                oldOrigin = oldOriginMm,
                newOrigin = new
                {
                    x = Math.Round(newTransform.Origin.X * MmPerFoot, 1),
                    y = Math.Round(newTransform.Origin.Y * MmPerFoot, 1),
                    z = Math.Round(newTransform.Origin.Z * MmPerFoot, 1)
                }
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

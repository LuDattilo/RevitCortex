using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

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
            tx.Start();

            if (alignMode.Equals("shared", StringComparison.OrdinalIgnoreCase))
            {
                // Move to shared coordinates: use project location survey point offset
                var hostLocation = doc.ActiveProjectLocation;
                var hostPosition = hostLocation.GetProjectPosition(XYZ.Zero);

                // The shared coordinate offset from internal origin
                var sharedOffset = new XYZ(
                    hostPosition.EastWest,
                    hostPosition.NorthSouth,
                    hostPosition.Elevation);

                // Move from current position to the shared coordinate origin
                var delta = sharedOffset - currentTransform.Origin;
                ElementTransformUtils.MoveElement(doc, linkInstance.Id, delta);
            }
            else
            {
                // Default: align to internal origin (0,0,0)
                var delta = XYZ.Zero - currentTransform.Origin;
                if (delta.GetLength() > 0.001)
                    ElementTransformUtils.MoveElement(doc, linkInstance.Id, delta);
            }

            tx.Commit();

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

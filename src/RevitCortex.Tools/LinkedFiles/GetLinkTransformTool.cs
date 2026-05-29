using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Returns the full transform of a linked file instance (origin, basis vectors, rotation angle).
/// </summary>
public class GetLinkTransformTool : ICortexTool
{
    public string Name => "get_link_transform";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Returns the full transform of a linked file instance: origin (mm), basis vectors, and rotation angle (degrees).";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long?>()
            ?? input["linkInstanceId"]?.Value<long?>()
            ?? 0;
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

            var transform = linkInstance.GetTotalTransform();

            // Calculate rotation angle around Z axis from BasisX
            var rotationRad = Math.Atan2(transform.BasisX.Y, transform.BasisX.X);
            var rotationDeg = rotationRad * 180.0 / Math.PI;

            // Check if the link has shared coordinates
            var linkDoc = linkInstance.GetLinkDocument();
            bool hasSharedCoordinates = false;
            if (linkDoc != null)
            {
                try
                {
                    var hostLocation = doc.ActiveProjectLocation;
                    var hostPosition = hostLocation.GetProjectPosition(XYZ.Zero);
                    var linkLocation = linkDoc.ActiveProjectLocation;
                    var linkPosition = linkLocation.GetProjectPosition(XYZ.Zero);
                    hasSharedCoordinates = Math.Abs(hostPosition.EastWest - linkPosition.EastWest) > 0.001 ||
                                           Math.Abs(hostPosition.NorthSouth - linkPosition.NorthSouth) > 0.001;
                }
                catch { /* shared coords not available */ }
            }

            return CortexResult<object>.Ok(new
            {
                instanceId,
                name = linkInstance.Name,
                isPinned = linkInstance.Pinned,
                isLoaded = linkDoc != null,
                origin = new
                {
                    x = Math.Round(transform.Origin.X * MmPerFoot, 1),
                    y = Math.Round(transform.Origin.Y * MmPerFoot, 1),
                    z = Math.Round(transform.Origin.Z * MmPerFoot, 1)
                },
                basisX = new { x = Math.Round(transform.BasisX.X, 6), y = Math.Round(transform.BasisX.Y, 6), z = Math.Round(transform.BasisX.Z, 6) },
                basisY = new { x = Math.Round(transform.BasisY.X, 6), y = Math.Round(transform.BasisY.Y, 6), z = Math.Round(transform.BasisY.Z, 6) },
                basisZ = new { x = Math.Round(transform.BasisZ.X, 6), y = Math.Round(transform.BasisZ.Y, 6), z = Math.Round(transform.BasisZ.Z, 6) },
                rotationDegrees = Math.Round(rotationDeg, 3),
                hasSharedCoordinates
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

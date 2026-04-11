using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Moves a linked file instance by a delta offset or to an absolute position (in mm).
/// </summary>
public class MoveLinkInstanceTool : ICortexTool
{
    public string Name => "move_link_instance";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Moves a linked file instance by a delta offset (mm) or to an absolute position (mm). Specify mode: 'delta' or 'absolute'.";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long>() ?? 0;
        var x = input["x"]?.Value<double>() ?? 0;
        var y = input["y"]?.Value<double>() ?? 0;
        var z = input["z"]?.Value<double>() ?? 0;
        var mode = input["mode"]?.Value<string>() ?? "delta";

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

            if (!session.RequestConfirmation("move link instance", 1, $"Move '{linkInstance.Name}'"))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            var currentTransform = linkInstance.GetTotalTransform();
            XYZ translation;

            if (mode.Equals("absolute", StringComparison.OrdinalIgnoreCase))
            {
                // Move to absolute position: calculate delta from current origin
                var targetFt = new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
                translation = targetFt - currentTransform.Origin;
            }
            else
            {
                // Delta mode: move by offset
                translation = new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
            }

            using var tx = new Transaction(doc, "RevitCortex: Move Link Instance");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, linkInstance.Id, translation);
            tx.Commit();

            // Read new position
            var newTransform = linkInstance.GetTotalTransform();
            return CortexResult<object>.Ok(new
            {
                instanceId,
                name = linkInstance.Name,
                mode,
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

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class ModifyElementTool : ICortexTool
{
    public string Name => "modify_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Move, rotate, mirror, or copy elements. Rotate is about the Z axis by default, or any axis via rotationAxis {x,y,z}.";
    // 1 foot = 304.8 mm
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required");

        var action = input["action"]?.Value<string>()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action is required",
                suggestion: "Supported actions: move, rotate, mirror, copy");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // Resolve valid element ids
        var revitIds = elementIds
            .Select(id =>
            {
#if REVIT2024_OR_GREATER
                return new ElementId(id);
#else
                return new ElementId((int)id);
#endif
            })
            .Where(id => doc.GetElement(id) != null)
            .ToList();

        if (revitIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No valid elements found for the provided elementIds");

        try
        {
            ICollection<ElementId>? newElementIds = null;

            using var tx = new Transaction(doc, $"RevitCortex: Modify Elements - {action}");
            tx.Start();
            try
            {
                switch (action)
                {
                    case "move":
                        ExecuteMove(doc, revitIds, input);
                        break;
                    case "rotate":
                        ExecuteRotate(doc, revitIds, input);
                        break;
                    case "mirror":
                        ExecuteMirror(doc, revitIds, input);
                        break;
                    case "copy":
                        newElementIds = ExecuteCopy(doc, revitIds, input);
                        break;
                    default:
                        tx.RollBack();
                        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                            $"Unknown action '{action}'",
                            suggestion: "Supported actions: move, rotate, mirror, copy");
                }

                tx.Commit();
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            var resultData = BuildResult(action, revitIds.Count, newElementIds);
            return CortexResult<object>.Ok(resultData);
        }
        catch (ArgumentException ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, ex.Message);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Modify element failed: {ex.Message}");
        }
    }

    // ── Action implementations ─────────────────────────────────────────────

    private static void ExecuteMove(Document doc, List<ElementId> elementIds, JObject input)
    {
        var translationToken = input["translation"];
        if (translationToken == null)
            throw new ArgumentException("translation is required for move action. Provide {x, y, z} in mm.");

        var translation = ParseXYZ(translationToken, "translation");
        ElementTransformUtils.MoveElements(doc, elementIds, translation);
    }

    private static void ExecuteRotate(Document doc, List<ElementId> elementIds, JObject input)
    {
        var centerToken = input["rotationCenter"];
        if (centerToken == null)
            throw new ArgumentException("rotationCenter is required for rotate action. Provide {x, y, z} in mm.");

        var angleToken = input["rotationAngle"];
        if (angleToken == null)
            throw new ArgumentException("rotationAngle is required for rotate action (degrees).");

        var center = ParseXYZ(centerToken, "rotationCenter");
        var angleDegrees = angleToken.Value<double>();
        var angleRadians = angleDegrees * Math.PI / 180.0;

        // Rotation axis: default Z at the center, or an arbitrary direction vector
        // via rotationAxis {x,y,z} (e.g. {x:1,y:0,z:0} to tilt about the X axis).
        var axisToken = input["rotationAxis"];
        XYZ axisDir = XYZ.BasisZ;
        if (axisToken != null && axisToken.Type != JTokenType.Null)
        {
            var ax = axisToken["x"]?.Value<double>() ?? 0;
            var ay = axisToken["y"]?.Value<double>() ?? 0;
            var az = axisToken["z"]?.Value<double>() ?? 1;
            var dir = new XYZ(ax, ay, az);
            if (dir.GetLength() > 1e-9) axisDir = dir.Normalize();
        }

        var axis = Line.CreateBound(center, center + axisDir);
        ElementTransformUtils.RotateElements(doc, elementIds, axis, angleRadians);
    }

    private static void ExecuteMirror(Document doc, List<ElementId> elementIds, JObject input)
    {
        var originToken = input["mirrorPlaneOrigin"];
        if (originToken == null)
            throw new ArgumentException("mirrorPlaneOrigin is required for mirror action. Provide {x, y, z} in mm.");

        var normalToken = input["mirrorPlaneNormal"];
        if (normalToken == null)
            throw new ArgumentException("mirrorPlaneNormal is required for mirror action. Provide {x, y, z} (unit vector, e.g. {x:1,y:0,z:0} for YZ plane).");

        var origin = ParseXYZ(originToken, "mirrorPlaneOrigin");

        // Normal is a direction vector — convert mm→feet only for position vectors;
        // for direction vectors, just read and normalize without unit conversion
        var nx = normalToken["x"]?.Value<double>() ?? 0;
        var ny = normalToken["y"]?.Value<double>() ?? 0;
        var nz = normalToken["z"]?.Value<double>() ?? 0;
        var normal = new XYZ(nx, ny, nz).Normalize();

        var mirrorPlane = Plane.CreateByNormalAndOrigin(normal, origin);
        ElementTransformUtils.MirrorElements(doc, elementIds, mirrorPlane, false);
    }

    private static ICollection<ElementId> ExecuteCopy(Document doc, List<ElementId> elementIds, JObject input)
    {
        var offsetToken = input["copyOffset"];
        if (offsetToken == null)
            throw new ArgumentException("copyOffset is required for copy action. Provide {x, y, z} in mm.");

        var offset = ParseXYZ(offsetToken, "copyOffset");
        return ElementTransformUtils.CopyElements(doc, elementIds, offset);
    }

    // ── Result builder ─────────────────────────────────────────────────────

    private static object BuildResult(string action, int elementCount, ICollection<ElementId>? newElementIds)
    {
        if (action == "copy" && newElementIds != null)
        {
            var newIds = newElementIds.Select(id =>
            {
#if REVIT2024_OR_GREATER
                return id.Value;
#else
                return (long)id.IntegerValue;
#endif
            }).ToArray();

            return new
            {
                message = $"Successfully copied {elementCount} element(s), created {newIds.Length} new element(s)",
                action,
                elementCount,
                newElementIds = newIds
            };
        }

        return new
        {
            message = $"Successfully executed '{action}' on {elementCount} element(s)",
            action,
            elementCount
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JSON {x,y,z} token (coordinates in mm) into Revit's internal foot-based XYZ.
    /// </summary>
    private static XYZ ParseXYZ(JToken token, string paramName)
    {
        if (token == null || token.Type == JTokenType.Null)
            throw new ArgumentException($"'{paramName}' must be a {{x, y, z}} object in mm.");

        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }
}

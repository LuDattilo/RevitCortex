using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Adds a new Revit link to the current document from a file path.
/// </summary>
public class AddLinkedFileTool : ICortexTool
{
    public string Name => "add_linked_file";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Adds a new Revit linked file from a file path and optionally places an instance at a specified position.";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var filePath = input["filePath"]?.Value<string>();
        var positionX = input["positionX"]?.Value<double>() ?? 0;
        var positionY = input["positionY"]?.Value<double>() ?? 0;
        var positionZ = input["positionZ"]?.Value<double>() ?? 0;

        if (string.IsNullOrWhiteSpace(filePath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "filePath is required");

        if (!session.RequestConfirmation("add linked file", 1, $"Link file: {filePath}"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
            var options = new RevitLinkOptions(false); // false = not relative path

            // RevitLinkType.Create and RevitLinkInstance.Create are not transactable
            var linkLoadResult = RevitLinkType.Create(doc, modelPath, options);
            var linkTypeId = linkLoadResult.ElementId;

            var instance = RevitLinkInstance.Create(doc, linkTypeId);

            // MoveElement requires a Transaction
            if (Math.Abs(positionX) > 0.001 || Math.Abs(positionY) > 0.001 || Math.Abs(positionZ) > 0.001)
            {
                using var tx = new Transaction(doc, "RevitCortex: Position Linked File");
                tx.Start();
                var offset = new XYZ(positionX / MmPerFoot, positionY / MmPerFoot, positionZ / MmPerFoot);
                ElementTransformUtils.MoveElement(doc, instance.Id, offset);
                tx.Commit();
            }

            return CortexResult<object>.Ok(new
            {
                linkTypeId = GetIdLong(linkTypeId),
                instanceId = GetIdLong(instance.Id),
                name = instance.Name,
                filePath,
                position = new { x = positionX, y = positionY, z = positionZ }
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to add linked file: {ex.Message}");
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

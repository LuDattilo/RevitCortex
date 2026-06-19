using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Places a view on a sheet at the specified position.
/// </summary>
public class PlaceViewportTool : ICortexTool
{
    public string Name => "place_viewport";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Places a view on a sheet at the specified position, with optional rotation and viewport type.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var posXMm = input["positionX"]?.Value<double>() ?? 0;
        var posYMm = input["positionY"]?.Value<double>() ?? 0;
        var rotation = input["rotation"]?.Value<string>();
        var viewportTypeId = input["viewportTypeId"]?.Value<long?>() ?? 0;

        if (sheetId <= 0 || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId and viewId are required");

        try
        {
#if REVIT2024_OR_GREATER
            var sheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
            var view = doc.GetElement(new ElementId(viewId)) as View;
            var viewEid = new ElementId(viewId);
            var sheetEid = new ElementId(sheetId);
#else
            var sheet = doc.GetElement(new ElementId((int)sheetId)) as ViewSheet;
            var view = doc.GetElement(new ElementId((int)viewId)) as View;
            var viewEid = new ElementId((int)viewId);
            var sheetEid = new ElementId((int)sheetId);
#endif
            if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Sheet not found");
            if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View not found");

            if (!Viewport.CanAddViewToSheet(doc, sheetEid, viewEid))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "View cannot be added to this sheet (already placed or not placeable)");

            var position = new XYZ(posXMm / MmPerFoot, posYMm / MmPerFoot, 0);

            using var tx = new Transaction(doc, "RevitCortex: Place Viewport");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var viewport = Viewport.Create(doc, sheetEid, viewEid, position);

            // Optional rotation
            if (!string.IsNullOrEmpty(rotation))
            {
                viewport.Rotation = rotation!.ToLowerInvariant() switch
                {
                    "clockwise"        => ViewportRotation.Clockwise,
                    "counterclockwise" => ViewportRotation.Counterclockwise,
                    _                  => ViewportRotation.None,
                };
            }

            // Optional viewport type (controls title/detail-number appearance)
            if (viewportTypeId > 0)
            {
                var vpType = doc.GetElement(ToolHelpers.ToElementId(viewportTypeId));
                if (vpType is ElementType && viewport.IsValidType(vpType.Id))
                    viewport.ChangeTypeId(vpType.Id);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                viewportId = ToolHelpers.GetElementIdValue(viewport.Id),
                sheetNumber = sheet.SheetNumber,
                viewName = view.Name,
                rotation = viewport.Rotation.ToString()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

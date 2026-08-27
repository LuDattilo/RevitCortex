using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Views;

/// <summary>
/// Lists, renames, moves, and assigns scope boxes (OST_VolumeOfInterest) to views.
/// The Revit API exposes no public method to CREATE a scope box from scratch — every
/// Autodesk-confirmed source on this agrees a scope box must first be drawn by hand in
/// Revit (Annotate > Scope Box); the API can only read, rename, translate, and assign an
/// EXISTING one. action=create returns a structured "unsupported" result rather than a
/// generic failure, same as manage_area_plans(action=create) for the same kind of API gap.
/// </summary>
[ToolSafety(false, false)]
public class ManageScopeBoxesTool : ICortexTool
{
    public string Name => "manage_scope_boxes";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Inventory, rename, move, or assign-to-views existing scope boxes (OST_VolumeOfInterest). " +
        "action=list|rename|move|assign_to_views|create. There is no public Revit API to CREATE a scope box " +
        "from scratch (confirmed unsupported): draw one by hand once, then use this tool to manage it. " +
        "rename needs elementId+newName. move needs elementId+translation{x,y,z} (mm). " +
        "assign_to_views needs scopeBoxId (0 clears it) and viewIds (array).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();
        try
        {
            switch (action)
            {
                case "list": return ListScopeBoxes(doc);
                case "create": return UnsupportedCreateResult();
                case "rename": return RenameScopeBox(doc, input);
                case "move": return MoveScopeBox(doc, input);
                case "assign_to_views": return AssignToViews(doc, input);
                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unsupported action: {action}",
                        suggestion: "Use: list | rename | move | assign_to_views | create");
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListScopeBoxes(Document doc)
    {
        var boxes = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
            .WhereElementIsNotElementType()
            .Select(e =>
            {
                var bb = e.get_BoundingBox(null);
                return new
                {
                    id = ToolHelpers.GetElementIdValue(e.Id),
                    name = e.Name,
                    minMm = bb != null ? ToMm(bb.Min) : null,
                    maxMm = bb != null ? ToMm(bb.Max) : null
                };
            })
            .ToList();

        return CortexResult<object>.Ok(new { count = boxes.Count, scopeBoxes = boxes });
    }

    private static CortexResult<object> UnsupportedCreateResult()
    {
        return CortexResult<object>.Ok(new
        {
            supported = false,
            message = "Scope box creation is unsupported: the Revit API exposes no method to build a " +
                       "OST_VolumeOfInterest element from scratch. Draw one by hand once (Annotate > Scope Box), " +
                       "then use action=rename/move/assign_to_views to manage it."
        });
    }

    private static CortexResult<object> RenameScopeBox(Document doc, JObject input)
    {
        var elementIdLong = input["elementId"]?.Value<long?>() ?? 0;
        var newName = input["newName"]?.Value<string>();
        if (elementIdLong <= 0 || string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementId and newName are required");

        var elem = doc.GetElement(ToolHelpers.ToElementId(elementIdLong));
        if (elem == null || elem.Category?.Id != new ElementId(BuiltInCategory.OST_VolumeOfInterest))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{elementIdLong} is not a scope box");

        using var tx = new Transaction(doc, "RiveTT: Rename Scope Box");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        elem.Name = newName!;
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { id = ToolHelpers.GetElementIdValue(elem.Id), name = elem.Name });
    }

    private static CortexResult<object> MoveScopeBox(Document doc, JObject input)
    {
        var elementIdLong = input["elementId"]?.Value<long?>() ?? 0;
        var translationToken = input["translation"];
        if (elementIdLong <= 0 || translationToken == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementId and translation ({x,y,z} mm) are required");

        var id = ToolHelpers.ToElementId(elementIdLong);
        var elem = doc.GetElement(id);
        if (elem == null || elem.Category?.Id != new ElementId(BuiltInCategory.OST_VolumeOfInterest))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{elementIdLong} is not a scope box");

        var translation = new XYZ(
            (translationToken["x"]?.Value<double>() ?? 0) / MmPerFoot,
            (translationToken["y"]?.Value<double>() ?? 0) / MmPerFoot,
            (translationToken["z"]?.Value<double>() ?? 0) / MmPerFoot);

        using var tx = new Transaction(doc, "RiveTT: Move Scope Box");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        ElementTransformUtils.MoveElement(doc, id, translation);
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        var bb = elem.get_BoundingBox(null);
        return CortexResult<object>.Ok(new
        {
            id = ToolHelpers.GetElementIdValue(elem.Id),
            minMm = bb != null ? ToMm(bb.Min) : null,
            maxMm = bb != null ? ToMm(bb.Max) : null
        });
    }

    private static CortexResult<object> AssignToViews(Document doc, JObject input)
    {
        var scopeBoxIdLong = input["scopeBoxId"]?.Value<long?>();
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (scopeBoxIdLong == null || viewIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "scopeBoxId and viewIds (array) are required");

        var targetId = scopeBoxIdLong.Value > 0 ? ToolHelpers.ToElementId(scopeBoxIdLong.Value) : ElementId.InvalidElementId;

        using var tx = new Transaction(doc, "RiveTT: Assign Scope Box");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var applied = new List<long>();
        var warnings = new List<string>();
        foreach (var vid in viewIds)
        {
            var view = doc.GetElement(ToolHelpers.ToElementId(vid)) as View;
            if (view == null) { warnings.Add($"View {vid} not found"); continue; }

            var param = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)
                        ?? view.LookupParameter("Scope Box");
            if (param == null || param.IsReadOnly)
            {
                warnings.Add($"View {vid} has no writable Scope Box parameter (view type may not support one)");
                continue;
            }

            param.Set(targetId);
            applied.Add(vid);
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new { appliedCount = applied.Count, appliedViewIds = applied, warnings });
    }

    private static object ToMm(XYZ p) => new { x = p.X * MmPerFoot, y = p.Y * MmPerFoot, z = p.Z * MmPerFoot };
}

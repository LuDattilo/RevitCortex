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

namespace RiveTT.Tools.Elements;

[ToolSafety(false, true)]
public sealed class DetachWallConstraintTool : ICortexTool
{
    public string Name => "detach_wall_constraint";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Preview or detach wall top-level constraints and Revit 2027 top/base attachments while preserving unconnected height.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var wallIds = input["wallIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (wallIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "wallIds is required");
        var mode = (input["mode"]?.Value<string>() ?? "level_top").ToLowerInvariant();
        if (mode is not ("level_top" or "attachment_top" or "attachment_base" or "all_attachments"))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "mode must be level_top, attachment_top, attachment_base, or all_attachments");
        var dryRun = ToolHelpers.GetDryRun(input);
        var details = new List<object>();
        var grouped = new List<long>();
        var errors = new List<object>();
        var modified = 0;

        var walls = wallIds.Distinct()
            .Select(id => doc.GetElement(ToolHelpers.ToElementId(id)) as Wall)
            .Where(wall => wall != null).Cast<Wall>().ToList();

        Transaction? tx = null;
        TransactionFailureHandling.FailureCapture? failures = null;
        try
        {
            if (!dryRun)
            {
                tx = new Transaction(doc, "RiveTT: Detach Wall Constraints");
                failures = TransactionFailureHandling.FromInput(tx, input);
                tx.Start();
            }

            foreach (var wall in walls)
            {
                var wallId = ToolHelpers.GetElementIdValue(wall.Id);
                if (wall.GroupId != ElementId.InvalidElementId)
                {
                    grouped.Add(wallId);
                    continue;
                }

                var topAttachments = wall.GetAttachmentIds(AttachmentLocation.Top).ToList();
                var baseAttachments = wall.GetAttachmentIds(AttachmentLocation.Base).ToList();
                var attachmentIds = topAttachments.Concat(baseAttachments).Distinct().ToList();
                var topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                var currentHeight = heightParam?.AsDouble() ??
                    ((wall.get_BoundingBox(null)?.Max.Z ?? 0) - (wall.get_BoundingBox(null)?.Min.Z ?? 0));

                if (!dryRun)
                {
                    try
                    {
                        if (mode == "level_top")
                        {
                            if (topConstraint == null || topConstraint.IsReadOnly)
                                throw new InvalidOperationException("Top constraint is not writable");
                            topConstraint.Set(ElementId.InvalidElementId);
                            if (heightParam != null && !heightParam.IsReadOnly)
                                heightParam.Set(currentHeight);
                        }
                        else
                        {
                            var targets = mode == "attachment_top" ? topAttachments
                                : mode == "attachment_base" ? baseAttachments : attachmentIds;
                            foreach (var targetId in targets)
                            {
                                if (mode == "all_attachments") wall.RemoveAttachment(targetId);
                                else wall.RemoveAttachment(targetId,
                                    mode == "attachment_top" ? AttachmentLocation.Top : AttachmentLocation.Base);
                            }
                        }
                        modified++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new { wallId, reason = ex.Message });
                        continue;
                    }
                }
                else modified++;

                details.Add(new
                {
                    wallId,
                    mode,
                    currentTopLevelId = ToolHelpers.GetElementIdValue(topConstraint?.AsElementId()),
                    preservedHeightMm = currentHeight * MmPerFoot,
                    attachmentTargetIds = attachmentIds.Select(ToolHelpers.GetElementIdValue).ToArray()
                });
            }

            if (tx != null)
            {
                if (tx.Commit() != TransactionStatus.Committed)
                    return TransactionFailureHandling.ToFailure(failures!,
                        "Wall constraint detachment was rolled back",
                        "Handle grouped walls separately and repair the listed attachments before retrying.");
                tx.Dispose();
                tx = null;
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                processed = wallIds.Count,
                resolvedWalls = walls.Count,
                modified,
                skipped = grouped.Count,
                errors = errors.Count,
                groupedWallIds = grouped,
                details
            });
        }
        catch (Exception ex)
        {
            if (tx?.GetStatus() == TransactionStatus.Started) tx.RollBack();
            tx?.Dispose();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to detach wall constraints: {ex.Message}");
        }
    }
}

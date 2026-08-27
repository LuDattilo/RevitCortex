using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Creates a key schedule (ViewSchedule.CreateKeySchedule) — finishes-by-room,
/// dwelling-unit typologies. create_schedule/create_preset_schedule only ever built
/// element-instance schedules, not the key-driven kind.
/// </summary>
[ToolSafety(false, false)]
public class CreateKeyScheduleTool : ICortexTool
{
    public string Name => "create_key_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a key schedule (ViewSchedule.CreateKeySchedule) — a reusable finish/typology key table, e.g. " +
        "room finish keys or dwelling-unit typologies. Different from create_schedule/create_preset_schedule, " +
        "which only build element-instance schedules. categoryName is the category the keys apply to " +
        "(e.g. Rooms). Add/edit fields and key rows afterward with modify_schedule.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryName = input["categoryName"]?.Value<string>();
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(categoryName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryName is required");

        var categoryId = CategoryResolver.ResolveToId(doc, categoryName!);
        if (categoryId == null || categoryId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Category '{categoryName}' could not be resolved in this document",
                suggestion: "Use an OST_* name, an English category name, or the exact localized label");

        using var tx = new Transaction(doc, "RiveTT: Create Key Schedule");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        ViewSchedule schedule;
        try
        {
            schedule = ViewSchedule.CreateKeySchedule(doc, categoryId);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"CreateKeySchedule failed: {ex.Message}",
                suggestion: "Not every category supports a key schedule; Rooms and most model categories do.");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            try { schedule.Name = name!; } catch { /* duplicate name */ }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CortexResult<object>.Ok(new
        {
            viewId = ToolHelpers.GetElementIdValue(schedule.Id),
            viewName = schedule.Name
        });
    }
}

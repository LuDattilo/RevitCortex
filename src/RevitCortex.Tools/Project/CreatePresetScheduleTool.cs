using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Creates preset schedules: door_by_room, window_by_room, room_finish,
/// material_takeoff, sheet_list, view_list.
/// </summary>
public class CreatePresetScheduleTool : ICortexTool
{
    public string Name => "create_preset_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates preset schedules: door_by_room, window_by_room, room_finish, material_takeoff, sheet_list, view_list.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var preset = input["preset"]?.Value<string>() ?? "";
        var name = input["name"]?.Value<string>();
        var categoryName = input["categoryName"]?.Value<string>();

        try
        {
            using var tx = new Transaction(doc, "RevitCortex: Create Preset Schedule");
            tx.Start();

            ViewSchedule schedule;
            switch (preset.ToLowerInvariant())
            {
                case "door_by_room":
                    schedule = CreateCategorySchedule(doc, BuiltInCategory.OST_Doors, name ?? "Door Schedule");
                    AddFieldsIfExist(schedule, "Room Number", "Room Name", "Family", "Type", "Width", "Height", "Count");
                    break;
                case "window_by_room":
                    schedule = CreateCategorySchedule(doc, BuiltInCategory.OST_Windows, name ?? "Window Schedule");
                    AddFieldsIfExist(schedule, "Room Number", "Room Name", "Family", "Type", "Width", "Height", "Sill Height", "Head Height", "Count");
                    break;
                case "room_finish":
                    schedule = CreateCategorySchedule(doc, BuiltInCategory.OST_Rooms, name ?? "Room Finish Schedule");
                    AddFieldsIfExist(schedule, "Number", "Name", "Level", "Area", "Floor Finish", "Wall Finish", "Ceiling Finish", "Base Finish");
                    break;
                case "material_takeoff":
                {
                    if (string.IsNullOrEmpty(categoryName))
                        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryName required for material_takeoff");
                    var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryName);
                    if (catId == ElementId.InvalidElementId)
                        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Category not found: {categoryName}");
                    schedule = ViewSchedule.CreateMaterialTakeoff(doc, catId);
                    schedule.Name = name ?? $"Material Takeoff - {categoryName}";
                    AddFieldsIfExist(schedule, "Material: Name", "Material: Area", "Material: Volume");
                    break;
                }
                case "sheet_list":
                    schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Sheets));
                    schedule.Name = name ?? "Sheet List";
                    AddFieldsIfExist(schedule, "Sheet Number", "Sheet Name", "Drawn By", "Checked By", "Current Revision");
                    break;
                case "view_list":
                    schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Views));
                    schedule.Name = name ?? "View List";
                    AddFieldsIfExist(schedule, "View Name", "View Type", "View Scale", "Sheet Number", "Sheet Name");
                    break;
                default:
                    tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unknown preset: {preset}",
                        suggestion: "Use: door_by_room, window_by_room, room_finish, material_takeoff, sheet_list, view_list");
            }

            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                scheduleId = ToolHelpers.GetElementIdValue(schedule.Id),
                scheduleName = schedule.Name,
                preset,
                fieldCount = schedule.Definition.GetFieldCount()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static ViewSchedule CreateCategorySchedule(Document doc, BuiltInCategory bic, string name)
    {
        var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(bic));
        schedule.Name = name;
        return schedule;
    }

    private static void AddFieldsIfExist(ViewSchedule schedule, params string[] fieldNames)
    {
        var schedulableFields = schedule.Definition.GetSchedulableFields();
        foreach (var fieldName in fieldNames)
        {
            var field = schedulableFields.FirstOrDefault(f =>
                f.GetName(schedule.Document).IndexOf(fieldName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (field != null)
            {
                try { schedule.Definition.AddField(field); } catch { }
            }
        }
    }
}

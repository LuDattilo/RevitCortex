using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Discovers all available schedulable fields for a given category by creating
/// a temporary schedule. Requires a transaction (creates + deletes temp element).
/// </summary>
public class ListSchedulableFieldsTool : ICortexTool
{
    public string Name => "list_schedulable_fields";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Discovers all available schedulable fields for a given category by creating a temporary schedule. Requires a transaction (creates + deletes temp element).";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var categoryName = input["categoryName"]?.Value<string>() ?? "OST_Rooms";
        var scheduleType = input["scheduleType"]?.Value<string>() ?? "regular";

        string bicName = categoryName.StartsWith("OST_") ? categoryName : "OST_" + categoryName;
        if (!Enum.TryParse<BuiltInCategory>(bicName, true, out var builtInCategory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown category: {categoryName}",
                suggestion: "Use OST_* codes like OST_Rooms, OST_Walls, OST_Doors");

        try
        {
            var categoryId = new ElementId(builtInCategory);
            ViewSchedule? tempSchedule = null;
            List<object> fields;

            // Create temp schedule inside a transaction
            using (var tx = new Transaction(doc, "CortexTempSchedule"))
            {
                tx.Start();
                tempSchedule = scheduleType.ToLower() switch
                {
                    "material_takeoff" => ViewSchedule.CreateMaterialTakeoff(doc, categoryId),
                    "key_schedule"     => ViewSchedule.CreateKeySchedule(doc, categoryId),
                    _                  => ViewSchedule.CreateSchedule(doc, categoryId)
                };

                fields = tempSchedule.Definition.GetSchedulableFields()
                    .Select(f => new
                    {
                        name      = f.GetName(doc),
                        fieldType = f.FieldType.ToString(),
#if REVIT2024_OR_GREATER
                        parameterId = f.ParameterId.Value
#else
                        parameterId = (long)f.ParameterId.IntegerValue
#endif
                    })
                    .OrderBy(f => f.name)
                    .Cast<object>()
                    .ToList();

                tx.RollBack(); // don't keep the temp schedule
            }

            return CortexResult<object>.Ok(new
            {
                category     = categoryName,
                scheduleType,
                fieldCount   = fields.Count,
                fields
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to list schedulable fields: {ex.Message}");
        }
    }
}

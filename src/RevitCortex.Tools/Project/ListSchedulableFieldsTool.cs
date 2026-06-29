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
/// Discovers all available schedulable fields for a given category by creating
/// a temporary schedule. Requires a transaction (creates + deletes temp element).
/// </summary>
[ToolSafety(true, false)]
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

        // Accept both "categoryName" (wrapper-native) and "category" (the convention used by
        // create_schedule, get_compound_structure, export_to_excel, etc.). Without this alias a
        // caller passing "category" silently fell through to the OST_Rooms default and got a
        // Room-oriented field list for whatever category they actually asked about.
        var categoryName = input["categoryName"]?.Value<string>()
            ?? input["category"]?.Value<string>()
            ?? "OST_Rooms";
        var scheduleType = input["scheduleType"]?.Value<string>() ?? "regular";

        var resolved = CategoryResolver.Resolve(categoryName);
        if (resolved == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown category: {categoryName}",
                suggestion: "Use OST_* codes like OST_Rooms, or English friendly names like Walls, Doors, Foundations");
        var builtInCategory = resolved.Value;

        try
        {
            var categoryId = new ElementId(builtInCategory);
            ViewSchedule? tempSchedule = null;
            List<object> fields;

            // Create temp schedule inside a transaction
            using (var tx = new Transaction(doc, "CortexTempSchedule"))
            {
                tx.Start();
                // Normalize like CreateScheduleTool (strip separators) so 'material_takeoff',
                // 'material-takeoff' and 'materialtakeoff' all hit the same branch; 'key' is
                // the short alias documented by the server wrapper.
                var normalizedType = scheduleType.ToLowerInvariant()
                    .Replace(" ", "").Replace("_", "").Replace("-", "");
                tempSchedule = normalizedType switch
                {
                    "materialtakeoff"     => ViewSchedule.CreateMaterialTakeoff(doc, categoryId),
                    "keyschedule" or "key" => ViewSchedule.CreateKeySchedule(doc, categoryId),
                    _                     => ViewSchedule.CreateSchedule(doc, categoryId)
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

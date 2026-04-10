using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Creates a new level at the specified elevation, optionally with floor/ceiling plan views.
/// </summary>
public class CreateLevelTool : ICortexTool
{
    public string Name => "create_level";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var elevationMm = input["elevation"]?.Value<double>() ?? 0;
        var isBuildingStory = input["isBuildingStory"]?.Value<bool>() ?? true;
        var createFloorPlan = input["createFloorPlan"]?.Value<bool>() ?? false;
        var createCeilingPlan = input["createCeilingPlan"]?.Value<bool>() ?? false;

        try
        {
            // Check for duplicate name
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Level '{name}' already exists at elevation {existing.Elevation * MmPerFoot:F0} mm");

            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RevitCortex: Create Level");
            tx.Start();

            var level = Level.Create(doc, elevationMm / MmPerFoot);
            level.Name = name;

            var storyParam = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            if (storyParam != null && !storyParam.IsReadOnly)
                storyParam.Set(isBuildingStory ? 1 : 0);

            long? floorPlanId = null;
            long? ceilingPlanId = null;

            if (createFloorPlan)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (vft != null)
                {
                    var view = ViewPlan.Create(doc, vft.Id, level.Id);
                    floorPlanId = GetIdLong(view.Id);
                }
                else warnings.Add("Floor plan ViewFamilyType not found");
            }

            if (createCeilingPlan)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);
                if (vft != null)
                {
                    var view = ViewPlan.Create(doc, vft.Id, level.Id);
                    ceilingPlanId = GetIdLong(view.Id);
                }
                else warnings.Add("Ceiling plan ViewFamilyType not found");
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                levelId = GetIdLong(level.Id),
                name = level.Name,
                elevationMm,
                isBuildingStory,
                floorPlanId,
                ceilingPlanId,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create level: {ex.Message}");
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

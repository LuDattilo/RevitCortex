using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Tags all walls in the current view at their midpoint.
/// </summary>
public class TagWallsTool : ICortexTool
{
    public string Name => "tag_walls";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tags all walls in the current view at their midpoint.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var useLeader = input["useLeader"]?.Value<bool>() ?? false;

        try
        {
            var view = doc.ActiveView;

            var walls = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();

            // Find wall tag type
            var tagType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_WallTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            if (tagType == null)
            {
                // Fallback to multi-category tags
                tagType = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();
            }

            if (tagType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "No wall tag or multi-category tag types found in project");

            int taggedCount = 0;
            var warnings = new List<string>();

            using var tx = new Transaction(doc, "RevitCortex: Tag Walls");
            tx.Start();

            if (!tagType.IsActive)
            {
                tagType.Activate();
                doc.Regenerate();
            }

            foreach (var wall in walls)
            {
                try
                {
                    var locCurve = wall.Location as LocationCurve;
                    if (locCurve == null) continue;

                    var midPoint = locCurve.Curve.Evaluate(0.5, true);
                    var reference = new Reference(wall);

                    IndependentTag.Create(doc, view.Id, reference, false, TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal, midPoint);
                    taggedCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to tag wall {GetIdLong(wall.Id)}: {ex.Message}");
                }
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                taggedCount,
                totalWalls = walls.Count,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to tag walls: {ex.Message}");
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

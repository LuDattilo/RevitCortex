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
/// Analyzes model complexity with element counts, category breakdown, and level distribution.
/// </summary>
public class AnalyzeModelStatisticsTool : ICortexTool
{
    public string Name => "analyze_model_statistics";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Analyzes model complexity with element counts, category breakdown, and level distribution.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeDetailedTypes = input["includeDetailedTypes"]?.Value<bool>() ?? false;
        var compact = input["compact"]?.Value<bool>() ?? false;

        try
        {
            var allElements = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToList();
            var allTypes = new FilteredElementCollector(doc).WhereElementIsElementType().ToList();
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).ToList();
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).ToList();
            var warnings = doc.GetWarnings();

            // Category breakdown
            var byCategory = allElements
                .Where(e => e.Category != null)
                .GroupBy(e => e.Category.Name)
                .Select(g => new { category = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            // Level distribution
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            var byLevel = levels.Select(level =>
            {
                var count = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.LevelId == level.Id)
                    .Count();
                return new { level = level.Name, elevation = level.Elevation * 304.8, count };
            }).ToList();

            var result = new
            {
                totalElements = allElements.Count,
                totalTypes = allTypes.Count,
                totalFamilies = families.Count,
                totalViews = views.Count(v => !v.IsTemplate),
                totalViewTemplates = views.Count(v => v.IsTemplate),
                totalSheets = sheets.Count,
                totalWarnings = warnings.Count,
                categoryBreakdown = compact ? byCategory.Take(20).ToList() : byCategory,
                levelDistribution = byLevel,
                detailedTypes = includeDetailedTypes
                    ? allTypes
                        .Where(t => t.Category != null)
                        .GroupBy(t => t.Category.Name)
                        .Select(g => new { category = g.Key, typeCount = g.Count() })
                        .OrderByDescending(x => x.typeCount)
                        .Take(30)
                        .ToList()
                    : null
            };

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

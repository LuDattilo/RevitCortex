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
/// Comprehensive BIM model health audit returning score (0-100), grade (A-F),
/// and detailed breakdown with actionable recommendations.
/// </summary>
[ToolSafety(true, false)]
public class CheckModelHealthTool : ICortexTool
{
    public string Name => "check_model_health";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Comprehensive BIM model health audit returning score (0-100), grade (A-F), and detailed breakdown with actionable recommendations.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        try
        {
            var warnings = doc.GetWarnings();
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();

            // In-place families
            var inPlaceFamilies = families.Where(f => f.IsInPlace).ToList();

            // Unused families (no instances)
            var usedFamilyIds = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance)
                .Cast<FamilyInstance>()
                .Select(fi => fi.Symbol?.Family?.Id)
                .Where(id => id != null)
                .Distinct()
                .ToHashSet();
            var unusedFamilies = families.Where(f => !usedFamilyIds.Contains(f.Id) && f.IsEditable).ToList();

            // CAD imports
            var cadImports = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();

            // Unplaced rooms
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList();
            var unplacedRooms = rooms.Where(r =>
            {
                var area = r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
                return area <= 0;
            }).ToList();

            // Unplaced views
            var placedViewIds = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                .Select(vp => vp.ViewId).ToHashSet();
            var unplacedViews = views.Where(v => !v.IsTemplate && !placedViewIds.Contains(v.Id)
                && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser
                && v.ViewType != ViewType.Internal && v.ViewType != ViewType.DrawingSheet).ToList();

            // Scoring
            double score = 100;
            var issues = new List<object>();

            void Penalize(double points, string issue, string recommendation)
            {
                score -= points;
                issues.Add(new { issue, severity = points > 10 ? "high" : points > 5 ? "medium" : "low", recommendation });
            }

            if (warnings.Count > 100) Penalize(15, $"{warnings.Count} warnings", "Review and resolve warnings");
            else if (warnings.Count > 50) Penalize(10, $"{warnings.Count} warnings", "Review and resolve warnings");
            else if (warnings.Count > 20) Penalize(5, $"{warnings.Count} warnings", "Review and resolve warnings");

            if (inPlaceFamilies.Count > 10) Penalize(15, $"{inPlaceFamilies.Count} in-place families", "Convert to loadable families");
            else if (inPlaceFamilies.Count > 5) Penalize(8, $"{inPlaceFamilies.Count} in-place families", "Convert to loadable families");

            if (unusedFamilies.Count > 50) Penalize(10, $"{unusedFamilies.Count} unused families", "Purge unused families");
            else if (unusedFamilies.Count > 20) Penalize(5, $"{unusedFamilies.Count} unused families", "Purge unused families");

            if (cadImports.Count > 10) Penalize(10, $"{cadImports.Count} CAD imports", "Clean up imported CAD files");
            else if (cadImports.Count > 3) Penalize(5, $"{cadImports.Count} CAD imports", "Clean up imported CAD files");

            if (unplacedRooms.Count > 10) Penalize(5, $"{unplacedRooms.Count} unplaced rooms", "Place or delete unplaced rooms");
            if (unplacedViews.Count > 50) Penalize(5, $"{unplacedViews.Count} unplaced views", "Delete or place unused views");

            score = Math.Max(0, Math.Min(100, score));
            var grade = score >= 90 ? "A" : score >= 80 ? "B" : score >= 70 ? "C" : score >= 60 ? "D" : "F";

            return CortexResult<object>.Ok(new
            {
                healthScore = Math.Round(score),
                grade,
                warningCount = warnings.Count,
                inPlaceFamilyCount = inPlaceFamilies.Count,
                unusedFamilyCount = unusedFamilies.Count,
                cadImportCount = cadImports.Count,
                unplacedRoomCount = unplacedRooms.Count,
                unplacedViewCount = unplacedViews.Count,
                issues
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

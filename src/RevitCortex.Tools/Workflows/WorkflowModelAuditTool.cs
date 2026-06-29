using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Workflows;

/// <summary>
/// Comprehensive model audit combining health check, warnings, and family analysis.
/// </summary>
[ToolSafety(true, false)]
public class WorkflowModelAuditTool : ICortexTool
{
    public string Name => "workflow_model_audit";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Comprehensive model audit combining health check, warnings, and family analysis.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeWarnings = input["includeWarnings"]?.Value<bool>() ?? true;
        var includeFamilies = input["includeFamilies"]?.Value<bool>() ?? true;
        var maxWarnings = input["maxWarnings"]?.Value<int>() ?? 50;

        try
        {
            // Model statistics
            var elementCount = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            var viewCount = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate);
            var sheetCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();

            // Warnings
            var allWarnings = doc.GetWarnings();
            var warningsByType = allWarnings
                .GroupBy(w => w.GetDescriptionText())
                .Select(g => new { description = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(maxWarnings)
                .ToList();

            // Families
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            var inPlaceFamilies = families.Where(f => f.IsInPlace).Select(f => new { name = f.Name, category = f.FamilyCategory?.Name }).ToList();

            var usedFamilyIds = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance).Cast<FamilyInstance>()
                .Select(fi => fi.Symbol?.Family?.Id).Where(id => id != null).Distinct().ToHashSet();
            var unusedFamilies = families.Where(f => !usedFamilyIds.Contains(f.Id) && f.IsEditable)
                .Select(f => new { name = f.Name, category = f.FamilyCategory?.Name }).ToList();

            // CAD imports
            var cadImports = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount();

            // Health score
            double score = 100;
            if (allWarnings.Count > 100) score -= 15;
            else if (allWarnings.Count > 50) score -= 10;
            else if (allWarnings.Count > 20) score -= 5;
            if (inPlaceFamilies.Count > 10) score -= 15;
            else if (inPlaceFamilies.Count > 5) score -= 8;
            if (unusedFamilies.Count > 50) score -= 10;
            else if (unusedFamilies.Count > 20) score -= 5;
            if (cadImports > 10) score -= 10;
            else if (cadImports > 3) score -= 5;
            score = Math.Max(0, Math.Min(100, score));

            var recommendations = new List<string>();
            if (unusedFamilies.Count > 10) recommendations.Add($"Purge {unusedFamilies.Count} unused families (use purge_unused tool)");
            if (inPlaceFamilies.Count > 3) recommendations.Add($"Convert {inPlaceFamilies.Count} in-place families to loadable families");
            if (cadImports > 3) recommendations.Add($"Clean up {cadImports} CAD imports (use cad_link_cleanup tool)");
            if (allWarnings.Count > 20) recommendations.Add($"Resolve {allWarnings.Count} warnings");

            return CortexResult<object>.Ok(new
            {
                healthScore = Math.Round(score),
                grade = score >= 90 ? "A" : score >= 80 ? "B" : score >= 70 ? "C" : score >= 60 ? "D" : "F",
                statistics = new { elementCount, viewCount, sheetCount, familyCount = families.Count },
                warningCount = allWarnings.Count,
                warnings = includeWarnings ? (object)warningsByType : null,
                inPlaceFamilyCount = inPlaceFamilies.Count,
                inPlaceFamilies = includeFamilies ? (object)inPlaceFamilies.Take(20).ToList() : null,
                unusedFamilyCount = unusedFamilies.Count,
                unusedFamilies = includeFamilies ? (object)unusedFamilies.Take(20).ToList() : null,
                cadImportCount = cadImports,
                recommendations
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

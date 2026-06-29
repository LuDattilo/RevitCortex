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
/// Retrieves all warnings/errors in the model with optional severity and
/// description filtering. Useful for model health auditing.
/// </summary>
[ToolSafety(true, false)]
public class GetWarningsTool : ICortexTool
{
    public string Name => "get_warnings";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Retrieves all warnings/errors in the model with optional severity and description filtering. Useful for model health auditing.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var severityFilter = input["severityFilter"]?.Value<string>() ?? "All";
        var maxWarnings    = input["maxWarnings"]?.Value<int>() ?? 500;
        var categoryFilter = input["categoryFilter"]?.Value<string>() ?? "";

        try
        {
            var allWarnings = doc.GetWarnings();
            var warnings = new List<object>();
            var severityCounts = new Dictionary<string, int>
            {
                { "Error", 0 },
                { "Warning", 0 }
            };

            int count = 0;
            foreach (var warning in allWarnings)
            {
                if (count >= maxWarnings) break;

                var severity = warning.GetSeverity().ToString();

                if (severityFilter != "All" && severity != severityFilter)
                    continue;

                var description = warning.GetDescriptionText();

                if (!string.IsNullOrEmpty(categoryFilter) &&
                    description.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (severityCounts.ContainsKey(severity))
                    severityCounts[severity]++;

                var failingIds = warning.GetFailingElements().Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return id.Value;
#else
                    return (long)id.IntegerValue;
#endif
                }).ToList();

                var additionalIds = warning.GetAdditionalElements().Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return id.Value;
#else
                    return (long)id.IntegerValue;
#endif
                }).ToList();

                warnings.Add(new
                {
                    severity,
                    description,
                    failingElementIds = failingIds,
                    additionalElementIds = additionalIds
                });

                count++;
            }

            return CortexResult<object>.Ok(new
            {
                totalWarnings    = allWarnings.Count,
                returnedWarnings = warnings.Count,
                severityCounts,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get warnings: {ex.Message}");
        }
    }
}

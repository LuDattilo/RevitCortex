using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists all project phases with sequence order and optionally phase filters.
/// </summary>
public class GetPhasesTool : ICortexTool, ICacheableTool
{
    public string Name => "get_phases";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all project phases with sequence order and optionally phase filters.";
    public CacheScope CacheScope => CacheScope.Document;
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var includePhaseFilters = input["includePhaseFilters"]?.Value<bool>() ?? true;

        try
        {
            var phases = new List<object>();
            foreach (Phase phase in doc.Phases)
            {
                phases.Add(new
                {
#if REVIT2024_OR_GREATER
                    id = phase.Id.Value,
#else
                    id = (long)phase.Id.IntegerValue,
#endif
                    name = phase.Name
                });
            }

            var result = new Dictionary<string, object>
            {
                ["phases"] = phases
            };

            if (includePhaseFilters)
            {
                var phaseFilters = new FilteredElementCollector(doc)
                    .OfClass(typeof(PhaseFilter))
                    .Cast<PhaseFilter>()
                    .Select(pf => new
                    {
#if REVIT2024_OR_GREATER
                        id = pf.Id.Value,
#else
                        id = (long)pf.Id.IntegerValue,
#endif
                        name = pf.Name
                    })
                    .ToList();

                result["phaseFilters"] = phaseFilters;
            }

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get phases: {ex.Message}");
        }
    }
}

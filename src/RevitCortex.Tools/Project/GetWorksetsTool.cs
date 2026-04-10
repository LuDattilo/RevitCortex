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
/// Lists worksets with open/close status and ownership info.
/// Only available for workshared documents (IsDynamic = true).
/// </summary>
public class GetWorksetsTool : ICortexTool
{
    public string Name => "get_worksets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        if (!doc.IsWorkshared)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Project is not workshared — worksets are not available",
                suggestion: "Use get_project_info to check isWorkshared before calling this tool");

        var includeSystemWorksets = input["includeSystemWorksets"]?.Value<bool>() ?? false;

        try
        {
            FilteredWorksetCollector wsCollector;
            if (includeSystemWorksets)
                wsCollector = new FilteredWorksetCollector(doc);
            else
                wsCollector = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset);

            var worksets = wsCollector.Select(ws => new
            {
                id                 = ws.Id.IntegerValue,
                name               = ws.Name,
                kind               = ws.Kind.ToString(),
                isOpen             = ws.IsOpen,
                isEditable         = ws.IsEditable,
                owner              = ws.Owner,
                isDefaultWorkset   = ws.IsDefaultWorkset,
                isVisibleByDefault = ws.IsVisibleByDefault
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                message = $"Retrieved {worksets.Count} workset(s)",
                worksetCount = worksets.Count,
                worksets
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get worksets: {ex.Message}");
        }
    }
}

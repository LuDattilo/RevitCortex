using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Returns metadata about the currently active view (name, type, scale, detail level).
/// </summary>
[ToolSafety(true, false)]
public class GetCurrentViewInfoTool : ICortexTool
{
    public string Name => "get_current_view_info";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns metadata about the currently active view (name, type, scale, detail level).";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        try
        {
            var activeView = doc.ActiveView;

            return CortexResult<object>.Ok(new
            {
#if REVIT2024_OR_GREATER
                id = activeView.Id.Value,
#else
                id = (long)activeView.Id.IntegerValue,
#endif
                uniqueId    = activeView.UniqueId,
                name        = activeView.Name,
                viewType    = activeView.ViewType.ToString(),
                isTemplate  = activeView.IsTemplate,
                scale       = activeView.Scale,
                detailLevel = activeView.DetailLevel.ToString()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get current view info: {ex.Message}");
        }
    }
}

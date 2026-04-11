using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Lists, applies, or removes view templates from views.
/// </summary>
public class ApplyViewTemplateTool : ICortexTool
{
    public string Name => "apply_view_template";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, applies, or removes view templates from views.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "apply";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListTemplates(doc),
                "apply" => ApplyTemplate(doc, input),
                "remove" => RemoveTemplate(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, apply, remove")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListTemplates(Document doc)
    {
        var templates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .Select(v => new { id = ToolHelpers.GetElementIdValue(v.Id), name = v.Name, viewType = v.ViewType.ToString() })
            .ToList();
        return CortexResult<object>.Ok(new { templateCount = templates.Count, templates });
    }

    private static CortexResult<object> ApplyTemplate(Document doc, JObject input)
    {
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var templateId = input["templateId"]?.Value<long>() ?? 0;
        var templateName = input["templateName"]?.Value<string>();

        // Resolve template
        View? template = null;
        if (templateId > 0)
        {
#if REVIT2024_OR_GREATER
            template = doc.GetElement(new ElementId(templateId)) as View;
#else
            template = doc.GetElement(new ElementId((int)templateId)) as View;
#endif
        }
        if (template == null && !string.IsNullOrEmpty(templateName))
        {
            template = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        }
        if (template == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View template not found");

        using var tx = new Transaction(doc, "RevitCortex: Apply View Template");
        tx.Start();
        int applied = 0;
        foreach (var vid in viewIds)
        {
#if REVIT2024_OR_GREATER
            var view = doc.GetElement(new ElementId(vid)) as View;
#else
            var view = doc.GetElement(new ElementId((int)vid)) as View;
#endif
            if (view != null && !view.IsTemplate) { view.ViewTemplateId = template.Id; applied++; }
        }
        tx.Commit();
        return CortexResult<object>.Ok(new { appliedCount = applied, templateName = template.Name });
    }

    private static CortexResult<object> RemoveTemplate(Document doc, JObject input)
    {
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        using var tx = new Transaction(doc, "RevitCortex: Remove View Template");
        tx.Start();
        int removed = 0;
        foreach (var vid in viewIds)
        {
#if REVIT2024_OR_GREATER
            var view = doc.GetElement(new ElementId(vid)) as View;
#else
            var view = doc.GetElement(new ElementId((int)vid)) as View;
#endif
            if (view != null && !view.IsTemplate) { view.ViewTemplateId = ElementId.InvalidElementId; removed++; }
        }
        tx.Commit();
        return CortexResult<object>.Ok(new { removedCount = removed });
    }
}

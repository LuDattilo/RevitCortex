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
/// Lists, duplicates, deletes, or renames view templates.
/// </summary>
[ToolSafety(false, true)]
public class ManageViewTemplatesTool : ICortexTool
{
    public string Name => "manage_view_templates";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, duplicates, deletes, or renames view templates.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListTemplates(doc, input),
                "duplicate" => DuplicateTemplate(doc, input),
                "delete" => DeleteTemplate(doc, input, session),
                "rename" => RenameTemplate(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, duplicate, delete, rename")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListTemplates(Document doc, JObject input)
    {
        var filterViewType = input["filterViewType"]?.Value<string>();
        var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.IsTemplate);
        if (!string.IsNullOrEmpty(filterViewType))
            templates = templates.Where(v => v.ViewType.ToString().Equals(filterViewType, StringComparison.OrdinalIgnoreCase));

        var result = templates.Select(v => new
        {
            id = ToolHelpers.GetElementIdValue(v.Id), name = v.Name, viewType = v.ViewType.ToString()
        }).ToList();
        return CortexResult<object>.Ok(new { templateCount = result.Count, templates = result });
    }

    private static CortexResult<object> DuplicateTemplate(Document doc, JObject input)
    {
        var templateIds = input["templateIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (templateIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "templateIds required");

        var results = new List<object>();
        using var tx = new Transaction(doc, "RevitCortex: Duplicate View Templates");
        tx.Start();
        foreach (var tid in templateIds)
        {
#if REVIT2024_OR_GREATER
            var template = doc.GetElement(new ElementId(tid)) as View;
#else
            var template = doc.GetElement(new ElementId((int)tid)) as View;
#endif
            if (template == null || !template.IsTemplate)
            {
                results.Add(new { originalId = tid, success = false,
                    message = template == null ? "Element not found" : "Not a view template" });
                continue;
            }

            ElementId newId;
            try
            {
                newId = template.Duplicate(ViewDuplicateOption.Duplicate);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                results.Add(new { originalId = tid, success = false,
                    message = $"Cannot duplicate template '{template.Name}': {ex.Message}" });
                continue;
            }
            var newView = doc.GetElement(newId) as View;
            if (newView != null)
                results.Add(new { originalId = tid, newId = ToolHelpers.GetElementIdValue(newId), newName = newView.Name });
        }
        tx.Commit();
        return CortexResult<object>.Ok(new { duplicatedCount = results.Count, templates = results });
    }

    private static CortexResult<object> DeleteTemplate(Document doc, JObject input, CortexSession session)
    {
        var templateIds = input["templateIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (!session.RequestConfirmation("delete view template(s)", templateIds.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        using var tx = new Transaction(doc, "RevitCortex: Delete View Templates");
        tx.Start();
        int deleted = 0;
        foreach (var tid in templateIds)
        {
#if REVIT2024_OR_GREATER
            var template = doc.GetElement(new ElementId(tid)) as View;
#else
            var template = doc.GetElement(new ElementId((int)tid)) as View;
#endif
            if (template != null && template.IsTemplate) { doc.Delete(template.Id); deleted++; }
        }
        tx.Commit();
        return CortexResult<object>.Ok(new { deletedCount = deleted });
    }

    private static CortexResult<object> RenameTemplate(Document doc, JObject input)
    {
        var templateId = input["templateId"]?.Value<long>() ?? 0;
        var newName = input["newName"]?.Value<string>();
        if (templateId <= 0 || string.IsNullOrEmpty(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "templateId and newName required");

#if REVIT2024_OR_GREATER
        var template = doc.GetElement(new ElementId(templateId)) as View;
#else
        var template = doc.GetElement(new ElementId((int)templateId)) as View;
#endif
        if (template == null || !template.IsTemplate)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View template not found");

        using var tx = new Transaction(doc, "RevitCortex: Rename View Template");
        tx.Start();
        var oldName = template.Name;
        template.Name = newName;
        tx.Commit();
        return CortexResult<object>.Ok(new { oldName, newName, templateId });
    }
}

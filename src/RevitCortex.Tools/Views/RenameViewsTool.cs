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
/// Renames views using find/replace, prefix, or suffix with optional view type filtering.
/// </summary>
public class RenameViewsTool : ICortexTool
{
    public string Name => "rename_views";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Renames views using find/replace, prefix, or suffix with optional view type filtering.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var operation = input["operation"]?.Value<string>() ?? "find_replace";
        var prefix = input["prefix"]?.Value<string>() ?? "";
        var suffix = input["suffix"]?.Value<string>() ?? "";
        var findText = input["findText"]?.Value<string>() ?? "";
        var replaceText = input["replaceText"]?.Value<string>() ?? "";
        var viewTypes = input["viewTypes"]?.ToObject<List<string>>() ?? new List<string>();
        var filterName = input["filterName"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        try
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate);

            if (viewTypes.Count > 0)
            {
                views = views.Where(v =>
                    viewTypes.Any(vt => v.ViewType.ToString().Equals(vt, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrEmpty(filterName))
                views = views.Where(v => v.Name.IndexOf(filterName, StringComparison.OrdinalIgnoreCase) >= 0);

            var results = new List<object>();

            var viewList = views.ToList();

            if (!dryRun)
            {
                if (!session.RequestConfirmation("rename", viewList.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Rename Views");
                tx.Start();

                foreach (var view in viewList)
                {
                    var oldName = view.Name;
                    var newName = ApplyRename(oldName, operation, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;

                    try
                    {
                        view.Name = newName;
                        results.Add(new { id = ToolHelpers.GetElementIdValue(view.Id), oldName, newName, success = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { id = ToolHelpers.GetElementIdValue(view.Id), oldName, newName, success = false, reason = ex.Message });
                    }
                }

                tx.Commit();
            }
            else
            {
                foreach (var view in viewList)
                {
                    var oldName = view.Name;
                    var newName = ApplyRename(oldName, operation, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;
                    results.Add(new { id = ToolHelpers.GetElementIdValue(view.Id), oldName, newName });
                }
            }

            return CortexResult<object>.Ok(new { dryRun, renamedCount = results.Count, results = results.Take(200).ToList() });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static string ApplyRename(string name, string op, string prefix, string suffix, string find, string replace)
    {
        return op.ToLowerInvariant() switch
        {
            "prefix" => prefix + name,
            "suffix" => name + suffix,
            "find_replace" => name.Replace(find, replace),
            _ => name
        };
    }
}

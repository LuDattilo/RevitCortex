using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Batch renames elements (views, sheets, levels, grids, rooms) using find/replace, prefix, suffix, or regex.
/// </summary>
public class BatchRenameTool : ICortexTool
{
    public string Name => "batch_rename";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Batch renames elements (views, sheets, levels, grids, rooms) using find/replace, prefix, suffix, or regex.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var targetCategory = input["targetCategory"]?.Value<string>();
        var findText = input["findText"]?.Value<string>() ?? "";
        var replaceText = input["replaceText"]?.Value<string>() ?? "";
        var prefix = input["prefix"]?.Value<string>() ?? "";
        var suffix = input["suffix"]?.Value<string>() ?? "";
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        try
        {
            List<Element> elements;

            if (elementIds.Count > 0)
            {
                elements = elementIds
                    .Select(id =>
                    {
#if REVIT2024_OR_GREATER
                        return doc.GetElement(new ElementId(id));
#else
                        return doc.GetElement(new ElementId((int)id));
#endif
                    })
                    .Where(e => e != null)
                    .ToList()!;
            }
            else if (!string.IsNullOrEmpty(targetCategory))
            {
                elements = (targetCategory.ToLowerInvariant() switch
                {
                    "views" => new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate).Cast<Element>(),
                    "sheets" => new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<Element>(),
                    "levels" => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Element>(),
                    "grids" => new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Element>(),
                    "rooms" => new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType().Cast<Element>(),
                    _ => Enumerable.Empty<Element>()
                }).ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "elementIds or targetCategory required");
            }

            var results = new List<object>();

            if (!dryRun)
            {
                if (!session.RequestConfirmation("rename", elements.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Batch Rename");
                tx.Start();

                foreach (var elem in elements)
                {
                    var oldName = GetName(elem);
                    var newName = ComputeNewName(oldName, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;

                    try
                    {
                        SetName(elem, newName);
                        results.Add(new { id = GetIdLong(elem.Id), oldName, newName, success = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { id = GetIdLong(elem.Id), oldName, newName, success = false, reason = ex.Message });
                    }
                }

                tx.Commit();
            }
            else
            {
                foreach (var elem in elements)
                {
                    var oldName = GetName(elem);
                    var newName = ComputeNewName(oldName, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;
                    results.Add(new { id = GetIdLong(elem.Id), oldName, newName });
                }
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                renamedCount = results.Count,
                results = results.Take(200).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static string ComputeNewName(string oldName, string prefix, string suffix, string findText, string replaceText)
    {
        var name = oldName;
        if (!string.IsNullOrEmpty(findText))
            name = name.Replace(findText, replaceText);
        if (!string.IsNullOrEmpty(prefix))
            name = prefix + name;
        if (!string.IsNullOrEmpty(suffix))
            name = name + suffix;
        return name;
    }

    private static string GetName(Element elem)
    {
        if (elem is ViewSheet sheet) return sheet.Name;
        if (elem is View view) return view.Name;
        return elem.Name;
    }

    private static void SetName(Element elem, string name)
    {
        if (elem is ViewSheet sheet) sheet.Name = name;
        else if (elem is View view) view.Name = name;
        else elem.Name = name;
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

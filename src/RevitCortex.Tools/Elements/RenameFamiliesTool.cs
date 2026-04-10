using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Renames loaded families with find/replace, prefix, or suffix.
/// </summary>
public class RenameFamiliesTool : ICortexTool
{
    public string Name => "rename_families";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var operation = input["operation"]?.Value<string>() ?? "prefix";
        var prefix = input["prefix"]?.Value<string>() ?? "";
        var suffix = input["suffix"]?.Value<string>() ?? "";
        var findText = input["findText"]?.Value<string>() ?? "";
        var replaceText = input["replaceText"]?.Value<string>() ?? "";
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var renameTypes = input["renameTypes"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        try
        {
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .Where(f => f.IsEditable);

            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => Utilities.CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToHashSet();
                families = families.Where(f => f.FamilyCategory != null && catIds.Contains(f.FamilyCategory.Id));
            }

            var results = new List<object>();

            if (!dryRun)
            {
                using var tx = new Transaction(doc, "RevitCortex: Rename Families");
                tx.Start();

                foreach (var fam in families.ToList())
                {
                    var oldName = fam.Name;
                    var newName = ApplyRename(oldName, operation, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;

                    try
                    {
                        fam.Name = newName;
                        results.Add(new { id = GetIdLong(fam.Id), oldName, newName, success = true });

                        if (renameTypes)
                        {
                            foreach (var symId in fam.GetFamilySymbolIds())
                            {
                                var sym = doc.GetElement(symId) as FamilySymbol;
                                if (sym == null) continue;
                                var oldTypeName = sym.Name;
                                var newTypeName = ApplyRename(oldTypeName, operation, prefix, suffix, findText, replaceText);
                                if (oldTypeName != newTypeName)
                                    try { sym.Name = newTypeName; } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { id = GetIdLong(fam.Id), oldName, newName, success = false, reason = ex.Message });
                    }
                }

                tx.Commit();
            }
            else
            {
                foreach (var fam in families.ToList())
                {
                    var oldName = fam.Name;
                    var newName = ApplyRename(oldName, operation, prefix, suffix, findText, replaceText);
                    if (oldName == newName) continue;
                    results.Add(new { id = GetIdLong(fam.Id), oldName, newName });
                }
            }

            return CortexResult<object>.Ok(new { dryRun, renamedCount = results.Count, results });
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

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

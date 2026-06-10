using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Exports loaded families as .rfa files to a specified folder.
/// </summary>
public class ExportFamiliesTool : ICortexTool
{
    public string Name => "export_families";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports loaded families as .rfa files to a specified folder.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var outputDirectory = input["outputDirectory"]?.Value<string>();
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var groupByCategory = input["groupByCategory"]?.Value<bool>() ?? true;
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(outputDirectory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "outputDirectory is required");

        try
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            // EditFamily throws on in-place families; mirror ListFamilySizesTool's guard.
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace);

            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => Utilities.CategoryResolver.ResolveToId(doc, c))
                    // C7: ResolveToId returns null for unrecognized names; guard before the
                    // InvalidElementId comparison (on net48 ElementId is a reference type).
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .ToHashSet();
                families = families.Where(f => f.FamilyCategory != null && catIds.Contains(f.FamilyCategory.Id));
            }

            var results = new List<object>();
            foreach (var family in families)
            {
                try
                {
                    var famDoc = doc.EditFamily(family);
                    if (famDoc == null)
                    {
                        results.Add(new { name = family.Name, success = false, reason = "Cannot edit family" });
                        continue;
                    }

                    // H18: ensure the opened family document is always closed, even when
                    // SaveAs throws (permissions, disk full, invalid path) — otherwise it
                    // leaks open in Revit and locks the file for the rest of the session.
                    try
                    {
                        var dir = outputDirectory;
                        if (groupByCategory && family.FamilyCategory != null)
                        {
                            dir = Path.Combine(outputDirectory, family.FamilyCategory.Name);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        }

                        var filePath = Path.Combine(dir, $"{family.Name}.rfa");
                        if (File.Exists(filePath) && !overwrite)
                        {
                            results.Add(new { name = family.Name, success = false, reason = "File exists (set overwrite: true)" });
                            continue;
                        }

                        famDoc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = true });
                        results.Add(new { name = family.Name, path = filePath, success = true });
                    }
                    finally
                    {
                        try { famDoc.Close(false); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { name = family.Name, success = false, reason = ex.Message });
                }
            }

            return CortexResult<object>.Ok(new
            {
                exportedCount = results.Count(r => ((dynamic)r).success),
                outputDirectory,
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

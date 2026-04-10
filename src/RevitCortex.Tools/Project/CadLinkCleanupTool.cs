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
/// Analyzes and cleans up imported/linked CAD files in the model.
/// </summary>
public class CadLinkCleanupTool : ICortexTool
{
    public string Name => "cad_link_cleanup";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Analyzes and cleans up imported/linked CAD files in the model.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";
        var deleteImports = input["deleteImports"]?.Value<bool>() ?? false;
        var deleteLinks = input["deleteLinks"]?.Value<bool>() ?? false;
        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();

        try
        {
            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Select(i => new
                {
                    Id = i.Id,
                    Name = i.Name,
                    IsLinked = i.IsLinked,
                    ViewSpecific = i.OwnerViewId != ElementId.InvalidElementId,
                    ViewName = i.OwnerViewId != ElementId.InvalidElementId
                        ? (doc.GetElement(i.OwnerViewId) as View)?.Name
                        : null
                })
                .ToList();

            if (action == "list")
            {
                return CortexResult<object>.Ok(new
                {
                    totalCount = imports.Count,
                    importCount = imports.Count(i => !i.IsLinked),
                    linkCount = imports.Count(i => i.IsLinked),
                    items = imports.Select(i => new
                    {
                        id = GetIdLong(i.Id),
                        name = i.Name,
                        type = i.IsLinked ? "link" : "import",
                        viewSpecific = i.ViewSpecific,
                        viewName = i.ViewName
                    }).ToList()
                });
            }

            // Delete action
            using var tx = new Transaction(doc, "RevitCortex: CAD Link Cleanup");
            tx.Start();
            int deleted = 0;

            var toDelete = imports.AsEnumerable();
            if (elementIds.Count > 0)
            {
                var idSet = elementIds.ToHashSet();
                toDelete = toDelete.Where(i => idSet.Contains(GetIdLong(i.Id)));
            }
            else
            {
                if (!deleteImports) toDelete = toDelete.Where(i => i.IsLinked);
                if (!deleteLinks) toDelete = toDelete.Where(i => !i.IsLinked);
            }

            foreach (var item in toDelete.ToList())
            {
                try { doc.Delete(item.Id); deleted++; } catch { }
            }

            tx.Commit();
            return CortexResult<object>.Ok(new { action = "delete", deletedCount = deleted });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
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

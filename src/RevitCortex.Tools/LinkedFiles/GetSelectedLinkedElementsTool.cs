using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Returns information about currently selected link instances and their loaded document contents.
/// Since MCP cannot trigger interactive element picking, this reads the current UI selection
/// and identifies any RevitLinkInstance elements, reporting their status and element summary.
/// </summary>
public class GetSelectedLinkedElementsTool : ICortexTool
{
    public string Name => "get_selected_linked_elements";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Returns info about currently selected link instances: load status, path, and element counts by category. Ask the user to select link instances first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeCategorySummary = input["includeCategorySummary"]?.Value<bool>() ?? true;
        var maxCategories = input["maxCategories"]?.Value<int>() ?? 20;

        try
        {
            var uiDoc = new UIDocument(doc);
            var selectedIds = uiDoc.Selection.GetElementIds();

            var linkResults = new List<object>();

            foreach (var id in selectedIds)
            {
                var element = doc.GetElement(id);
                var linkInstance = element as RevitLinkInstance;
                if (linkInstance == null) continue;

                var linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
                var isLoaded = linkType != null && RevitLinkType.IsLoaded(doc, linkType.Id);

                var path = "";
                try
                {
                    var extRef = linkType?.GetExternalFileReference();
                    if (extRef != null)
                        path = ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
                }
                catch { /* path unavailable */ }

                var transform = linkInstance.GetTotalTransform();
                var linkData = new Dictionary<string, object?>
                {
                    ["instanceId"] = GetIdLong(linkInstance.Id),
                    ["typeId"] = linkType != null ? GetIdLong(linkType.Id) : 0,
                    ["name"] = linkInstance.Name,
                    ["isLoaded"] = isLoaded,
                    ["isPinned"] = linkInstance.Pinned,
                    ["path"] = path,
                    ["origin"] = new
                    {
                        x = Math.Round(transform.Origin.X * MmPerFoot, 1),
                        y = Math.Round(transform.Origin.Y * MmPerFoot, 1),
                        z = Math.Round(transform.Origin.Z * MmPerFoot, 1)
                    }
                };

                // Add category summary if the link is loaded
                if (includeCategorySummary && isLoaded)
                {
                    var linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc != null)
                    {
                        var categoryCounts = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType()
                            .Where(e => e.Category != null)
                            .GroupBy(e => e.Category!.Name)
                            .Select(g => new { category = g.Key, count = g.Count() })
                            .OrderByDescending(c => c.count)
                            .Take(maxCategories)
                            .ToList();

                        var totalElements = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType()
                            .GetElementCount();

                        linkData["totalElements"] = totalElements;
                        linkData["categorySummary"] = categoryCounts;
                    }
                }

                linkResults.Add(linkData);
            }

            if (linkResults.Count == 0)
            {
                return CortexResult<object>.Ok(new
                {
                    message = selectedIds.Count == 0
                        ? "No elements selected. Ask the user to select one or more linked file instances."
                        : $"{selectedIds.Count} element(s) selected but none are link instances.",
                    selectedLinkCount = 0,
                    links = Array.Empty<object>()
                });
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Found {linkResults.Count} selected link instance(s)",
                selectedLinkCount = linkResults.Count,
                links = linkResults
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private const double MmPerFoot = 304.8;

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

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
/// Returns elements from Revit linked models, with optional filtering by link
/// name, categories (OST_* codes) and parameter extraction.
/// Mirrors the fork's GetLinkedElementsEventHandler logic.
/// </summary>
public class GetLinkedElementsTool : ICortexTool
{
    public string Name => "get_linked_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Parse inputs ───────────────────────────────────────────────────
        var linkName       = input["linkName"]?.ToString();
        var categories     = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var maxElements    = input["maxElements"]?.Value<int>() ?? 5000;

        try
        {
            // Find all RevitLinkInstance elements in the host document
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            // Optionally filter by partial link name (case-insensitive)
            if (!string.IsNullOrWhiteSpace(linkName))
            {
                linkInstances = linkInstances
                    .Where(li => li.Name.IndexOf(linkName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (linkInstances.Count == 0)
            {
                return CortexResult<object>.Ok(new
                {
                    message   = string.IsNullOrWhiteSpace(linkName)
                                    ? "No linked models found in the document"
                                    : $"No linked models matching '{linkName}' found",
                    linkCount = 0,
                    links     = Array.Empty<object>()
                });
            }

            var linksData = new List<object>();

            foreach (var linkInstance in linkInstances)
            {
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;  // link is unloaded or unavailable

                // Collect elements from the linked document
                List<Element> elements;

                if (categories.Count > 0)
                {
                    // Resolve OST_* category codes to BuiltInCategory and collect per-category
                    elements = new List<Element>();
                    foreach (var catCode in categories)
                    {
                        if (!Enum.TryParse<BuiltInCategory>(catCode, ignoreCase: true, out var bic))
                            continue;   // skip unknown category codes

                        var catElements = new FilteredElementCollector(linkDoc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToList();

                        elements.AddRange(catElements);
                    }

                    // Deduplicate in case an element somehow matched multiple categories
                    elements = elements
                        .GroupBy(e => e.Id)
                        .Select(g => g.First())
                        .ToList();
                }
                else
                {
                    elements = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType()
                        .Take(maxElements)
                        .ToList();
                }

                // Apply global max-elements cap after category collection
                if (elements.Count > maxElements)
                    elements = elements.Take(maxElements).ToList();

                var elementsData = elements.Select(e => BuildElementData(e, parameterNames)).ToList();

                linksData.Add(new
                {
                    linkName      = linkInstance.Name,
#if REVIT2024_OR_GREATER
                    linkId        = linkInstance.Id.Value,
#else
                    linkId        = (long)linkInstance.Id.IntegerValue,
#endif
                    documentTitle = linkDoc.Title,
                    elementCount  = elementsData.Count,
                    elements      = elementsData
                });
            }

            return CortexResult<object>.Ok(new
            {
                message   = $"Found {linksData.Count} linked model(s)",
                linkCount = linksData.Count,
                links     = linksData
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to retrieve linked elements: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildElementData(Element e, List<string> parameterNames)
    {
        var data = new Dictionary<string, object?>
        {
#if REVIT2024_OR_GREATER
            ["elementId"] = e.Id.Value,
#else
            ["elementId"] = (long)e.Id.IntegerValue,
#endif
            ["category"]  = e.Category?.Name ?? "",
            ["name"]      = e.Name
        };

        foreach (var pn in parameterNames)
        {
            var p = e.LookupParameter(pn);
            data[pn] = p != null && p.HasValue
                ? (object?)(p.AsValueString() ?? p.AsString() ?? "")
                : "";
        }

        return data;
    }
}

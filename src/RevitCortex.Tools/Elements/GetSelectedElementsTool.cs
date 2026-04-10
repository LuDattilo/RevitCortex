using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Returns the currently selected elements in the Revit UI.
/// Mirrors the fork's GetSelectedElementsEventHandler logic.
/// </summary>
public class GetSelectedElementsTool : ICortexTool
{
    public string Name => "get_selected_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns the currently selected elements in the Revit UI. Mirrors the fork's GetSelectedElementsEventHandler logic.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var limit = input["limit"]?.Value<int>() ?? 500;

        try
        {
            var uiDoc = new UIDocument(doc);
            var selectedIds = uiDoc.Selection.GetElementIds();

            IEnumerable<Element> selectedElements = selectedIds
                .Select(id => doc.GetElement(id))
                .Where(e => e != null);

            if (limit > 0)
                selectedElements = selectedElements.Take(limit);

            var elements = selectedElements.Select(e => new
            {
#if REVIT2024_OR_GREATER
                id = e.Id.Value,
#else
                id = (long)e.Id.IntegerValue,
#endif
                uniqueId = e.UniqueId,
                name     = e.Name,
                category = e.Category?.Name
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                message          = elements.Count == 0
                                       ? "No elements are currently selected"
                                       : $"Found {elements.Count} selected element(s)",
                selectedCount    = elements.Count,
                elements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to retrieve selected elements: {ex.Message}");
        }
    }
}

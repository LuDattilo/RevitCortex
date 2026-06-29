using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Lists saved selections or loads a named selection, optionally selecting elements in view.
/// </summary>
[ToolSafety(true, false)]
public class LoadSelectionTool : ICortexTool
{
    public string Name => "load_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists saved selections or loads a named selection, optionally selecting elements in view.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        var selectInView = input["selectInView"]?.Value<bool>() ?? true;

        try
        {
            var allFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            // List all if no name specified
            if (string.IsNullOrEmpty(name))
            {
                var selections = allFilters.Select(sf => new
                {
                    name = sf.Name,
                    id = ToolHelpers.GetElementIdValue(sf.Id),
                    elementCount = sf.GetElementIds().Count
                }).ToList();

                return CortexResult<object>.Ok(new
                {
                    selectionCount = selections.Count,
                    selections
                });
            }

            // Load specific selection
            var filter = allFilters.FirstOrDefault(sf =>
                sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call load_selection without a name to list all saved selections");

            var elementIds = filter.GetElementIds();

            if (selectInView)
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                uidoc.Selection.SetElementIds(elementIds);
            }

            var ids = elementIds.Select(id => ToolHelpers.GetElementIdValue(id)).ToList();

            return CortexResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                elementIds = ids,
                selectedInView = selectInView
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to load selection: {ex.Message}");
        }
    }
}

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
/// Saves the current selection or specified element IDs as a named selection filter.
/// </summary>
[ToolSafety(false, true)]
public class SaveSelectionTool : ICortexTool
{
    public string Name => "save_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Saves the current selection or specified element IDs as a named selection filter.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        var elementIds = input["elementIds"]?.ToObject<List<long>>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;

        try
        {
            ICollection<ElementId> ids;
            if (elementIds != null && elementIds.Count > 0)
            {
                ids = elementIds.Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return new ElementId(id);
#else
                    return new ElementId((int)id);
#endif
                }).ToList();
            }
            else
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                ids = uidoc.Selection.GetElementIds();
                if (ids.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "No elements selected and no elementIds provided");
            }

            // Check existing
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(sf => sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            using var tx = new Transaction(doc, "RevitCortex: Save Selection");
            tx.Start();

            if (existing != null)
            {
                if (!overwrite)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Selection '{name}' already exists. Set overwrite=true to replace.");
                doc.Delete(existing.Id);
            }

            var filter = SelectionFilterElement.Create(doc, name);
            filter.SetElementIds(ids);

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                overwritten = existing != null
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to save selection: {ex.Message}");
        }
    }
}

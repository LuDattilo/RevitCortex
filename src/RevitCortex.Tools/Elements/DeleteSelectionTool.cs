using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Deletes a named saved selection filter.
/// </summary>
public class DeleteSelectionTool : ICortexTool
{
    public string Name => "delete_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Deletes a named saved selection filter.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        try
        {
            var filter = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(sf => sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Selection '{name}' not found");

            if (!session.RequestConfirmation("delete", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            using var tx = new Transaction(doc, "RevitCortex: Delete Selection");
            tx.Start();
            doc.Delete(filter.Id);
            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                deletedSelection = name,
                success = true
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to delete selection: {ex.Message}");
        }
    }
}

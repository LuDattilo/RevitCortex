using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Deletes a material from the project (with confirmation).
/// </summary>
public class DeleteMaterialTool : ICortexTool
{
    public string Name => "delete_material";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Deletes a material from the project. Shows confirmation dialog before executing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var materialId   = input["materialId"]?.Value<long?>();
        var materialName = input["materialName"]?.Value<string>();

        if (materialId == null && string.IsNullOrWhiteSpace(materialName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide materialId or materialName",
                suggestion: "Use get_materials to find the material to delete");

        try
        {
            Material? material = null;

            if (materialId.HasValue)
            {
#if REVIT2024_OR_GREATER
                material = doc.GetElement(new ElementId(materialId.Value)) as Material;
#else
                material = doc.GetElement(new ElementId((int)materialId.Value)) as Material;
#endif
            }

            if (material == null && !string.IsNullOrWhiteSpace(materialName))
            {
                material = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
            }

            if (material == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Material not found (id={materialId}, name={materialName})",
                    suggestion: "Use get_materials to list available materials");

            var matName = material.Name;

            // Confirmation dialog
            if (!session.RequestConfirmation("delete material", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            using (var tx = new Transaction(doc, "RevitCortex: Delete Material"))
            {
                tx.Start();
                doc.Delete(material.Id);
                tx.Commit();
            }

            return CortexResult<object>.Ok(new
            {
                deleted = true,
                materialName = matName,
                message = $"Material '{matName}' deleted"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to delete material: {ex.Message}");
        }
    }
}

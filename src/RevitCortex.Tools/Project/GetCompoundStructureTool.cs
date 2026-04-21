using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Reads compound structure (stratigraphy/layers) from system family types:
/// Walls, Floors, Roofs, Ceilings.
/// </summary>
public class GetCompoundStructureTool : ICortexTool
{
    public string Name => "get_compound_structure";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reads compound structure (layer stratigraphy) from system family types: walls, floors, roofs, ceilings. Returns layer function, width, and material for each layer.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementId = input["elementId"]?.Value<long?>();
        var typeId    = input["typeId"]?.Value<long?>();
        var typeName  = input["typeName"]?.Value<string>();
        var category  = input["category"]?.Value<string>();

        try
        {
            // Resolve the host type with compound structure
            HostObjAttributes? hostType = null;
            string resolvedFrom = "";

            // Option 1: from element instance
            if (elementId.HasValue)
            {
#if REVIT2024_OR_GREATER
                var elem = doc.GetElement(new ElementId(elementId.Value));
#else
                var elem = doc.GetElement(new ElementId((int)elementId.Value));
#endif
                if (elem == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Element {elementId} not found");

                hostType = doc.GetElement(elem.GetTypeId()) as HostObjAttributes;
                resolvedFrom = "element";

                if (hostType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Element {elementId} is not a system family with compound structure (wall, floor, roof, ceiling)",
                        suggestion: "Provide the ID of a wall, floor, roof, or ceiling element");
            }
            // Option 2: from type ID directly
            else if (typeId.HasValue)
            {
#if REVIT2024_OR_GREATER
                hostType = doc.GetElement(new ElementId(typeId.Value)) as HostObjAttributes;
#else
                hostType = doc.GetElement(new ElementId((int)typeId.Value)) as HostObjAttributes;
#endif
                resolvedFrom = "typeId";

                if (hostType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Type {typeId} is not a system family type with compound structure");
            }
            // Option 3: from type name + category
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                hostType = FindTypeByName(doc, typeName!, category);
                resolvedFrom = "typeName";

                if (hostType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Type '{typeName}' not found" + (category != null ? $" in category {category}" : ""),
                        suggestion: "Use get_available_family_types to list available types");
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Provide elementId, typeId, or typeName to identify the system family type",
                    suggestion: "Example: {\"elementId\": 619340} or {\"typeName\": \"Generic - 200mm\", \"category\": \"OST_Walls\"}");
            }

            var cs = hostType.GetCompoundStructure();
            if (cs == null)
            {
                return CortexResult<object>.Ok(new
                {
                    typeName = hostType.Name,
                    typeCategory = hostType.Category?.Name ?? "",
                    hasCompoundStructure = false,
                    message = "This type does not have a compound structure (e.g., curtain wall, stacked wall)"
                });
            }

            // Read layers
            var layers = new List<object>();
            var csLayers = cs.GetLayers();
            for (int i = 0; i < csLayers.Count; i++)
            {
                var layer = csLayers[i];
                var matId = layer.MaterialId;
                string? matName = null;
                string? matClass = null;

                if (matId != ElementId.InvalidElementId)
                {
                    var mat = doc.GetElement(matId) as Material;
                    matName = mat?.Name;
                    matClass = mat?.MaterialClass;
                }

                long matIdValue;
#if REVIT2024_OR_GREATER
                matIdValue = matId.Value;
#else
                matIdValue = (long)matId.IntegerValue;
#endif

                layers.Add(new
                {
                    index = i,
                    function_ = layer.Function.ToString(),
                    widthMm = Math.Round(layer.Width * 304.8, 2),
                    widthFt = Math.Round(layer.Width, 6),
                    materialId = matIdValue,
                    materialName = matName ?? "(none)",
                    materialClass = matClass ?? "",
                    isStructural = layer.Function == MaterialFunctionAssignment.Structure,
                    isVariable = cs.VariableLayerIndex == i
                });
            }

            long typeIdValue;
#if REVIT2024_OR_GREATER
            typeIdValue = hostType.Id.Value;
#else
            typeIdValue = (long)hostType.Id.IntegerValue;
#endif

            return CortexResult<object>.Ok(new
            {
                typeId = typeIdValue,
                typeName = hostType.Name,
                typeCategory = hostType.Category?.Name ?? "",
                resolvedFrom,
                hasCompoundStructure = true,
                totalWidthMm = Math.Round(cs.GetWidth() * 304.8, 2),
                totalWidthFt = Math.Round(cs.GetWidth(), 6),
                layerCount = csLayers.Count,
                structuralLayerIndex = cs.StructuralMaterialIndex,
                isVerticallyCompound = cs.IsVerticallyCompound,
                layers
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get compound structure: {ex.Message}");
        }
    }

    private static HostObjAttributes? FindTypeByName(Document doc, string typeName, string? category)
    {
        var collector = new FilteredElementCollector(doc)
            .WhereElementIsElementType();

        if (!string.IsNullOrEmpty(category))
        {
            var catId = CategoryResolver.ResolveToId(doc, category!);
            if (catId != null && catId != ElementId.InvalidElementId)
                collector = collector.OfCategoryId(catId);
        }

        return collector
            .OfType<HostObjAttributes>()
            .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}

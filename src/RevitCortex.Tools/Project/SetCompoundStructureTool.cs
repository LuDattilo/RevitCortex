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
/// Modifies compound structure (stratigraphy/layers) on system family types:
/// Walls, Floors, Roofs, Ceilings.
/// Supports adding, removing, and modifying layers.
/// </summary>
public class SetCompoundStructureTool : ICortexTool
{
    public string Name => "set_compound_structure";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Modifies compound structure (layer stratigraphy) on system family types. Actions: replace, add, remove, modify (layers), and set_wrapping (openingWrapping/endCap/per-layer wraps).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var typeId   = input["typeId"]?.Value<long?>();
        var typeName = input["typeName"]?.Value<string>();
        var category = input["category"]?.Value<string>();
        var action   = input["action"]?.Value<string>() ?? "replace";
        var dryRun   = input["dryRun"]?.Value<bool>() ?? true;

        try
        {
            // Resolve host type
            HostObjAttributes? hostType = null;

            if (typeId.HasValue)
            {
#if REVIT2024_OR_GREATER
                hostType = doc.GetElement(new ElementId(typeId.Value)) as HostObjAttributes;
#else
                hostType = doc.GetElement(new ElementId((int)typeId.Value)) as HostObjAttributes;
#endif
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                hostType = FindTypeByName(doc, typeName!, category);
            }

            if (hostType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "System family type not found. Provide typeId or typeName+category.",
                    suggestion: "Use get_compound_structure to find the type first");

            var cs = hostType.GetCompoundStructure();
            if (cs == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Type '{hostType.Name}' does not have a compound structure (curtain/stacked wall?)");

            switch (action.ToLowerInvariant())
            {
                case "replace":
                    return ReplaceAllLayers(doc, hostType, cs, input, session, dryRun);

                case "add":
                    return AddLayer(doc, hostType, cs, input, session, dryRun);

                case "remove":
                    return RemoveLayer(doc, hostType, cs, input, session, dryRun);

                case "modify":
                    return ModifyLayer(doc, hostType, cs, input, session, dryRun);

                case "set_wrapping":
                    return SetWrapping(doc, hostType, cs, input, session, dryRun);

                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unknown action '{action}'. Use: replace, add, remove, modify, set_wrapping");
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to set compound structure: {ex.Message}");
        }
    }

    private CortexResult<object> ReplaceAllLayers(Document doc, HostObjAttributes hostType,
        CompoundStructure cs, JObject input, CortexSession session, bool dryRun)
    {
        var layersJson = input["layers"]?.ToObject<List<JObject>>();
        if (layersJson == null || layersJson.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "layers array is required for 'replace' action",
                suggestion: "Provide layers: [{function: \"Structure\", widthMm: 200, materialId: 12345}, ...]");

        var newLayers = new List<CompoundStructureLayer>();
        foreach (var lj in layersJson)
        {
            var (ok, layer) = ParseLayer(doc, lj);
            if (!ok || layer == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Invalid layer definition: {lj}. Required: function, widthMm or widthFt");
            newLayers.Add(layer);
        }

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                typeName = hostType.Name,
                action = "replace",
                currentLayerCount = cs.GetLayers().Count,
                newLayerCount = newLayers.Count,
                totalWidthMm = Math.Round(newLayers.Sum(l => l.Width) * 304.8, 2),
                layers = FormatLayers(doc, newLayers)
            });
        }

        var desc = $"Replace all layers on '{hostType.Name}' with {newLayers.Count} new layers " +
                   $"({Math.Round(newLayers.Sum(l => l.Width) * 304.8, 1)}mm total)";
        if (!session.RequestConfirmation("replace compound structure", 1, desc))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using (var tx = new Transaction(doc, "RevitCortex: Replace Compound Structure"))
        {
            tx.Start();
            cs.SetLayers(newLayers);

            // Set structural material index to the first Structure layer
            for (int i = 0; i < newLayers.Count; i++)
            {
                if (newLayers[i].Function == MaterialFunctionAssignment.Structure)
                {
                    cs.StructuralMaterialIndex = i;
                    break;
                }
            }

            // Validate before applying
            IDictionary<int, CompoundStructureError> errors;
            IDictionary<int, int> errorLayerMapping;
            if (!cs.IsValid(doc, out errors, out errorLayerMapping))
            {
                tx.RollBack();
                return BuildValidationError(doc, errors, newLayers);
            }

            hostType.SetCompoundStructure(cs);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            dryRun = false,
            typeName = hostType.Name,
            action = "replace",
            layerCount = newLayers.Count,
            totalWidthMm = Math.Round(newLayers.Sum(l => l.Width) * 304.8, 2),
            message = $"Replaced all layers on '{hostType.Name}'"
        });
    }

    private CortexResult<object> AddLayer(Document doc, HostObjAttributes hostType,
        CompoundStructure cs, JObject input, CortexSession session, bool dryRun)
    {
        var layerJson = input["layer"]?.ToObject<JObject>();
        var position  = input["position"]?.Value<int?>(); // null = append at end

        if (layerJson == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "layer object is required for 'add' action");

        var (ok, newLayer) = ParseLayer(doc, layerJson);
        if (!ok || newLayer == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Invalid layer definition. Required: function, widthMm or widthFt");

        var existingLayers = cs.GetLayers().ToList();
        int insertAt = position ?? existingLayers.Count;
        insertAt = Math.Max(0, Math.Min(existingLayers.Count, insertAt));

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                typeName = hostType.Name,
                action = "add",
                insertPosition = insertAt,
                currentLayerCount = existingLayers.Count,
                newLayerCount = existingLayers.Count + 1
            });
        }

        var addDesc = $"Add {newLayer.Function} layer at position {insertAt} on '{hostType.Name}' " +
                      $"({Math.Round(newLayer.Width * 304.8, 1)}mm)";
        if (!session.RequestConfirmation("add compound structure layer", 1, addDesc))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        existingLayers.Insert(insertAt, newLayer);

        using (var tx = new Transaction(doc, "RevitCortex: Add Compound Structure Layer"))
        {
            tx.Start();
            cs.SetLayers(existingLayers);
            hostType.SetCompoundStructure(cs);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            dryRun = false,
            typeName = hostType.Name,
            action = "add",
            insertPosition = insertAt,
            layerCount = existingLayers.Count,
            message = $"Added layer at position {insertAt} on '{hostType.Name}'"
        });
    }

    private CortexResult<object> RemoveLayer(Document doc, HostObjAttributes hostType,
        CompoundStructure cs, JObject input, CortexSession session, bool dryRun)
    {
        var layerIndex = input["layerIndex"]?.Value<int?>();
        if (layerIndex == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "layerIndex is required for 'remove' action",
                suggestion: "Use get_compound_structure to see layer indices");

        var existingLayers = cs.GetLayers().ToList();
        if (layerIndex < 0 || layerIndex >= existingLayers.Count)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"layerIndex {layerIndex} out of range (0-{existingLayers.Count - 1})");

        if (existingLayers.Count <= 1)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Cannot remove the last layer. Use 'replace' to redefine the structure.");

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                typeName = hostType.Name,
                action = "remove",
                removingLayerIndex = layerIndex,
                removingFunction = existingLayers[layerIndex.Value].Function.ToString(),
                currentLayerCount = existingLayers.Count,
                newLayerCount = existingLayers.Count - 1
            });
        }

        var removeDesc = $"Remove layer {layerIndex} ({existingLayers[layerIndex.Value].Function}) from '{hostType.Name}'";
        if (!session.RequestConfirmation("remove compound structure layer", 1, removeDesc))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        existingLayers.RemoveAt(layerIndex.Value);

        using (var tx = new Transaction(doc, "RevitCortex: Remove Compound Structure Layer"))
        {
            tx.Start();
            cs.SetLayers(existingLayers);
            hostType.SetCompoundStructure(cs);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            dryRun = false,
            typeName = hostType.Name,
            action = "remove",
            removedIndex = layerIndex,
            layerCount = existingLayers.Count,
            message = $"Removed layer at index {layerIndex} from '{hostType.Name}'"
        });
    }

    private CortexResult<object> ModifyLayer(Document doc, HostObjAttributes hostType,
        CompoundStructure cs, JObject input, CortexSession session, bool dryRun)
    {
        var layerIndex = input["layerIndex"]?.Value<int?>();
        if (layerIndex == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "layerIndex is required for 'modify' action");

        var existingLayers = cs.GetLayers().ToList();
        if (layerIndex < 0 || layerIndex >= existingLayers.Count)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"layerIndex {layerIndex} out of range (0-{existingLayers.Count - 1})");

        var layer = existingLayers[layerIndex.Value];
        var changes = new List<string>();

        // Parse optional changes
        var funcStr = input["function"]?.Value<string>();
        if (!string.IsNullOrEmpty(funcStr) && TryParseFunction(funcStr!, out var func))
        {
            layer.Function = func;
            changes.Add("function");
        }

        var widthMm = input["widthMm"]?.Value<double?>();
        var widthFt = input["widthFt"]?.Value<double?>();
        if (widthMm.HasValue)
        {
            layer.Width = widthMm.Value / 304.8;
            changes.Add("width");
        }
        else if (widthFt.HasValue)
        {
            layer.Width = widthFt.Value;
            changes.Add("width");
        }

        var materialId = input["materialId"]?.Value<long?>();
        var materialName = input["materialName"]?.Value<string>();
        if (materialId.HasValue)
        {
#if REVIT2024_OR_GREATER
            layer.MaterialId = new ElementId(materialId.Value);
#else
            layer.MaterialId = new ElementId((int)materialId.Value);
#endif
            changes.Add("material");
        }
        else if (!string.IsNullOrWhiteSpace(materialName))
        {
            var mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
            if (mat != null)
            {
                layer.MaterialId = mat.Id;
                changes.Add("material");
            }
        }

        if (changes.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No changes specified. Provide function, widthMm/widthFt, or materialId/materialName.");

        existingLayers[layerIndex.Value] = layer;

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                typeName = hostType.Name,
                action = "modify",
                layerIndex,
                changes,
                newWidthMm = Math.Round(layer.Width * 304.8, 2)
            });
        }

        var modifyDesc = $"Modify layer {layerIndex} on '{hostType.Name}': {string.Join(", ", changes)}";
        if (!session.RequestConfirmation("modify compound structure layer", 1, modifyDesc))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using (var tx = new Transaction(doc, "RevitCortex: Modify Compound Structure Layer"))
        {
            tx.Start();
            cs.SetLayers(existingLayers);
            hostType.SetCompoundStructure(cs);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            dryRun = false,
            typeName = hostType.Name,
            action = "modify",
            layerIndex,
            changes,
            message = $"Modified layer {layerIndex} on '{hostType.Name}': {string.Join(", ", changes)}"
        });
    }

    private CortexResult<object> SetWrapping(Document doc, HostObjAttributes hostType,
        CompoundStructure cs, JObject input, CortexSession session, bool dryRun)
    {
        var changes = new List<string>();

        // Opening (insert) wrapping: how layers wrap around doors/windows.
        var openingWrap = input["openingWrapping"]?.Value<string>();
        if (!string.IsNullOrEmpty(openingWrap))
        {
            cs.OpeningWrapping = openingWrap!.ToLowerInvariant().Replace("_", "").Replace(" ", "") switch
            {
                "none" or "donotwrap" => OpeningWrappingCondition.None,
                "exterior"            => OpeningWrappingCondition.Exterior,
                "interior"            => OpeningWrappingCondition.Interior,
                _                     => OpeningWrappingCondition.ExteriorAndInterior,
            };
            changes.Add("openingWrapping");
        }

        // End cap condition: how layers cap at wall ends.
        var endCap = input["endCap"]?.Value<string>();
        if (!string.IsNullOrEmpty(endCap))
        {
            cs.EndCap = endCap!.ToLowerInvariant().Replace("_", "").Replace(" ", "") switch
            {
                "none" or "noendcap"  => EndCapCondition.NoEndCap,
                "interior"            => EndCapCondition.Interior,
                _                     => EndCapCondition.Exterior,
            };
            changes.Add("endCap");
        }

        // Per-layer participation in wrapping: layerWrapping = [{layerIndex, wraps:bool}]
        var layerWrap = input["layerWrapping"] as JArray;
        if (layerWrap != null)
        {
            int layerCount = cs.GetLayers().Count;
            foreach (var lw in layerWrap.OfType<JObject>())
            {
                var idx = lw["layerIndex"]?.Value<int?>();
                var wraps = lw["wraps"]?.Value<bool?>();
                if (idx.HasValue && wraps.HasValue && idx.Value >= 0 && idx.Value < layerCount)
                {
                    cs.SetParticipatesInWrapping(idx.Value, wraps.Value);
                    changes.Add($"layer{idx.Value}.wraps");
                }
            }
        }

        if (changes.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No wrapping changes specified. Provide openingWrapping, endCap, or layerWrapping[].");

        if (dryRun)
            return CortexResult<object>.Ok(new
            {
                dryRun = true, typeName = hostType.Name, action = "set_wrapping", changes
            });

        if (!session.RequestConfirmation("set compound structure wrapping", 1, hostType.Name))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using (var tx = new Transaction(doc, "RevitCortex: Set Compound Structure Wrapping"))
        {
            tx.Start();
            hostType.SetCompoundStructure(cs);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            dryRun = false, typeName = hostType.Name, action = "set_wrapping", changes,
            message = $"Updated wrapping on '{hostType.Name}': {string.Join(", ", changes)}"
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static (bool ok, CompoundStructureLayer? layer) ParseLayer(Document doc, JObject lj)
    {
        var funcStr = lj["function"]?.Value<string>();
        if (string.IsNullOrEmpty(funcStr)) return (false, null);
        if (!TryParseFunction(funcStr!, out var func)) return (false, null);

        var widthMm = lj["widthMm"]?.Value<double?>();
        var widthFt = lj["widthFt"]?.Value<double?>();
        double width;
        if (func == MaterialFunctionAssignment.Membrane)
        {
            width = 0; // Revit requires Membrane layers to have exactly 0 width
        }
        else if (widthMm.HasValue) width = widthMm.Value / 304.8;
        else if (widthFt.HasValue) width = widthFt.Value;
        else return (false, null);

        var materialId = ElementId.InvalidElementId;
        var matIdVal = lj["materialId"]?.Value<long?>();
        var matName  = lj["materialName"]?.Value<string>();

        if (matIdVal.HasValue)
        {
#if REVIT2024_OR_GREATER
            materialId = new ElementId(matIdVal.Value);
#else
            materialId = new ElementId((int)matIdVal.Value);
#endif
        }
        else if (!string.IsNullOrWhiteSpace(matName))
        {
            var mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name.Equals(matName, StringComparison.OrdinalIgnoreCase));
            if (mat != null) materialId = mat.Id;
        }

        return (true, new CompoundStructureLayer(width, func, materialId));
    }

    private static bool TryParseFunction(string value, out MaterialFunctionAssignment result)
    {
        // Support both enum names and common aliases
        // Try direct enum parse first (handles exact names like "Structure", "Substrate", etc.)
        if (Enum.TryParse<MaterialFunctionAssignment>(value, true, out result))
            return true;

        // Try common aliases
        var normalized = value.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        switch (normalized)
        {
            case "structure":
                result = MaterialFunctionAssignment.Structure; return true;
            case "substrate":
                result = MaterialFunctionAssignment.Substrate; return true;
            case "thermal": case "airgap": case "insulation": case "thermalorair":
                result = MaterialFunctionAssignment.Insulation; return true;
            case "finish1": case "exteriorfinish":
                result = MaterialFunctionAssignment.Finish1; return true;
            case "finish2": case "interiorfinish":
                result = MaterialFunctionAssignment.Finish2; return true;
            case "membrane": case "membranelayer":
                result = MaterialFunctionAssignment.Membrane; return true;
            case "structuraldeck": case "deck":
                result = MaterialFunctionAssignment.StructuralDeck; return true;
            default:
                result = MaterialFunctionAssignment.Structure; return false;
        }
    }

    private static List<object> FormatLayers(Document doc, List<CompoundStructureLayer> layers)
    {
        var result = new List<object>();
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            string? matName = null;
            if (layer.MaterialId != ElementId.InvalidElementId)
                matName = (doc.GetElement(layer.MaterialId) as Material)?.Name;

            result.Add(new
            {
                index = i,
                function_ = layer.Function.ToString(),
                widthMm = Math.Round(layer.Width * 304.8, 2),
                materialName = matName ?? "(none)"
            });
        }
        return result;
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

    private static CortexResult<object> BuildValidationError(
        Document doc, IDictionary<int, CompoundStructureError> errors,
        List<CompoundStructureLayer> layers)
    {
        var errorList = new List<object>();
        var suggestions = new List<string>();

        foreach (var kvp in errors)
        {
            int idx = kvp.Key;
            var err = kvp.Value;
            var layerFunc = idx >= 0 && idx < layers.Count ? layers[idx].Function.ToString() : "?";
            var layerMm = idx >= 0 && idx < layers.Count ? Math.Round(layers[idx].Width * 304.8, 2) : 0.0;
            var (desc, fix) = DescribeError(err, layerFunc, layerMm);

            errorList.Add(new { layer = idx, function_ = layerFunc, widthMm = layerMm, error = err.ToString(), description = desc, fix });
            if (!string.IsNullOrEmpty(fix) && !suggestions.Contains(fix!))
                suggestions.Add(fix!);
        }

        var message = $"Validation failed on {errors.Count} layer(s):\n" +
                      string.Join("\n", errorList.Select(e =>
                      {
                          dynamic d = e;
                          return $"  Layer [{d.layer}] {d.function_} ({d.widthMm}mm): {d.description}";
                      }));

        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, message,
            suggestion: suggestions.Count > 0 ? string.Join("\n", suggestions) : null,
            context: new Dictionary<string, object>
            {
                ["validationErrors"] = errorList,
                ["layersSent"] = FormatLayers(doc, layers)
            });
    }

    private static (string description, string? fix) DescribeError(
        CompoundStructureError error, string layerFunction, double widthMm)
    {
        switch (error)
        {
            case CompoundStructureError.MembraneTooThick:
                return ($"Membrane layer must have 0mm width, got {widthMm}mm",
                    "Set widthMm to 0 for Membrane layers (the tool does this automatically — report this as a bug)");

            case CompoundStructureError.CoreTooThin:
                return ("Core (Structure) layer has zero or insufficient thickness",
                    "Increase the Structure layer width (minimum ~1mm)");

            case CompoundStructureError.NonmembraneTooThin:
                return ($"{layerFunction} layer at {widthMm}mm is below Revit minimum thickness",
                    $"Increase layer width — Revit requires non-membrane layers to be at least ~0.8mm");

            case CompoundStructureError.BadShellOrder:
                return ("Layer order is wrong: exterior layers must come before core, core before interior",
                    "Reorder layers: Finish1 → Substrate → Structure → Substrate → Finish2 (exterior to interior)");

            case CompoundStructureError.ThinOuterLayer:
                return ($"Face (finish) layer at {widthMm}mm is too thin",
                    "Increase Finish layer width — outer layers need minimum thickness");

            case CompoundStructureError.VerticalUnusedLayer:
                return ($"Non-membrane layer has 0mm width — only Membrane layers can be zero-width",
                    "Set a non-zero width for this layer, or change function to Membrane");

            case CompoundStructureError.BadShellsStructure:
                return ("Shell layer count exceeds total layers — invalid structure configuration",
                    "Ensure at least one Structure layer is defined and layer functions are correct");

            case CompoundStructureError.DeckCantBoundAbove:
                return ("StructuralDeck layer needs a layer above it",
                    "Add a layer (Finish or Substrate) above the StructuralDeck layer");

            case CompoundStructureError.DeckCantBoundBelow:
                return ("StructuralDeck layer needs a layer below it",
                    "Add a layer (Substrate or Structure) below the StructuralDeck layer");

            case CompoundStructureError.InvalidMaterialId:
                return ("Material ID does not reference a valid material in the project",
                    "Use materialName instead of materialId, or create the material first with create_material");

            case CompoundStructureError.VarThickLayerCantBeZero:
                return ("Variable thickness layer cannot have zero width",
                    "Set a non-zero width for variable thickness layers");

            default:
                return ($"Validation error: {error}", null);
        }
    }
}

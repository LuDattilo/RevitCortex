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
/// Sets identity, appearance, and product information on Revit materials.
/// Supports color, transparency, shininess, smoothness, class, category,
/// and identity parameters (description, manufacturer, model, URL, etc.).
/// </summary>
[ToolSafety(false, true)]
public class SetMaterialPropertiesTool : ICortexTool
{
    public string Name => "set_material_properties";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Sets identity, appearance (color, transparency, shininess, smoothness), class, product info, and assigns appearance/structural/thermal assets (by id) on Revit materials.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var requests = input["requests"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (requests.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "requests array is required");

        try
        {
            var results = new List<object>();

            if (!dryRun)
            {
                if (!session.RequestConfirmation("modify material properties", requests.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Set Material Properties");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                foreach (var req in requests)
                {
                    var materialId = req["materialId"]?.Value<long>() ?? 0;
#if REVIT2024_OR_GREATER
                    var mat = doc.GetElement(new ElementId(materialId)) as Material;
#else
                    var mat = doc.GetElement(new ElementId((int)materialId)) as Material;
#endif
                    if (mat == null)
                    {
                        results.Add(new { materialId, success = false, reason = "Material not found" });
                        continue;
                    }

                    var changes = new List<string>();

                    // Identity properties
                    SetIfPresent(req, "name", v => { mat.Name = v; changes.Add("name"); });

                    // Appearance properties
                    var colorHex = req["color"]?.Value<string>();
                    if (!string.IsNullOrEmpty(colorHex))
                    {
                        var c = ParseColor(colorHex!);
                        if (c != null) { mat.Color = c; changes.Add("color"); }
                    }

                    var transparency = req["transparency"]?.Value<int?>();
                    if (transparency.HasValue)
                    {
                        mat.Transparency = Math.Max(0, Math.Min(100, transparency.Value));
                        changes.Add("transparency");
                    }

                    var shininess = req["shininess"]?.Value<int?>();
                    if (shininess.HasValue)
                    {
                        mat.Shininess = Math.Max(0, Math.Min(128, shininess.Value));
                        changes.Add("shininess");
                    }

                    var smoothness = req["smoothness"]?.Value<int?>();
                    if (smoothness.HasValue)
                    {
                        mat.Smoothness = Math.Max(0, Math.Min(100, smoothness.Value));
                        changes.Add("smoothness");
                    }

                    // Class and category
                    SetIfPresent(req, "materialClass", v => { mat.MaterialClass = v; changes.Add("materialClass"); });
                    SetIfPresent(req, "materialCategory", v => { mat.MaterialCategory = v; changes.Add("materialCategory"); });

                    // Built-in parameters
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION, req, "description", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER, req, "manufacturer", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MODEL, req, "model", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_URL, req, "url", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_COST, req, "cost", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MARK, req, "mark", changes);
                    SetParamIfPresent(mat, BuiltInParameter.KEYNOTE_PARAM, req, "keynote", changes);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, req, "comments", changes);

                    // Asset assignment by id (appearance / structural / thermal).
                    AssignAsset(doc, req, "appearanceAssetId", typeof(AppearanceAssetElement),
                        id => mat.AppearanceAssetId = id, changes);
                    AssignAsset(doc, req, "structuralAssetId", typeof(PropertySetElement),
                        id => mat.StructuralAssetId = id, changes);
                    AssignAsset(doc, req, "thermalAssetId", typeof(PropertySetElement),
                        id => mat.ThermalAssetId = id, changes);

                    results.Add(new { materialId, name = mat.Name, success = true, changedProperties = changes });
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }
            else
            {
                foreach (var req in requests)
                {
                    var materialId = req["materialId"]?.Value<long>() ?? 0;
#if REVIT2024_OR_GREATER
                    var mat = doc.GetElement(new ElementId(materialId)) as Material;
#else
                    var mat = doc.GetElement(new ElementId((int)materialId)) as Material;
#endif
                    results.Add(mat != null
                        ? new { materialId, name = mat.Name, success = true, changedProperties = (object?)null }
                        : (object)new { materialId, success = false, reason = "Material not found", changedProperties = (object?)null });
                }
            }

            return CortexResult<object>.Ok(new { dryRun, modifiedCount = results.Count(r => ((dynamic)r).success), results });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Assigns an asset element to the material by id, after verifying the element exists
    /// and is of the expected type. A value of 0 or -1 clears the assignment (InvalidElementId).
    /// </summary>
    private static void AssignAsset(Document doc, JObject req, string key, Type expectedType,
        Action<ElementId> setter, List<string> changes)
    {
        var idVal = req[key]?.Value<long?>();
        if (!idVal.HasValue) return;

        try
        {
            if (idVal.Value <= 0)
            {
                setter(ElementId.InvalidElementId);
                changes.Add($"{key}=cleared");
                return;
            }
#if REVIT2024_OR_GREATER
            var assetId = new ElementId(idVal.Value);
#else
            var assetId = new ElementId((int)idVal.Value);
#endif
            var el = doc.GetElement(assetId);
            if (el == null || !expectedType.IsInstanceOfType(el))
            {
                changes.Add($"{key}=skipped(not a {expectedType.Name})");
                return;
            }
            setter(assetId);
            changes.Add(key);
        }
        catch (Exception ex) { changes.Add($"{key}=failed({ex.Message})"); }
    }

    private static void SetIfPresent(JObject req, string key, Action<string> setter)
    {
        var val = req[key]?.Value<string>();
        if (val != null) try { setter(val); } catch { }
    }

    private static void SetParamIfPresent(Material mat, BuiltInParameter bip, JObject req, string key, List<string> changes)
    {
        var val = req[key]?.Value<string>();
        if (val == null) return;
        var param = mat.get_Parameter(bip);
        if (param != null && !param.IsReadOnly)
        {
            if (param.StorageType == StorageType.String) { param.Set(val); changes.Add(key); }
            else if (param.StorageType == StorageType.Double && double.TryParse(val, out var d)) { param.Set(d); changes.Add(key); }
        }
    }

    private static Color? ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return null;
        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color(r, g, b);
        }
        catch { return null; }
    }
}

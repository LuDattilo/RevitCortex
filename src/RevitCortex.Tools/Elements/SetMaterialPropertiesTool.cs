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
/// Sets identity and product information on Revit materials.
/// </summary>
public class SetMaterialPropertiesTool : ICortexTool
{
    public string Name => "set_material_properties";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

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
                using var tx = new Transaction(doc, "RevitCortex: Set Material Properties");
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

                    SetIfPresent(req, "name", v => mat.Name = v);
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION, req, "description");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER, req, "manufacturer");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MODEL, req, "model");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_URL, req, "url");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_COST, req, "cost");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_MARK, req, "mark");
                    SetParamIfPresent(mat, BuiltInParameter.KEYNOTE_PARAM, req, "keynote");
                    SetParamIfPresent(mat, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, req, "comments");

                    results.Add(new { materialId, name = mat.Name, success = true });
                }

                tx.Commit();
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
                        ? new { materialId, name = mat.Name, success = true }
                        : (object)new { materialId, success = false, reason = "Material not found" });
                }
            }

            return CortexResult<object>.Ok(new { dryRun, modifiedCount = results.Count(r => ((dynamic)r).success), results });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static void SetIfPresent(JObject req, string key, Action<string> setter)
    {
        var val = req[key]?.Value<string>();
        if (val != null) try { setter(val); } catch { }
    }

    private static void SetParamIfPresent(Material mat, BuiltInParameter bip, JObject req, string key)
    {
        var val = req[key]?.Value<string>();
        if (val == null) return;
        var param = mat.get_Parameter(bip);
        if (param != null && !param.IsReadOnly)
        {
            if (param.StorageType == StorageType.String) param.Set(val);
            else if (param.StorageType == StorageType.Double && double.TryParse(val, out var d)) param.Set(d);
        }
    }
}

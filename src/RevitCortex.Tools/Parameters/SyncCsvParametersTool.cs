using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Parameters;

/// <summary>
/// Syncs element parameters from structured data (import from CSV/JSON array).
/// </summary>
public class SyncCsvParametersTool : ICortexTool
{
    public string Name => "sync_csv_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var data = input["data"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (data.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "data array is required (each item needs elementId and parameters object)");

        try
        {
            var results = new List<object>();

            if (!dryRun)
            {
                using var tx = new Transaction(doc, "RevitCortex: Sync CSV Parameters");
                tx.Start();

                foreach (var row in data)
                {
                    var elementId = row["elementId"]?.Value<long>() ?? 0;
                    var parameters = row["parameters"] as JObject;
                    if (elementId <= 0 || parameters == null)
                    {
                        results.Add(new { elementId, success = false, reason = "Missing elementId or parameters" });
                        continue;
                    }

#if REVIT2024_OR_GREATER
                    var elem = doc.GetElement(new ElementId(elementId));
#else
                    var elem = doc.GetElement(new ElementId((int)elementId));
#endif
                    if (elem == null)
                    {
                        results.Add(new { elementId, success = false, reason = "Element not found" });
                        continue;
                    }

                    int setCount = 0;
                    foreach (var prop in parameters.Properties())
                    {
                        var param = elem.LookupParameter(prop.Name);
                        if (param == null || param.IsReadOnly) continue;

                        var val = prop.Value.ToString();
                        try
                        {
                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    param.Set(val);
                                    setCount++;
                                    break;
                                case StorageType.Double when double.TryParse(val, out var d):
                                    param.Set(d);
                                    setCount++;
                                    break;
                                case StorageType.Integer when int.TryParse(val, out var i):
                                    param.Set(i);
                                    setCount++;
                                    break;
                            }
                        }
                        catch { }
                    }

                    results.Add(new { elementId, success = true, parametersSet = setCount });
                }

                tx.Commit();
            }
            else
            {
                foreach (var row in data)
                {
                    var elementId = row["elementId"]?.Value<long>() ?? 0;
                    var parameters = row["parameters"] as JObject;
#if REVIT2024_OR_GREATER
                    var elem = elementId > 0 ? doc.GetElement(new ElementId(elementId)) : null;
#else
                    var elem = elementId > 0 ? doc.GetElement(new ElementId((int)elementId)) : null;
#endif
                    var matchCount = parameters?.Properties()
                        .Count(p => elem?.LookupParameter(p.Name) != null) ?? 0;
                    results.Add(new { elementId, found = elem != null, matchingParameters = matchCount });
                }
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                processedCount = results.Count,
                results = results.Take(200).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

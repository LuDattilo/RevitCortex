using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Renumbers elements by assigning sequential numbers with optional prefix/suffix.
/// Defaults to dryRun=true for safety — preview renaming plan before committing.
/// Supports Rooms, Doors, Windows, Parking, or a custom parameterName.
/// Mirrors the fork's RenumberElementsEventHandler logic.
/// </summary>
public class RenumberElementsTool : ICortexTool
{
    public string Name => "renumber_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Renumbers elements by assigning sequential numbers with optional prefix/suffix. Defaults to dryRun=true for safety — preview renaming plan before committing. Supports Rooms, Doors, Windows, Parking, or a custom parameterName. Mirrors the fork's RenumberElementsEventHandler logic.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIdsToken = input["elementIds"];
        var targetCategory  = input["targetCategory"]?.Value<string>() ?? "";
        var parameterName   = input["parameterName"]?.Value<string>() ?? "";
        var startNumber     = input["startNumber"]?.Value<int>() ?? 1;
        var increment       = input["increment"]?.Value<int>() ?? 1;
        var prefix          = input["prefix"]?.Value<string>() ?? "";
        var suffix          = input["suffix"]?.Value<string>() ?? "";
        var sortBy          = input["sortBy"]?.Value<string>() ?? "location";
        var dryRun          = input["dryRun"]?.Value<bool>() ?? true;

        long[] rawIds = Array.Empty<long>();
        if (elementIdsToken != null && elementIdsToken.Type != JTokenType.Null)
        {
            try { rawIds = elementIdsToken.ToObject<long[]>() ?? Array.Empty<long>(); }
            catch { return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds must be an array of numbers"); }
        }

        // Require either elementIds or a known targetCategory
        if (rawIds.Length == 0 && string.IsNullOrWhiteSpace(targetCategory))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide elementIds or targetCategory (Rooms|Doors|Windows|Parking)",
                suggestion: "Example: {\"targetCategory\": \"Rooms\", \"startNumber\": 1, \"dryRun\": true}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        try
        {
            var elements = GetTargetElements(doc, rawIds, targetCategory);

            if (elements.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No elements found to renumber");

            elements = SortElements(elements, sortBy);

            var renumberResults = new List<object>();
            int currentNumber = startNumber;

            Transaction? tx = dryRun ? null : new Transaction(doc, "RevitCortex: Renumber Elements");
            try
            {
                tx?.Start();

                foreach (var elem in elements)
                {
                    string newValue = $"{prefix}{currentNumber}{suffix}";
                    string oldValue = GetCurrentNumber(elem, parameterName);
                    bool success = true;
                    string message = "";

                    if (!dryRun)
                    {
                        try
                        {
                            SetElementNumber(elem, newValue, parameterName);
                        }
                        catch (Exception ex)
                        {
                            success = false;
                            message = ex.Message;
                        }
                    }

                    renumberResults.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = elem.Id.Value,
#else
                        id = elem.Id.IntegerValue,
#endif
                        oldValue,
                        newValue,
                        success,
                        message
                    });

                    currentNumber += increment;
                }

                tx?.Commit();
            }
            catch
            {
                if (tx?.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }
            finally
            {
                tx?.Dispose();
            }

            return CortexResult<object>.Ok(new
            {
                message = dryRun
                    ? $"Preview: {renumberResults.Count} element(s) would be renumbered (dryRun). Set dryRun=false to execute."
                    : $"Renumbered {renumberResults.Count} element(s) successfully.",
                dryRun,
                totalProcessed = renumberResults.Count,
                renames = renumberResults
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Renumber elements failed: {ex.Message}");
        }
    }

    private static List<Element> GetTargetElements(Document doc, long[] rawIds, string targetCategory)
    {
        if (rawIds.Length > 0)
        {
            return rawIds
                .Select(id => doc.GetElement(ToElementId(id)))
                .Where(e => e != null)
                .ToList()!;
        }

        BuiltInCategory bic = targetCategory switch
        {
            "Rooms"   => BuiltInCategory.OST_Rooms,
            "Doors"   => BuiltInCategory.OST_Doors,
            "Windows" => BuiltInCategory.OST_Windows,
            "Parking" => BuiltInCategory.OST_Parking,
            _ => throw new ArgumentException($"Unknown targetCategory '{targetCategory}'. Use Rooms, Doors, Windows, or Parking.")
        };

        return new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();
    }

    private static List<Element> SortElements(List<Element> elements, string sortBy)
    {
        if (sortBy == "location")
        {
            return elements.OrderBy(e =>
            {
                if (e.Location is LocationPoint lp)
                    return lp.Point.X + lp.Point.Y * 10000;
                if (e.Location is LocationCurve lc)
                    return lc.Curve.GetEndPoint(0).X + lc.Curve.GetEndPoint(0).Y * 10000;
                return 0.0;
            }).ToList();
        }

        if (sortBy == "name")
            return elements.OrderBy(e => e.Name).ToList();

        // "none" or unknown — preserve original order
        return elements;
    }

    private static string GetCurrentNumber(Element elem, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(parameterName))
        {
            var param = elem.LookupParameter(parameterName);
            return param?.AsString() ?? param?.AsValueString() ?? "";
        }

        if (elem is Room room) return room.Number;

        var numberParam = elem.get_Parameter(BuiltInParameter.DOOR_NUMBER)
                       ?? elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
        return numberParam?.AsString() ?? "";
    }

    private static void SetElementNumber(Element elem, string value, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(parameterName))
        {
            var param = elem.LookupParameter(parameterName);
            if (param != null && !param.IsReadOnly)
            {
                param.Set(value);
                return;
            }
            throw new InvalidOperationException($"Parameter '{parameterName}' not found or is read-only on element {elem.Id}");
        }

        if (elem is Room room)
        {
            room.Number = value;
            return;
        }

        var numberParam = elem.get_Parameter(BuiltInParameter.DOOR_NUMBER)
                       ?? elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
        if (numberParam != null && !numberParam.IsReadOnly)
        {
            numberParam.Set(value);
        }
        else
        {
            throw new InvalidOperationException($"Cannot find a writable number parameter on element {elem.Id}");
        }
    }

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }
}

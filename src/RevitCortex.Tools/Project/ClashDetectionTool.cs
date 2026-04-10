using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Detects geometric intersections (clashes) between two sets of elements.
/// </summary>
public class ClashDetectionTool : ICortexTool
{
    public string Name => "clash_detection";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Detects geometric intersections (clashes) between two sets of elements.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryA = input["categoryA"]?.Value<string>();
        var categoryB = input["categoryB"]?.Value<string>();
        var elementIdsA = input["elementIdsA"]?.ToObject<List<long>>() ?? new List<long>();
        var elementIdsB = input["elementIdsB"]?.ToObject<List<long>>() ?? new List<long>();
        var toleranceMm = input["tolerance"]?.Value<double>() ?? 0;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;

        try
        {
            // Resolve set A
            List<Element> setA;
            if (elementIdsA.Count > 0)
            {
                setA = elementIdsA.Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return doc.GetElement(new ElementId(id));
#else
                    return doc.GetElement(new ElementId((int)id));
#endif
                }).Where(e => e != null).ToList()!;
            }
            else if (!string.IsNullOrEmpty(categoryA))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryA);
                setA = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType().ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryA or elementIdsA required");
            }

            // Resolve set B
            List<Element> setB;
            if (elementIdsB.Count > 0)
            {
                setB = elementIdsB.Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return doc.GetElement(new ElementId(id));
#else
                    return doc.GetElement(new ElementId((int)id));
#endif
                }).Where(e => e != null).ToList()!;
            }
            else if (!string.IsNullOrEmpty(categoryB))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryB);
                setB = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType().ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryB or elementIdsB required");
            }

            var tolerance = toleranceMm / MmPerFoot;
            var clashes = new List<object>();

            foreach (var a in setA)
            {
                if (clashes.Count >= maxResults) break;
                var bbA = a.get_BoundingBox(null);
                if (bbA == null) continue;

                foreach (var b in setB)
                {
                    if (clashes.Count >= maxResults) break;
                    if (a.Id == b.Id) continue;

                    var bbB = b.get_BoundingBox(null);
                    if (bbB == null) continue;

                    if (BoundingBoxesIntersect(bbA, bbB, tolerance))
                    {
                        clashes.Add(new
                        {
                            elementIdA = GetIdLong(a.Id),
                            elementIdB = GetIdLong(b.Id),
                            categoryA = a.Category?.Name,
                            categoryB = b.Category?.Name,
                            nameA = a.Name,
                            nameB = b.Name
                        });
                    }
                }
            }

            return CortexResult<object>.Ok(new
            {
                setACount = setA.Count,
                setBCount = setB.Count,
                clashCount = clashes.Count,
                clashes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
    {
        return a.Min.X - tolerance <= b.Max.X && a.Max.X + tolerance >= b.Min.X
            && a.Min.Y - tolerance <= b.Max.Y && a.Max.Y + tolerance >= b.Min.Y
            && a.Min.Z - tolerance <= b.Max.Z && a.Max.Z + tolerance >= b.Min.Z;
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}

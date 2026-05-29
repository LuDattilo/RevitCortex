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
/// Detects geometric intersections (clashes) between two sets of elements.
/// </summary>
public class ClashDetectionTool : ICortexTool
{
    public string Name => "clash_detection";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Detects geometric intersections (clashes) between two sets of elements. Uses true solid-geometry intersection by default (bounding-box pre-filter + ElementIntersectsElementFilter); set useSolidGeometry=false for a faster bbox-only approximation.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryA = input["categoryA"]?.Value<string>() ?? input["category1"]?.Value<string>();
        var categoryB = input["categoryB"]?.Value<string>() ?? input["category2"]?.Value<string>();
        var elementIdsA = input["elementIdsA"]?.ToObject<List<long>>() ?? new List<long>();
        var elementIdsB = input["elementIdsB"]?.ToObject<List<long>>() ?? new List<long>();
        var toleranceMm = input["tolerance"]?.Value<double>() ?? 0;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;
        // True solid-geometry intersection (default) vs. bbox-only approximation.
        var useSolidGeometry = input["useSolidGeometry"]?.Value<bool>() ?? true;

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
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryA!);
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
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryB!);
                setB = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType().ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryB or elementIdsB required");
            }

            var tolerance = toleranceMm / MmPerFoot;
            var clashes = new List<object>();

            // Pre-cache B bounding boxes (and a B id-set) once for the bbox pre-filter.
            var setBWithBoxes = setB
                .Select(b => new { Elem = b, Box = b.get_BoundingBox(null) })
                .Where(x => x.Box != null)
                .ToList();

            foreach (var a in setA)
            {
                if (clashes.Count >= maxResults) break;
                var bbA = a.get_BoundingBox(null);
                if (bbA == null) continue;

                // Bounding-box pre-filter: cheap, narrows the candidates.
                var candidates = setBWithBoxes
                    .Where(x => x.Elem.Id != a.Id && BoundingBoxesIntersect(bbA, x.Box!, tolerance))
                    .Select(x => x.Elem)
                    .ToList();
                if (candidates.Count == 0) continue;

                // Solid-geometry confirmation: ElementIntersectsElementFilter tests the
                // actual solids, eliminating bbox false positives (e.g. an L-shaped beam
                // whose box overlaps but whose solid does not).
                HashSet<long>? solidHitIds = null;
                if (useSolidGeometry)
                {
                    try
                    {
                        var candidateIds = candidates.Select(c => c.Id).ToList();
                        var intersecting = new FilteredElementCollector(doc, candidateIds)
                            .WherePasses(new ElementIntersectsElementFilter(a))
                            .ToElementIds();
                        solidHitIds = new HashSet<long>(intersecting.Select(id => ToolHelpers.GetElementIdValue(id)));
                    }
                    catch
                    {
                        // Some elements (no solid geometry) make the filter throw — fall
                        // back to the bbox candidates for this A rather than dropping it.
                        solidHitIds = null;
                    }
                }

                foreach (var b in candidates)
                {
                    if (clashes.Count >= maxResults) break;
                    if (solidHitIds != null && !solidHitIds.Contains(ToolHelpers.GetElementIdValue(b.Id)))
                        continue;

                    clashes.Add(new
                    {
                        elementIdA = ToolHelpers.GetElementIdValue(a.Id),
                        elementIdB = ToolHelpers.GetElementIdValue(b.Id),
                        categoryA = a.Category?.Name,
                        categoryB = b.Category?.Name,
                        nameA = a.Name,
                        nameB = b.Name
                    });
                }
            }

            return CortexResult<object>.Ok(new
            {
                setACount = setA.Count,
                setBCount = setB.Count,
                method = useSolidGeometry ? "solid_geometry" : "bounding_box",
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
}

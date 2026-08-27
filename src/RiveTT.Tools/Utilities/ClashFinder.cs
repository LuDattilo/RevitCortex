using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// The one clash-detection pass, shared by detect_clashes and show_clashes (renamed from
/// clash_detection / workflow_clash_review — R1/§6: kept as two tools because one is
/// read-only and the other writes a review view, a distinction the ribbon write-lock cannot
/// express on a single tool).
///
/// The two used to disagree: detect_clashes confirmed every bounding-box candidate with
/// ElementIntersectsElementFilter, while show_clashes stopped at the boxes. The review tool
/// therefore reported MORE clashes than the plain one on the same model — an L-shaped beam
/// whose box overlaps a duct but whose solid does not counted as a hit, and the reviewer
/// opened a 3D view on nothing. Same pass, same answer, one place.
///
/// The bounding-box test stays as a pre-filter: it is cheap and narrows the candidate set
/// before the expensive solid test. Only its role as the FINAL answer was wrong.
/// </summary>
public static class ClashFinder
{
    /// <summary>One confirmed pair.</summary>
    public sealed class Hit
    {
        public long ElementIdA { get; init; }
        public long ElementIdB { get; init; }
        public string? CategoryA { get; init; }
        public string? CategoryB { get; init; }
        public string? NameA { get; init; }
        public string? NameB { get; init; }
    }

    /// <summary>The hits plus the combined extent, for a section box around them.</summary>
    public sealed class Result
    {
        public List<Hit> Hits { get; } = new();

        /// <summary>Minimum corner of every clashing pair's combined box, in feet. Null when no hit.</summary>
        public XYZ? Min { get; internal set; }

        /// <summary>Maximum corner of every clashing pair's combined box, in feet. Null when no hit.</summary>
        public XYZ? Max { get; internal set; }

        /// <summary>"solid_geometry" or "bounding_box" — what actually decided the hits.</summary>
        public string Method { get; internal set; } = "solid_geometry";

        /// <summary>True when maxResults cut the search short, so the count is a floor, not a total.</summary>
        public bool Truncated { get; internal set; }
    }

    /// <summary>
    /// Finds the pairs of <paramref name="setA"/> x <paramref name="setB"/> that intersect.
    /// </summary>
    /// <param name="toleranceFt">Box inflation in FEET (Revit internal units), not mm.</param>
    /// <param name="useSolidGeometry">
    /// True (the default everywhere) confirms each box candidate against the real solids.
    /// False keeps the box result, which over-reports.
    /// </param>
    public static Result Find(
        Document doc,
        IList<Element> setA,
        IList<Element> setB,
        double toleranceFt,
        int maxResults,
        bool useSolidGeometry)
    {
        var result = new Result
        {
            Method = useSolidGeometry ? "solid_geometry" : "bounding_box"
        };

        var setBWithBoxes = setB
            .Select(b => new { Elem = b, Box = b.get_BoundingBox(null) })
            .Where(x => x.Box != null)
            .ToList();

        foreach (var a in setA)
        {
            if (result.Hits.Count >= maxResults) { result.Truncated = true; break; }

            var boxA = a.get_BoundingBox(null);
            if (boxA == null) continue;

            // Cheap pre-filter first.
            var candidates = setBWithBoxes
                .Where(x => x.Elem.Id != a.Id && BoxesIntersect(boxA, x.Box!, toleranceFt))
                .ToList();
            if (candidates.Count == 0) continue;

            // Solid confirmation: eliminates the box false positives.
            HashSet<long>? solidHitIds = null;
            if (useSolidGeometry)
            {
                try
                {
                    var candidateIds = candidates.Select(c => c.Elem.Id).ToList();
                    var intersecting = new FilteredElementCollector(doc, candidateIds)
                        .WherePasses(new ElementIntersectsElementFilter(a))
                        .ToElementIds();
                    solidHitIds = new HashSet<long>(intersecting.Select(ToolHelpers.GetElementIdValue));
                }
                catch
                {
                    // Elements without solid geometry make the filter throw — keep this A's
                    // box candidates rather than dropping it silently.
                    solidHitIds = null;
                }
            }

            foreach (var candidate in candidates)
            {
                if (result.Hits.Count >= maxResults) { result.Truncated = true; break; }

                var b = candidate.Elem;
                if (solidHitIds != null && !solidHitIds.Contains(ToolHelpers.GetElementIdValue(b.Id)))
                    continue;

                result.Hits.Add(new Hit
                {
                    ElementIdA = ToolHelpers.GetElementIdValue(a.Id),
                    ElementIdB = ToolHelpers.GetElementIdValue(b.Id),
                    CategoryA = a.Category?.Name,
                    CategoryB = b.Category?.Name,
                    NameA = a.Name,
                    NameB = b.Name
                });

                Accumulate(result, boxA, candidate.Box!);
            }
        }

        return result;
    }

    /// <summary>Grows the running extent to cover one more clashing pair.</summary>
    private static void Accumulate(Result result, BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        var min = new XYZ(
            Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z));
        var max = new XYZ(
            Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z));

        result.Min = result.Min == null
            ? min
            : new XYZ(Math.Min(result.Min.X, min.X), Math.Min(result.Min.Y, min.Y), Math.Min(result.Min.Z, min.Z));
        result.Max = result.Max == null
            ? max
            : new XYZ(Math.Max(result.Max.X, max.X), Math.Max(result.Max.Y, max.Y), Math.Max(result.Max.Z, max.Z));
    }

    public static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double toleranceFt)
    {
        return a.Min.X - toleranceFt <= b.Max.X && a.Max.X + toleranceFt >= b.Min.X
            && a.Min.Y - toleranceFt <= b.Max.Y && a.Max.Y + toleranceFt >= b.Min.Y
            && a.Min.Z - toleranceFt <= b.Max.Z && a.Max.Z + toleranceFt >= b.Min.Z;
    }
}

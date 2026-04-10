using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Workflows;

/// <summary>
/// Detects clashes between two categories and optionally creates a section box view.
/// </summary>
public class WorkflowClashReviewTool : ICortexTool
{
    public string Name => "workflow_clash_review";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryA = input["categoryA"]?.Value<string>();
        var categoryB = input["categoryB"]?.Value<string>();
        var toleranceMm = input["tolerance"]?.Value<double>() ?? 0;
        var createSectionBox = input["createSectionBox"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(categoryA) || string.IsNullOrEmpty(categoryB))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryA and categoryB required");

        try
        {
            var catIdA = Utilities.CategoryResolver.ResolveToId(doc, categoryA);
            var catIdB = Utilities.CategoryResolver.ResolveToId(doc, categoryB);
            if (catIdA == ElementId.InvalidElementId || catIdB == ElementId.InvalidElementId)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Category not found");

            var setA = new FilteredElementCollector(doc).OfCategoryId(catIdA).WhereElementIsNotElementType().ToList();
            var setB = new FilteredElementCollector(doc).OfCategoryId(catIdB).WhereElementIsNotElementType().ToList();
            var tolerance = toleranceMm / MmPerFoot;

            var clashes = new List<object>();
            XYZ? minPt = null, maxPt = null;

            foreach (var a in setA)
            {
                if (clashes.Count >= 100) break;
                var bbA = a.get_BoundingBox(null);
                if (bbA == null) continue;

                foreach (var b in setB)
                {
                    if (clashes.Count >= 100) break;
                    if (a.Id == b.Id) continue;
                    var bbB = b.get_BoundingBox(null);
                    if (bbB == null) continue;

                    if (bbA.Min.X - tolerance <= bbB.Max.X && bbA.Max.X + tolerance >= bbB.Min.X
                        && bbA.Min.Y - tolerance <= bbB.Max.Y && bbA.Max.Y + tolerance >= bbB.Min.Y
                        && bbA.Min.Z - tolerance <= bbB.Max.Z && bbA.Max.Z + tolerance >= bbB.Min.Z)
                    {
                        clashes.Add(new { elementIdA = GetIdLong(a.Id), elementIdB = GetIdLong(b.Id),
                            nameA = a.Name, nameB = b.Name });

                        // Track combined bounding box for section box
                        var cMin = new XYZ(Math.Min(bbA.Min.X, bbB.Min.X), Math.Min(bbA.Min.Y, bbB.Min.Y), Math.Min(bbA.Min.Z, bbB.Min.Z));
                        var cMax = new XYZ(Math.Max(bbA.Max.X, bbB.Max.X), Math.Max(bbA.Max.Y, bbB.Max.Y), Math.Max(bbA.Max.Z, bbB.Max.Z));
                        minPt = minPt == null ? cMin : new XYZ(Math.Min(minPt.X, cMin.X), Math.Min(minPt.Y, cMin.Y), Math.Min(minPt.Z, cMin.Z));
                        maxPt = maxPt == null ? cMax : new XYZ(Math.Max(maxPt.X, cMax.X), Math.Max(maxPt.Y, cMax.Y), Math.Max(maxPt.Z, cMax.Z));
                    }
                }
            }

            long? sectionBoxViewId = null;
            if (createSectionBox && clashes.Count > 0 && minPt != null && maxPt != null)
            {
                using var tx = new Transaction(doc, "RevitCortex: Clash Review Section Box");
                tx.Start();
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (vft != null)
                {
                    var offset = 3.0; // ~1m offset
                    var view3D = View3D.CreateIsometric(doc, vft.Id);
                    view3D.Name = $"ClashReview_{DateTime.Now:HHmmss}";
                    view3D.SetSectionBox(new BoundingBoxXYZ
                    {
                        Min = new XYZ(minPt.X - offset, minPt.Y - offset, minPt.Z - offset),
                        Max = new XYZ(maxPt.X + offset, maxPt.Y + offset, maxPt.Z + offset)
                    });
                    sectionBoxViewId = GetIdLong(view3D.Id);
                }
                tx.Commit();
            }

            return CortexResult<object>.Ok(new
            {
                categoryA, categoryB,
                setACount = setA.Count, setBCount = setB.Count,
                clashCount = clashes.Count,
                sectionBoxViewId,
                clashes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
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

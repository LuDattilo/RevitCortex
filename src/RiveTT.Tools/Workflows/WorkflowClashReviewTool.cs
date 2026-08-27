using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Workflows;

/// <summary>
/// Detects clashes between two categories and optionally creates a section box view.
/// Renamed from workflow_clash_review to show_clashes: "show_" already names this shape of
/// operation elsewhere (show_cross_model_elements) — a lookup plus a view built around the
/// result — and reads better than the double verb detect_show_clashes would have.
/// </summary>
[ToolSafety(false, false)]
public class WorkflowClashReviewTool : ICortexTool
{
    public string Name => "show_clashes";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Detects clashes between two categories and optionally creates a 3D section-boxed view for review. "
        + "Uses the same true solid-geometry intersection as detect_clashes (bounding-box pre-filter, then "
        + "ElementIntersectsElementFilter); set useSolidGeometry=false for the faster box-only approximation.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryA = input["categoryA"]?.Value<string>() ?? input["category1"]?.Value<string>();
        var categoryB = input["categoryB"]?.Value<string>() ?? input["category2"]?.Value<string>();
        var toleranceMm = input["tolerance"]?.Value<double>() ?? 0;
        var createSectionBox = input["createSectionBox"]?.Value<bool>() ?? true;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;
        // Same default as detect_clashes: the composed tool must not be laxer than the
        // plain one it wraps.
        var useSolidGeometry = input["useSolidGeometry"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(categoryA) || string.IsNullOrEmpty(categoryB))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryA and categoryB required");

        try
        {
            var catIdA = Utilities.CategoryResolver.ResolveToId(doc, categoryA!);
            var catIdB = Utilities.CategoryResolver.ResolveToId(doc, categoryB!);
            if (catIdA == null || catIdB == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Category not found");

            var setA = new FilteredElementCollector(doc).OfCategoryId(catIdA).WhereElementIsNotElementType().ToList();
            var setB = new FilteredElementCollector(doc).OfCategoryId(catIdB).WhereElementIsNotElementType().ToList();

            // Bounding boxes alone made this tool report more clashes than detect_clashes
            // on the same model, and open a review view on pairs whose solids never touch.
            // Both now run the same pass.
            var found = ClashFinder.Find(
                doc, setA, setB, toleranceMm / MmPerFoot, maxResults, useSolidGeometry);

            var clashes = found.Hits;
            var minPt = found.Min;
            var maxPt = found.Max;

            long? sectionBoxViewId = null;
            if (createSectionBox && clashes.Count > 0 && minPt != null && maxPt != null)
            {
                using var tx = new Transaction(doc, "RiveTT: Clash Review Section Box");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
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
                    sectionBoxViewId = ToolHelpers.GetElementIdValue(view3D.Id);
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            // Build suggestion if a set is empty
            string? suggestion = null;
            if (setB.Count == 0 && categoryB!.Contains("Structural"))
            {
                suggestion = $"No elements found for '{categoryB}'. " +
                    "Architectural models may use 'OST_Columns' instead of 'OST_StructuralColumns'. " +
                    "Try the non-structural variant.";
            }
            else if (setA.Count == 0 && categoryA!.Contains("Structural"))
            {
                suggestion = $"No elements found for '{categoryA}'. Try the non-structural variant.";
            }

            return CortexResult<object>.Ok(new
            {
                categoryA, categoryB,
                setACount = setA.Count, setBCount = setB.Count,
                method = found.Method,
                clashCount = clashes.Count,
                truncated = found.Truncated,
                maxResults,
                sectionBoxViewId,
                suggestion,
                clashes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}

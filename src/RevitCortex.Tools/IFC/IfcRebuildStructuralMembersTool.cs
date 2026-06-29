using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reconstructs structural columns and beams from IFC-imported DirectShape elements.
/// Columns use point-based NewFamilyInstance; beams use curve-based NewFamilyInstance.
/// </summary>
[ToolSafety(false, false)]
public class IfcRebuildStructuralMembersTool : ICortexTool
{
    public string Name => "ifc_rebuild_structural_members";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit columns and beams from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var memberType = input["memberType"]?.Value<string>() ?? "all";
        var familySymbolIdRaw = input["familySymbolId"]?.Value<long>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // Gather candidates
        List<DirectShape> candidates;
        if (elementIds != null && elementIds.Length > 0)
        {
            candidates = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            var columns = memberType != "beams"
                ? IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_StructuralColumns)
                : new List<DirectShape>();
            var beams = memberType != "columns"
                ? IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_StructuralFraming)
                : new List<DirectShape>();
            candidates = columns.Concat(beams).ToList();
        }

        FamilySymbol? userSymbol = null;
        if (familySymbolIdRaw.HasValue)
        {
            userSymbol = doc!.GetElement(ToolHelpers.ToElementId(familySymbolIdRaw.Value)) as FamilySymbol;
            if (userSymbol == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"FamilySymbol {familySymbolIdRaw.Value} not found");
        }

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        if (!dryRun)
        {
            if (!session.RequestConfirmation("rebuild structural members", candidates.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        }

        // One TransactionGroup per invocation: the N per-element commits collapse
        // into a single undo step, and a mid-run failure can no longer leave a
        // fragmented undo stack behind.
        using TransactionGroup? txGroup = dryRun ? null : new TransactionGroup(doc!, "RevitCortex: Rebuild Structural Members");
        txGroup?.Start();

        foreach (var ds in candidates)
        {
            var catName = ds.Category?.Name ?? "";
            var isColumn = catName.ToLowerInvariant().Contains("column") ||
                           catName.ToLowerInvariant().Contains("pilastr");

            if (isColumn)
            {
                var profile = IfcGeometryHelper.ExtractColumnProfile(ds);
                if (profile == null)
                {
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        type = "column",
                        status = "skipped",
                        reason = "Could not extract column profile",
                    });
                    continue;
                }

                var level = IfcGeometryHelper.FindNearestLevel(doc!, profile.BaseElevation);
                if (level == null)
                {
                    // H20: record the skip so the result count matches the candidate count.
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "column",
                        status = "skipped", reason = "No level found near the base elevation",
                    });
                    continue;
                }

                if (dryRun)
                {
                    rebuilt++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        type = "column",
                        status = "would_rebuild",
                        levelName = level.Name,
                        heightMm = Math.Round(profile.Height * MmPerFoot, 0),
                        widthMm = Math.Round(profile.CrossSectionWidth * MmPerFoot, 0),
                        depthMm = Math.Round(profile.CrossSectionDepth * MmPerFoot, 0),
                    });
                    continue;
                }

                try
                {
                    var symbol = userSymbol ?? FindColumnSymbol(doc!);
                    if (symbol == null)
                    {
                        skipped++;
                        results.Add(new
                        {
                            sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                            name = ds.Name, type = "column",
                            status = "skipped", reason = "No matching column family found",
                        });
                        continue;
                    }

                    using var tx = new Transaction(doc!, "RevitCortex: Rebuild Column");
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    // H-IFC-ACT: Activate() writes to the document — must run inside the transaction.
                    if (!symbol.IsActive) symbol.Activate();
                    var inst = doc!.Create.NewFamilyInstance(
                        profile.CenterPoint, symbol, level, StructuralType.Column);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException(
                            "Revit rolled back the transaction: " + TransactionFailureHandling.Describe(txFailures));

                    rebuilt++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "column",
                        status = "rebuilt",
                        newElementId = ToolHelpers.GetElementIdValue(inst.Id),
                    });
                }
                catch (Exception ex)
                {
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "column",
                        status = "failed", reason = ex.Message,
                    });
                }
            }
            else // beam
            {
                var profile = IfcGeometryHelper.ExtractBeamProfile(ds);
                if (profile == null)
                {
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "beam",
                        status = "skipped", reason = "Could not extract beam profile",
                    });
                    continue;
                }

                var level = IfcGeometryHelper.FindNearestLevel(doc!, profile.Elevation);
                if (level == null)
                {
                    // H20: record the skip so the result count matches the candidate count.
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "beam",
                        status = "skipped", reason = "No level found near the elevation",
                    });
                    continue;
                }

                if (dryRun)
                {
                    rebuilt++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "beam",
                        status = "would_rebuild",
                        levelName = level.Name,
                        lengthMm = Math.Round(profile.StartPoint.DistanceTo(profile.EndPoint) * MmPerFoot, 0),
                    });
                    continue;
                }

                try
                {
                    var symbol = userSymbol ?? FindBeamSymbol(doc!);
                    if (symbol == null)
                    {
                        skipped++;
                        results.Add(new
                        {
                            sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                            name = ds.Name, type = "beam",
                            status = "skipped", reason = "No matching beam family found",
                        });
                        continue;
                    }

                    using var tx = new Transaction(doc!, "RevitCortex: Rebuild Beam");
                    var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    tx.Start();
                    // H-IFC-ACT: Activate() writes to the document — must run inside the transaction.
                    if (!symbol.IsActive) symbol.Activate();
                    var line = Line.CreateBound(profile.StartPoint, profile.EndPoint);
                    var inst = doc!.Create.NewFamilyInstance(
                        line, symbol, level, StructuralType.Beam);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException(
                            "Revit rolled back the transaction: " + TransactionFailureHandling.Describe(txFailures));

                    rebuilt++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "beam",
                        status = "rebuilt",
                        newElementId = ToolHelpers.GetElementIdValue(inst.Id),
                    });
                }
                catch (Exception ex)
                {
                    skipped++;
                    results.Add(new
                    {
                        sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name, type = "beam",
                        status = "failed", reason = ex.Message,
                    });
                }
            }
        }

        if (txGroup != null && txGroup.GetStatus() == TransactionStatus.Started)
            txGroup.Assimilate();

        return CortexResult<object>.Ok(new { dryRun, totalCandidates = candidates.Count, rebuilt, skipped, results });
    }

    private static FamilySymbol? FindColumnSymbol(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }

    private static FamilySymbol? FindBeamSymbol(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }
}

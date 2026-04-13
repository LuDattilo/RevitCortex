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
/// Places family instances (doors, windows) from IFC-imported DirectShapes.
/// Uses bounding box center for placement and tries to find matching family symbols.
/// </summary>
public class IfcRebuildFamilyInstancesTool : ICortexTool
{
    public string Name => "ifc_rebuild_family_instances";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild doors, windows, and other family instances from IFC DirectShapes";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

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
            var doors = (categoryFilter == null || categoryFilter == "OST_Doors")
                ? IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Doors)
                : new List<DirectShape>();
            var windows = (categoryFilter == null || categoryFilter == "OST_Windows")
                ? IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Windows)
                : new List<DirectShape>();
            var generic = categoryFilter == "OST_GenericModel"
                ? IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_GenericModel)
                : new List<DirectShape>();
            candidates = doors.Concat(windows).Concat(generic).ToList();
        }

        // Find host walls for door/window placement
        var walls = new FilteredElementCollector(doc!)
            .OfClass(typeof(Wall))
            .Cast<Wall>()
            .ToList();

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        foreach (var ds in candidates)
        {
            var bb = ds.get_BoundingBox(null);
            if (bb == null) { skipped++; continue; }

            var catName = ds.Category?.Name ?? "Unknown";
            var isDoor = catName.ToLowerInvariant().Contains("door") || catName.ToLowerInvariant().Contains("port");
            var isWindow = catName.ToLowerInvariant().Contains("window") || catName.ToLowerInvariant().Contains("finestr");

            var builtInCat = isDoor ? BuiltInCategory.OST_Doors
                           : isWindow ? BuiltInCategory.OST_Windows
                           : BuiltInCategory.OST_GenericModel;

            var symbol = new FilteredElementCollector(doc!)
                .OfCategory(builtInCat)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            if (symbol == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    category = catName,
                    status = "skipped",
                    reason = $"No FamilySymbol found for category {builtInCat}",
                });
                continue;
            }

            var center = new XYZ(
                (bb.Min.X + bb.Max.X) / 2,
                (bb.Min.Y + bb.Max.Y) / 2,
                bb.Min.Z);

            var level = IfcGeometryHelper.FindNearestLevel(doc!, bb.Min.Z);
            if (level == null) { skipped++; continue; }

            // Find host wall (for doors/windows)
            Wall? hostWall = (isDoor || isWindow) ? FindNearestWall(walls, center) : null;

            if (dryRun)
            {
                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    category = catName,
                    status = "would_rebuild",
                    symbolName = symbol.Name,
                    levelName = level.Name,
                    hasHostWall = hostWall != null,
                    widthMm = Math.Round((bb.Max.X - bb.Min.X) * MmPerFoot, 0),
                    heightMm = Math.Round((bb.Max.Z - bb.Min.Z) * MmPerFoot, 0),
                });
                continue;
            }

            try
            {
                if (!symbol.IsActive) symbol.Activate();

                using var tx = new Transaction(doc!, "RevitCortex: Place Family Instance");
                tx.Start();

                FamilyInstance inst;
                if (hostWall != null && (isDoor || isWindow))
                {
                    inst = doc!.Create.NewFamilyInstance(
                        center, symbol, hostWall, level, StructuralType.NonStructural);
                }
                else
                {
                    inst = doc!.Create.NewFamilyInstance(
                        center, symbol, level, StructuralType.NonStructural);
                }

                tx.Commit();

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
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
                    name = ds.Name,
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        return CortexResult<object>.Ok(new { dryRun, totalCandidates = candidates.Count, rebuilt, skipped, results });
    }

    private static Wall? FindNearestWall(List<Wall> walls, XYZ point)
    {
        Wall? nearest = null;
        double minDist = double.MaxValue;

        foreach (var wall in walls)
        {
            var wallBb = wall.get_BoundingBox(null);
            if (wallBb == null) continue;

            var wallCenter = new XYZ(
                (wallBb.Min.X + wallBb.Max.X) / 2,
                (wallBb.Min.Y + wallBb.Max.Y) / 2,
                (wallBb.Min.Z + wallBb.Max.Z) / 2);

            var dist = point.DistanceTo(wallCenter);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = wall;
            }
        }

        // Only return if within 2 feet (~600mm)
        return minDist < 2.0 ? nearest : null;
    }
}

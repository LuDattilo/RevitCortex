# IFC Native Reconstruction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 10 MCP tools that analyze IFC-imported elements and reconstruct them as native Revit elements (walls, floors, roofs, structural members, openings, family instances), with QA comparison and fallback tagging.

**Architecture:** After an IFC file is imported (via Step 1 tools), the document contains DirectShape elements. This Step 2 pipeline analyzes those DirectShapes, determines which can be rebuilt as native Revit elements, and performs the reconstruction per-category. Each rebuilder tool is independent — they share a common analysis helper (`IfcGeometryHelper`) for extracting geometry from DirectShapes and matching to levels. All tools live in `src/RevitCortex.Tools/IFC/` with schemas in `server/src/schemas/ifc.ts` (appended) and TS registrations following the existing pattern.

**Tech Stack:** C# (.NET 8 / .NET Framework 4.8 multi-target), TypeScript, Zod, Revit API 2023-2027 (Wall.Create, Floor.Create, NewFamilyInstance, NewOpening, NewFootPrintRoof, DirectShape geometry extraction), xUnit

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Tools/IFC/IfcGeometryHelper.cs` | Shared helper: extract geometry from DirectShapes, find bounding boxes, match levels, detect geometry type (extrusion/sweep/brep/mesh) |
| `src/RevitCortex.Tools/IFC/IfcAnalyzeRebuildabilityTool.cs` | Scan imported IFC elements, classify each as rebuildable or not, return confidence scores |
| `src/RevitCortex.Tools/IFC/IfcListRebuildCandidatesTool.cs` | Filter and list elements that pass the rebuildability threshold |
| `src/RevitCortex.Tools/IFC/IfcRebuildWallsTool.cs` | Reconstruct walls from DirectShapes using `Wall.Create` |
| `src/RevitCortex.Tools/IFC/IfcRebuildFloorsTool.cs` | Reconstruct floors from DirectShapes using `Floor.Create` |
| `src/RevitCortex.Tools/IFC/IfcRebuildRoofsTool.cs` | Reconstruct roofs from DirectShapes using `NewFootPrintRoof` |
| `src/RevitCortex.Tools/IFC/IfcRebuildStructuralMembersTool.cs` | Reconstruct columns and beams using `NewFamilyInstance` |
| `src/RevitCortex.Tools/IFC/IfcRebuildOpeningsTool.cs` | Cut openings in rebuilt walls/floors using `NewOpening` |
| `src/RevitCortex.Tools/IFC/IfcRebuildFamilyInstancesTool.cs` | Place family instances (doors, windows) into rebuilt host elements |
| `src/RevitCortex.Tools/IFC/IfcCompareOriginalVsRebuiltTool.cs` | Compare geometry/volume/area between source DirectShape and rebuilt element |
| `src/RevitCortex.Tools/IFC/IfcTagUnreconstructableElementsTool.cs` | Tag elements that cannot be rebuilt with a parameter or comment |
| `server/src/tools/ifc_analyze_rebuildability.ts` | TS registration |
| `server/src/tools/ifc_list_rebuild_candidates.ts` | TS registration |
| `server/src/tools/ifc_rebuild_walls.ts` | TS registration |
| `server/src/tools/ifc_rebuild_floors.ts` | TS registration |
| `server/src/tools/ifc_rebuild_roofs.ts` | TS registration |
| `server/src/tools/ifc_rebuild_structural_members.ts` | TS registration |
| `server/src/tools/ifc_rebuild_openings.ts` | TS registration |
| `server/src/tools/ifc_rebuild_family_instances.ts` | TS registration |
| `server/src/tools/ifc_compare_original_vs_rebuilt.ts` | TS registration |
| `server/src/tools/ifc_tag_unreconstructable_elements.ts` | TS registration |

### Modified Files

| File | Change |
|------|--------|
| `server/src/schemas/ifc.ts` | Append 10 new Zod schemas for Step 2 tools |
| `server/src/tools/register.ts` | Add 10 imports + 10 entries in `toolRegistrations` array |
| `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` | Update expected tool count (123 → 133) |
| `src/RevitCortex.Plugin/CortexRouter.cs` | Add `ifc_analyze_`, `ifc_compare_` to read-only prefixes |

---

## Conversion Constants

All Revit API length values are in **feet**. IFC uses **millimeters**. Conversion constant:

```csharp
private const double MmPerFoot = 304.8;
```

All tools that work with coordinates must convert input mm to feet (÷ 304.8) and output feet to mm (× 304.8).

---

## Task 1: IfcGeometryHelper — Shared Utility

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcGeometryHelper.cs`

- [ ] **Step 1: Create the helper class**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Shared geometry utilities for IFC reconstruction tools.
/// Extracts geometry from DirectShape elements, matches levels, detects geometry types.
/// </summary>
public static class IfcGeometryHelper
{
    private const double MmPerFoot = 304.8;

    /// <summary>
    /// Extract all non-degenerate Solids from an element's geometry.
    /// Handles both top-level solids and nested GeometryInstance solids.
    /// </summary>
    public static List<Solid> GetSolids(Element element)
    {
        var solids = new List<Solid>();
        var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
        var geom = element.get_Geometry(opts);
        if (geom == null) return solids;

        foreach (var obj in geom)
        {
            if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
            {
                solids.Add(solid);
            }
            else if (obj is GeometryInstance inst)
            {
                foreach (var instObj in inst.GetInstanceGeometry())
                {
                    if (instObj is Solid s && s.Faces.Size > 0 && s.Volume > 1e-9)
                        solids.Add(s);
                }
            }
        }
        return solids;
    }

    /// <summary>
    /// Get the combined bounding box of an element in mm.
    /// </summary>
    public static (XYZ min, XYZ max)? GetBoundingBoxMm(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;
        return (
            new XYZ(bb.Min.X * MmPerFoot, bb.Min.Y * MmPerFoot, bb.Min.Z * MmPerFoot),
            new XYZ(bb.Max.X * MmPerFoot, bb.Max.Y * MmPerFoot, bb.Max.Z * MmPerFoot)
        );
    }

    /// <summary>
    /// Compute total volume in cubic meters from all solids of an element.
    /// </summary>
    public static double GetVolumeCubicMeters(Element element)
    {
        var solids = GetSolids(element);
        double totalFt3 = solids.Sum(s => s.Volume);
        return totalFt3 * 0.0283168; // ft³ to m³
    }

    /// <summary>
    /// Find the nearest level at or below a given elevation (in feet).
    /// </summary>
    public static Level? FindNearestLevel(Document doc, double elevationFeet)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderByDescending(l => l.Elevation)
            .ToList();

        foreach (var level in levels)
        {
            if (level.Elevation <= elevationFeet + 0.01) // small tolerance
                return level;
        }
        return levels.LastOrDefault(); // lowest level as fallback
    }

    /// <summary>
    /// Find level by name (case-insensitive).
    /// </summary>
    public static Level? FindLevelByName(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detect geometry type of an element based on its solids.
    /// Returns: "extrusion", "sweep", "brep", "mesh", or "unknown".
    /// </summary>
    public static string DetectGeometryType(Element element)
    {
        var solids = GetSolids(element);
        if (solids.Count == 0)
        {
            // Check for mesh-only geometry
            var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
            var geom = element.get_Geometry(opts);
            if (geom != null && geom.Any(g => g is Mesh))
                return "mesh";
            return "unknown";
        }

        // Simple heuristic: count planar vs curved faces
        int planarFaces = 0;
        int curvedFaces = 0;
        foreach (var solid in solids)
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace) planarFaces++;
                else curvedFaces++;
            }
        }

        if (curvedFaces == 0 && planarFaces <= 8)
            return "extrusion"; // box-like, likely extruded profile
        if (curvedFaces > 0 && curvedFaces <= planarFaces)
            return "sweep"; // has curved surfaces but mostly planar
        return "brep"; // complex boundary representation
    }

    /// <summary>
    /// Extract the bottom face footprint of a solid as a CurveLoop.
    /// Returns null if no clear bottom planar face is found.
    /// </summary>
    public static CurveLoop? ExtractBottomFootprint(Solid solid)
    {
        PlanarFace? bottomFace = null;
        double lowestZ = double.MaxValue;

        foreach (Face face in solid.Faces)
        {
            if (face is PlanarFace pf)
            {
                // A bottom face has a normal pointing down (negative Z)
                if (pf.FaceNormal.Z < -0.9)
                {
                    var origin = pf.Origin;
                    if (origin.Z < lowestZ)
                    {
                        lowestZ = origin.Z;
                        bottomFace = pf;
                    }
                }
            }
        }

        if (bottomFace == null) return null;

        // Get the outer edge loop of the bottom face
        var edgeLoops = bottomFace.GetEdgesAsCurveLoops();
        return edgeLoops.Count > 0 ? edgeLoops[0] : null;
    }

    /// <summary>
    /// Try to extract a wall-like linear profile: base line + height + thickness.
    /// Returns null if the element doesn't look like a wall.
    /// </summary>
    public static WallProfile? ExtractWallProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        // A wall has one dimension much larger than the other horizontal one
        double length, thickness;
        XYZ startPt, endPt;

        if (dx > dy * 3)
        {
            // Wall runs along X
            length = dx;
            thickness = dy;
            double midY = (bb.Min.Y + bb.Max.Y) / 2;
            startPt = new XYZ(bb.Min.X, midY, bb.Min.Z);
            endPt = new XYZ(bb.Max.X, midY, bb.Min.Z);
        }
        else if (dy > dx * 3)
        {
            // Wall runs along Y
            length = dy;
            thickness = dx;
            double midX = (bb.Min.X + bb.Max.X) / 2;
            startPt = new XYZ(midX, bb.Min.Y, bb.Min.Z);
            endPt = new XYZ(midX, bb.Max.Y, bb.Min.Z);
        }
        else
        {
            return null; // Not wall-like proportions
        }

        // Minimum wall proportions: length > 300mm, thickness 50-1000mm, height > 500mm
        double lengthMm = length * MmPerFoot;
        double thickMm = thickness * MmPerFoot;
        double heightMm = dz * MmPerFoot;
        if (lengthMm < 300 || thickMm < 50 || thickMm > 1000 || heightMm < 500)
            return null;

        return new WallProfile
        {
            StartPoint = startPt,
            EndPoint = endPt,
            Height = dz,
            Thickness = thickness,
            BaseElevation = bb.Min.Z,
        };
    }

    /// <summary>
    /// Try to extract a column-like profile: center point + height.
    /// </summary>
    public static ColumnProfile? ExtractColumnProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        double maxHorizontal = Math.Max(dx, dy);
        double minHorizontal = Math.Min(dx, dy);

        // Column: height >> horizontal dimensions, roughly square cross-section
        double heightMm = dz * MmPerFoot;
        double maxHorzMm = maxHorizontal * MmPerFoot;
        if (heightMm < 1000 || dz < maxHorizontal * 2)
            return null; // Not column-like

        return new ColumnProfile
        {
            CenterPoint = new XYZ(
                (bb.Min.X + bb.Max.X) / 2,
                (bb.Min.Y + bb.Max.Y) / 2,
                bb.Min.Z),
            Height = dz,
            BaseElevation = bb.Min.Z,
            CrossSectionWidth = dx,
            CrossSectionDepth = dy,
        };
    }

    /// <summary>
    /// Try to extract a beam-like profile: start point, end point, cross-section.
    /// A beam is horizontal with length >> cross-section dimensions.
    /// </summary>
    public static BeamProfile? ExtractBeamProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        // Find the longest horizontal dimension
        double maxHoriz = Math.Max(dx, dy);
        double minHoriz = Math.Min(dx, dy);

        // Beam: longest horizontal dim >> the other two dims
        if (maxHoriz < minHoriz * 2.5 || maxHoriz < dz * 2.5)
            return null;

        XYZ startPt, endPt;
        double midZ = (bb.Min.Z + bb.Max.Z) / 2;

        if (dx >= dy)
        {
            double midY = (bb.Min.Y + bb.Max.Y) / 2;
            startPt = new XYZ(bb.Min.X, midY, midZ);
            endPt = new XYZ(bb.Max.X, midY, midZ);
        }
        else
        {
            double midX = (bb.Min.X + bb.Max.X) / 2;
            startPt = new XYZ(midX, bb.Min.Y, midZ);
            endPt = new XYZ(midX, bb.Max.Y, midZ);
        }

        return new BeamProfile
        {
            StartPoint = startPt,
            EndPoint = endPt,
            Elevation = midZ,
            CrossSectionWidth = Math.Min(dx, dy),
            CrossSectionDepth = dz,
        };
    }

    /// <summary>
    /// Get all DirectShape elements in the document, optionally filtered by category.
    /// </summary>
    public static List<DirectShape> GetDirectShapes(Document doc, BuiltInCategory? category = null)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(DirectShape));
        if (category.HasValue)
            collector = collector.OfCategory(category.Value);
        return collector.Cast<DirectShape>().ToList();
    }

    /// <summary>
    /// Get a specific IFC parameter value from an element by name.
    /// IFC imports store IFC properties as instance parameters.
    /// </summary>
    public static string? GetIfcParameter(Element element, string paramName)
    {
        var param = element.LookupParameter(paramName);
        if (param == null || !param.HasValue) return null;
        return param.StorageType == StorageType.String
            ? param.AsString()
            : param.AsValueString();
    }

    // ── Profile data classes ──

    public class WallProfile
    {
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.Zero;
        public double Height { get; set; }
        public double Thickness { get; set; }
        public double BaseElevation { get; set; }
    }

    public class ColumnProfile
    {
        public XYZ CenterPoint { get; set; } = XYZ.Zero;
        public double Height { get; set; }
        public double BaseElevation { get; set; }
        public double CrossSectionWidth { get; set; }
        public double CrossSectionDepth { get; set; }
    }

    public class BeamProfile
    {
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.Zero;
        public double Elevation { get; set; }
        public double CrossSectionWidth { get; set; }
        public double CrossSectionDepth { get; set; }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcGeometryHelper.cs
git commit -m "feat(ifc): add IfcGeometryHelper for reconstruction pipeline"
```

---

## Task 2: Append Zod Schemas for Step 2 Tools

**Files:**
- Modify: `server/src/schemas/ifc.ts`

- [ ] **Step 1: Append these schemas to the end of `server/src/schemas/ifc.ts`**

```typescript
// ══════════════════════════════════════════════════════════════
// Step 2 — IFC Native Reconstruction
// ══════════════════════════════════════════════════════════════

// ── ifc_analyze_rebuildability ──
export const IfcAnalyzeRebuildabilityInput = z.object({
  categoryFilter: z
    .string()
    .optional()
    .describe(
      "OST category code to filter (e.g. 'OST_Walls'). Omit to analyze all IFC elements."
    ),
  maxElements: z
    .number()
    .optional()
    .default(200)
    .describe("Max elements to analyze. Default: 200"),
});

// ── ifc_list_rebuild_candidates ──
export const IfcListRebuildCandidatesInput = z.object({
  categoryFilter: z
    .string()
    .optional()
    .describe("OST category code to filter. Omit for all categories."),
  minConfidence: z
    .number()
    .optional()
    .default(0.5)
    .describe("Minimum rebuild confidence threshold (0.0-1.0). Default: 0.5"),
  maxElements: z
    .number()
    .optional()
    .default(100)
    .describe("Max candidates to return. Default: 100"),
});

// ── ifc_rebuild_walls ──
export const IfcRebuildWallsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all wall candidates."),
  wallTypeId: z
    .number()
    .optional()
    .describe("WallType element ID to use. Omit to use the closest matching type by thickness."),
  structural: z
    .boolean()
    .optional()
    .default(false)
    .describe("Mark rebuilt walls as structural. Default: false"),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_floors ──
export const IfcRebuildFloorsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all floor candidates."),
  floorTypeId: z
    .number()
    .optional()
    .describe("FloorType element ID to use. Omit to use the default floor type."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_roofs ──
export const IfcRebuildRoofsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all roof candidates."),
  roofTypeId: z
    .number()
    .optional()
    .describe("RoofType element ID to use. Omit to use the default roof type."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_structural_members ──
export const IfcRebuildStructuralMembersInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all structural candidates."),
  memberType: z
    .enum(["columns", "beams", "all"])
    .optional()
    .default("all")
    .describe("Which structural member type to rebuild. Default: all"),
  familySymbolId: z
    .number()
    .optional()
    .describe("FamilySymbol element ID to use. Omit to auto-select by cross-section."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_openings ──
export const IfcRebuildOpeningsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs representing openings. Omit to auto-detect."),
  hostElementIds: z
    .array(z.number())
    .optional()
    .describe("Host wall/floor element IDs to search for openings. Omit to search all rebuilt elements."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_family_instances ──
export const IfcRebuildFamilyInstancesInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild as family instances."),
  categoryFilter: z
    .enum(["OST_Doors", "OST_Windows", "OST_GenericModel"])
    .optional()
    .describe("Category to filter. Omit to rebuild doors and windows."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_compare_original_vs_rebuilt ──
export const IfcCompareOriginalVsRebuiltInput = z.object({
  originalElementId: z
    .number()
    .describe("Element ID of the original DirectShape (IFC import)"),
  rebuiltElementId: z
    .number()
    .describe("Element ID of the rebuilt native Revit element"),
});

// ── ifc_tag_unreconstructable_elements ──
export const IfcTagUnreconstructableElementsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific element IDs to tag. Omit to tag all elements that failed rebuild analysis."),
  tagValue: z
    .string()
    .optional()
    .default("IFC_UNRECONSTRUCTABLE")
    .describe("Value to set in the Comments parameter. Default: IFC_UNRECONSTRUCTABLE"),
});
```

- [ ] **Step 2: Verify TS build**

Run: `cd server && npm run build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add server/src/schemas/ifc.ts
git commit -m "feat(ifc): add Zod schemas for 10 IFC reconstruction tools"
```

---

## Task 3: ifc_analyze_rebuildability — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcAnalyzeRebuildabilityTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Scans imported IFC elements (DirectShapes), classifies each as rebuildable or not,
/// and returns confidence scores per element.
/// </summary>
public class IfcAnalyzeRebuildabilityTool : ICortexTool
{
    public string Name => "ifc_analyze_rebuildability";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Analyze IFC-imported elements for native Revit reconstruction feasibility";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var maxElements = input["maxElements"]?.Value<int>() ?? 200;

        BuiltInCategory? builtInCat = null;
        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            if (Enum.TryParse<BuiltInCategory>(categoryFilter, out var parsed))
                builtInCat = parsed;
        }

        var directShapes = IfcGeometryHelper.GetDirectShapes(doc!, builtInCat);
        if (directShapes.Count == 0)
            return CortexResult<object>.Ok(new
            {
                message = "No IFC DirectShape elements found",
                totalAnalyzed = 0,
                results = Array.Empty<object>(),
            });

        var results = new List<object>();
        var categorySummary = new Dictionary<string, int>();
        int rebuildableCount = 0;

        foreach (var ds in directShapes.Take(maxElements))
        {
            var categoryName = ds.Category?.Name ?? "Unknown";
            var geomType = IfcGeometryHelper.DetectGeometryType(ds);
            var ifcEntity = IfcGeometryHelper.GetIfcParameter(ds, "IfcExportAs")
                         ?? IfcGeometryHelper.GetIfcParameter(ds, "IfcType")
                         ?? "";

            var (strategy, confidence) = DetermineStrategy(ds, categoryName, geomType);

            if (confidence >= 0.5) rebuildableCount++;

            if (!categorySummary.ContainsKey(categoryName))
                categorySummary[categoryName] = 0;
            categorySummary[categoryName]++;

            results.Add(new
            {
                elementId = ToolHelpers.GetElementIdValue(ds.Id),
                name = ds.Name,
                category = categoryName,
                ifcEntity,
                geometryType = geomType,
                rebuildStrategy = strategy,
                rebuildConfidence = Math.Round(confidence, 2),
            });
        }

        // Store analysis results in session for use by other IFC tools
        session.Store.Set("ifc_analysis_results", results);

        return CortexResult<object>.Ok(new
        {
            totalAnalyzed = results.Count,
            totalInDocument = directShapes.Count,
            rebuildableCount,
            categorySummary,
            results,
        });
    }

    private static (string strategy, double confidence) DetermineStrategy(
        DirectShape ds, string category, string geomType)
    {
        // Mesh and unknown geometry cannot be rebuilt
        if (geomType == "mesh" || geomType == "unknown")
            return ("none", 0.0);

        // Match by category name patterns (works across locales via OST mapping)
        var catLower = category.ToLowerInvariant();

        if (catLower.Contains("wall") || catLower.Contains("mur"))
        {
            var profile = IfcGeometryHelper.ExtractWallProfile(ds);
            if (profile != null)
                return ("Wall.Create", geomType == "extrusion" ? 0.9 : 0.6);
            return ("Wall.Create (complex)", 0.3);
        }

        if (catLower.Contains("floor") || catLower.Contains("slab") ||
            catLower.Contains("paviment") || catLower.Contains("sol"))
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count > 0 && IfcGeometryHelper.ExtractBottomFootprint(solids[0]) != null)
                return ("Floor.Create", geomType == "extrusion" ? 0.85 : 0.5);
            return ("Floor.Create (complex)", 0.25);
        }

        if (catLower.Contains("roof") || catLower.Contains("tetto") || catLower.Contains("toit"))
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count > 0 && IfcGeometryHelper.ExtractBottomFootprint(solids[0]) != null)
                return ("NewFootPrintRoof", 0.7);
            return ("NewFootPrintRoof (complex)", 0.2);
        }

        if (catLower.Contains("column") || catLower.Contains("pilastr") || catLower.Contains("poteau"))
        {
            var profile = IfcGeometryHelper.ExtractColumnProfile(ds);
            return profile != null
                ? ("NewFamilyInstance.Column", 0.85)
                : ("NewFamilyInstance.Column (complex)", 0.3);
        }

        if (catLower.Contains("beam") || catLower.Contains("trave") ||
            catLower.Contains("telaio") || catLower.Contains("framing") || catLower.Contains("ossature"))
        {
            var profile = IfcGeometryHelper.ExtractBeamProfile(ds);
            return profile != null
                ? ("NewFamilyInstance.Beam", 0.85)
                : ("NewFamilyInstance.Beam (complex)", 0.3);
        }

        if (catLower.Contains("door") || catLower.Contains("port"))
            return ("FamilyInstance.Door", 0.6);

        if (catLower.Contains("window") || catLower.Contains("finestr") || catLower.Contains("fenetr"))
            return ("FamilyInstance.Window", 0.6);

        return ("none", 0.1);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcAnalyzeRebuildabilityTool.cs
git commit -m "feat(ifc): add ifc_analyze_rebuildability tool"
```

---

## Task 4: ifc_list_rebuild_candidates — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcListRebuildCandidatesTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Lists IFC-imported elements that pass the rebuildability confidence threshold.
/// Works best after ifc_analyze_rebuildability has been called (uses cached results).
/// </summary>
public class IfcListRebuildCandidatesTool : ICortexTool
{
    public string Name => "ifc_list_rebuild_candidates";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List IFC elements that can be rebuilt as native Revit elements";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var minConfidence = input["minConfidence"]?.Value<double>() ?? 0.5;
        var maxElements = input["maxElements"]?.Value<int>() ?? 100;

        // Try cached results from ifc_analyze_rebuildability
        var cached = session.Store.Get<List<object>>("ifc_analysis_results");

        if (cached != null && cached.Count > 0)
        {
            // Filter cached results
            var filtered = cached
                .Cast<dynamic>()
                .Where(r => (double)r.rebuildConfidence >= minConfidence)
                .Where(r => string.IsNullOrWhiteSpace(categoryFilter) ||
                            ((string)r.category).Contains(categoryFilter!, StringComparison.OrdinalIgnoreCase))
                .Take(maxElements)
                .Select(r => new
                {
                    elementId = (long)r.elementId,
                    name = (string)r.name,
                    category = (string)r.category,
                    rebuildStrategy = (string)r.rebuildStrategy,
                    rebuildConfidence = (double)r.rebuildConfidence,
                })
                .ToList();

            return CortexResult<object>.Ok(new
            {
                count = filtered.Count,
                minConfidence,
                source = "cached_analysis",
                candidates = filtered,
            });
        }

        // No cached results — run a fresh lightweight analysis
        BuiltInCategory? builtInCat = null;
        if (!string.IsNullOrWhiteSpace(categoryFilter) &&
            Enum.TryParse<BuiltInCategory>(categoryFilter, out var parsed))
            builtInCat = parsed;

        var directShapes = IfcGeometryHelper.GetDirectShapes(doc!, builtInCat);
        var candidates = new List<object>();

        foreach (var ds in directShapes.Take(maxElements * 2)) // scan more to find enough
        {
            var catName = ds.Category?.Name ?? "Unknown";
            var geomType = IfcGeometryHelper.DetectGeometryType(ds);

            double confidence = EstimateConfidence(ds, catName, geomType);
            if (confidence < minConfidence) continue;

            candidates.Add(new
            {
                elementId = ToolHelpers.GetElementIdValue(ds.Id),
                name = ds.Name,
                category = catName,
                geometryType = geomType,
                rebuildConfidence = Math.Round(confidence, 2),
            });

            if (candidates.Count >= maxElements) break;
        }

        return CortexResult<object>.Ok(new
        {
            count = candidates.Count,
            minConfidence,
            source = "fresh_scan",
            candidates,
        });
    }

    private static double EstimateConfidence(DirectShape ds, string category, string geomType)
    {
        if (geomType == "mesh" || geomType == "unknown") return 0.0;

        var catLower = category.ToLowerInvariant();
        double baseScore = geomType == "extrusion" ? 0.8 : (geomType == "sweep" ? 0.5 : 0.3);

        if (catLower.Contains("wall") || catLower.Contains("mur"))
            return IfcGeometryHelper.ExtractWallProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("floor") || catLower.Contains("slab") || catLower.Contains("paviment"))
            return baseScore;
        if (catLower.Contains("column") || catLower.Contains("pilastr"))
            return IfcGeometryHelper.ExtractColumnProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("beam") || catLower.Contains("trave") || catLower.Contains("framing"))
            return IfcGeometryHelper.ExtractBeamProfile(ds) != null ? baseScore + 0.1 : baseScore - 0.2;
        if (catLower.Contains("roof") || catLower.Contains("tetto"))
            return baseScore - 0.1;
        if (catLower.Contains("door") || catLower.Contains("window"))
            return 0.6;

        return 0.1;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcListRebuildCandidatesTool.cs
git commit -m "feat(ifc): add ifc_list_rebuild_candidates tool"
```

---

## Task 5: ifc_rebuild_walls — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildWallsTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reconstructs walls from IFC-imported DirectShape elements using Wall.Create.
/// Extracts wall profile (base line + height + thickness) and finds matching WallType.
/// </summary>
public class IfcRebuildWallsTool : ICortexTool
{
    public string Name => "ifc_rebuild_walls";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit walls from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var wallTypeIdRaw = input["wallTypeId"]?.Value<long>();
        var structural = input["structural"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // Get candidates
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
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Walls);
        }

        // Find wall type
        WallType? wallType = null;
        if (wallTypeIdRaw.HasValue)
        {
            wallType = doc!.GetElement(ToolHelpers.ToElementId(wallTypeIdRaw.Value)) as WallType;
            if (wallType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"WallType {wallTypeIdRaw.Value} not found");
        }

        var allWallTypes = new FilteredElementCollector(doc!)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .ToList();

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        foreach (var ds in candidates)
        {
            var profile = IfcGeometryHelper.ExtractWallProfile(ds);
            if (profile == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "Could not extract wall profile",
                });
                continue;
            }

            // Find matching wall type by thickness
            var useWallType = wallType ?? FindClosestWallType(allWallTypes, profile.Thickness);
            if (useWallType == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No matching WallType found for thickness " +
                             Math.Round(profile.Thickness * MmPerFoot, 0) + "mm",
                });
                continue;
            }

            var level = IfcGeometryHelper.FindNearestLevel(doc!, profile.BaseElevation);
            if (level == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No level found",
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
                    status = "would_rebuild",
                    wallTypeName = useWallType.Name,
                    levelName = level.Name,
                    lengthMm = Math.Round(profile.StartPoint.DistanceTo(profile.EndPoint) * MmPerFoot, 0),
                    heightMm = Math.Round(profile.Height * MmPerFoot, 0),
                    thicknessMm = Math.Round(profile.Thickness * MmPerFoot, 0),
                });
                continue;
            }

            // Actually rebuild
            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Wall");
                tx.Start();

                var baseLine = Line.CreateBound(profile.StartPoint, profile.EndPoint);
                var offset = profile.BaseElevation - level.Elevation;

                var newWall = Wall.Create(doc!, baseLine, useWallType.Id, level.Id,
                    profile.Height, offset, false, structural);

                tx.Commit();

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newWall.Id),
                    wallTypeName = useWallType.Name,
                    levelName = level.Name,
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

        return CortexResult<object>.Ok(new
        {
            dryRun,
            totalCandidates = candidates.Count,
            rebuilt,
            skipped,
            results,
        });
    }

    private static WallType? FindClosestWallType(List<WallType> types, double thicknessFeet)
    {
        WallType? best = null;
        double bestDelta = double.MaxValue;

        foreach (var wt in types)
        {
            try
            {
                var width = wt.Width; // in feet
                var delta = Math.Abs(width - thicknessFeet);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = wt;
                }
            }
            catch { /* some types may not have Width */ }
        }

        // Only accept if within 50mm tolerance
        return bestDelta * MmPerFoot < 50 ? best : null;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildWallsTool.cs
git commit -m "feat(ifc): add ifc_rebuild_walls tool"
```

---

## Task 6: ifc_rebuild_floors — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildFloorsTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reconstructs floors from IFC-imported DirectShape elements using Floor.Create.
/// Extracts the bottom face footprint as a CurveLoop for the floor profile.
/// </summary>
public class IfcRebuildFloorsTool : ICortexTool
{
    public string Name => "ifc_rebuild_floors";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit floors from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var floorTypeIdRaw = input["floorTypeId"]?.Value<long>();
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
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Floors);
        }

        FloorType? floorType = null;
        if (floorTypeIdRaw.HasValue)
        {
            floorType = doc!.GetElement(ToolHelpers.ToElementId(floorTypeIdRaw.Value)) as FloorType;
            if (floorType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"FloorType {floorTypeIdRaw.Value} not found");
        }
        else
        {
            // Use the first available floor type
            floorType = new FilteredElementCollector(doc!)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
        }

        if (floorType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No FloorType available in the document");

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        foreach (var ds in candidates)
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count == 0)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No solid geometry found",
                });
                continue;
            }

            var footprint = IfcGeometryHelper.ExtractBottomFootprint(solids[0]);
            if (footprint == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "Could not extract floor footprint",
                });
                continue;
            }

            var bb = ds.get_BoundingBox(null);
            var level = IfcGeometryHelper.FindNearestLevel(doc!, bb?.Min.Z ?? 0);
            if (level == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No level found",
                });
                continue;
            }

            if (dryRun)
            {
                rebuilt++;
                var areaSqM = footprint.GetExactLength() > 0
                    ? Math.Round(ComputeLoopArea(footprint) * MmPerFoot * MmPerFoot / 1e6, 2)
                    : 0;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "would_rebuild",
                    floorTypeName = floorType.Name,
                    levelName = level.Name,
                    areaSqM,
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Floor");
                tx.Start();

                var curveLoops = new List<CurveLoop> { footprint };
                var newFloor = Floor.Create(doc!, curveLoops, floorType.Id, level.Id);

                tx.Commit();

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newFloor.Id),
                    floorTypeName = floorType.Name,
                    levelName = level.Name,
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

        return CortexResult<object>.Ok(new
        {
            dryRun,
            totalCandidates = candidates.Count,
            rebuilt,
            skipped,
            results,
        });
    }

    private static double ComputeLoopArea(CurveLoop loop)
    {
        // Approximate area using shoelace formula on projected XY points
        var points = new List<XYZ>();
        foreach (var curve in loop)
        {
            points.Add(curve.GetEndPoint(0));
        }
        if (points.Count < 3) return 0;

        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }
        return Math.Abs(area) / 2.0;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildFloorsTool.cs
git commit -m "feat(ifc): add ifc_rebuild_floors tool"
```

---

## Task 7: ifc_rebuild_roofs — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildRoofsTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reconstructs roofs from IFC-imported DirectShape elements using NewFootPrintRoof.
/// Extracts the bottom face footprint for the roof profile.
/// </summary>
public class IfcRebuildRoofsTool : ICortexTool
{
    public string Name => "ifc_rebuild_roofs";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit roofs from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var roofTypeIdRaw = input["roofTypeId"]?.Value<long>();
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
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Roofs);
        }

        RoofType? roofType = null;
        if (roofTypeIdRaw.HasValue)
        {
            roofType = doc!.GetElement(ToolHelpers.ToElementId(roofTypeIdRaw.Value)) as RoofType;
            if (roofType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"RoofType {roofTypeIdRaw.Value} not found");
        }
        else
        {
            roofType = new FilteredElementCollector(doc!)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault();
        }

        if (roofType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No RoofType available in the document");

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        foreach (var ds in candidates)
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count == 0)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No solid geometry found",
                });
                continue;
            }

            var footprint = IfcGeometryHelper.ExtractBottomFootprint(solids[0]);
            if (footprint == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "Could not extract roof footprint",
                });
                continue;
            }

            var bb = ds.get_BoundingBox(null);
            var level = IfcGeometryHelper.FindNearestLevel(doc!, bb?.Min.Z ?? 0);
            if (level == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No level found",
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
                    status = "would_rebuild",
                    roofTypeName = roofType.Name,
                    levelName = level.Name,
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Roof");
                tx.Start();

                // Convert CurveLoop to CurveArray for NewFootPrintRoof
                var curveArray = new CurveArray();
                foreach (var curve in footprint)
                    curveArray.Append(curve);

                var newRoof = doc!.Create.NewFootPrintRoof(
                    curveArray, level, roofType, out _);

                tx.Commit();

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newRoof?.Id),
                    roofTypeName = roofType.Name,
                    levelName = level.Name,
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

        return CortexResult<object>.Ok(new
        {
            dryRun,
            totalCandidates = candidates.Count,
            rebuilt,
            skipped,
            results,
        });
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildRoofsTool.cs
git commit -m "feat(ifc): add ifc_rebuild_roofs tool"
```

---

## Task 8: ifc_rebuild_structural_members — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildStructuralMembersTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
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
                if (level == null) { skipped++; continue; }

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
                    var symbol = userSymbol ?? FindColumnSymbol(doc!, profile);
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

                    if (!symbol.IsActive) symbol.Activate();

                    using var tx = new Transaction(doc!, "RevitCortex: Rebuild Column");
                    tx.Start();
                    var inst = doc!.Create.NewFamilyInstance(
                        profile.CenterPoint, symbol, level, StructuralType.Column);
                    tx.Commit();

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
                if (level == null) { skipped++; continue; }

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
                    var symbol = userSymbol ?? FindBeamSymbol(doc!, profile);
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

                    if (!symbol.IsActive) symbol.Activate();

                    using var tx = new Transaction(doc!, "RevitCortex: Rebuild Beam");
                    tx.Start();
                    var line = Line.CreateBound(profile.StartPoint, profile.EndPoint);
                    var inst = doc!.Create.NewFamilyInstance(
                        line, symbol, level, StructuralType.Beam);
                    tx.Commit();

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

        return CortexResult<object>.Ok(new { dryRun, totalCandidates = candidates.Count, rebuilt, skipped, results });
    }

    private static FamilySymbol? FindColumnSymbol(Document doc, IfcGeometryHelper.ColumnProfile profile)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }

    private static FamilySymbol? FindBeamSymbol(Document doc, IfcGeometryHelper.BeamProfile profile)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildStructuralMembersTool.cs
git commit -m "feat(ifc): add ifc_rebuild_structural_members tool"
```

---

## Task 9: ifc_rebuild_openings — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildOpeningsTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Cuts openings in rebuilt walls and floors based on IFC opening elements.
/// Uses Document.Create.NewOpening for rectangular wall openings and
/// curve-based floor/roof openings.
/// </summary>
public class IfcRebuildOpeningsTool : ICortexTool
{
    public string Name => "ifc_rebuild_openings";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Cut openings in rebuilt walls/floors based on IFC opening elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var hostElementIds = input["hostElementIds"]?.ToObject<long[]>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // Find opening DirectShapes
        List<DirectShape> openingCandidates;
        if (elementIds != null && elementIds.Length > 0)
        {
            openingCandidates = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            // IFC openings are typically in GenericModel or a void category
            openingCandidates = IfcGeometryHelper.GetDirectShapes(doc!)
                .Where(ds =>
                {
                    var ifcType = IfcGeometryHelper.GetIfcParameter(ds, "IfcExportAs")
                               ?? IfcGeometryHelper.GetIfcParameter(ds, "IfcType") ?? "";
                    return ifcType.Contains("Opening", StringComparison.OrdinalIgnoreCase)
                        || ifcType.Contains("IfcOpeningElement", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        // Find host elements (rebuilt walls/floors)
        List<Element> hosts;
        if (hostElementIds != null && hostElementIds.Length > 0)
        {
            hosts = hostElementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)))
                .Where(e => e != null)
                .ToList()!;
        }
        else
        {
            var walls = new FilteredElementCollector(doc!)
                .OfClass(typeof(Wall)).Cast<Element>();
            var floors = new FilteredElementCollector(doc!)
                .OfClass(typeof(Floor)).Cast<Element>();
            hosts = walls.Concat(floors).ToList();
        }

        var results = new List<object>();
        int created = 0, skipped = 0;

        foreach (var openingDs in openingCandidates)
        {
            var bb = openingDs.get_BoundingBox(null);
            if (bb == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "skipped",
                    reason = "No bounding box",
                });
                continue;
            }

            // Find the host that contains this opening (bounding box overlap)
            var hostElement = FindContainingHost(hosts, bb);
            if (hostElement == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "skipped",
                    reason = "No matching host element found",
                });
                continue;
            }

            if (dryRun)
            {
                created++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    hostElementId = ToolHelpers.GetElementIdValue(hostElement.Id),
                    hostType = hostElement is Wall ? "wall" : "floor",
                    status = "would_create",
                    widthMm = Math.Round((bb.Max.X - bb.Min.X) * MmPerFoot, 0),
                    heightMm = Math.Round((bb.Max.Z - bb.Min.Z) * MmPerFoot, 0),
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Create Opening");
                tx.Start();

                if (hostElement is Wall wall)
                {
                    doc!.Create.NewOpening(wall, bb.Min, bb.Max);
                }
                else
                {
                    // Floor/roof opening using curve profile
                    var curveArray = new CurveArray();
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                        new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                        new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                        new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                        new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)));

                    doc!.Create.NewOpening(hostElement, curveArray, true);
                }

                tx.Commit();

                created++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    hostElementId = ToolHelpers.GetElementIdValue(hostElement.Id),
                    status = "created",
                });
            }
            catch (Exception ex)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        return CortexResult<object>.Ok(new { dryRun, totalCandidates = openingCandidates.Count, created, skipped, results });
    }

    private static Element? FindContainingHost(List<Element> hosts, BoundingBoxXYZ openingBb)
    {
        var openingCenter = new XYZ(
            (openingBb.Min.X + openingBb.Max.X) / 2,
            (openingBb.Min.Y + openingBb.Max.Y) / 2,
            (openingBb.Min.Z + openingBb.Max.Z) / 2);

        foreach (var host in hosts)
        {
            var hostBb = host.get_BoundingBox(null);
            if (hostBb == null) continue;

            // Check if opening center is within the host bounding box (with tolerance)
            double tol = 0.5; // ~150mm tolerance
            if (openingCenter.X >= hostBb.Min.X - tol && openingCenter.X <= hostBb.Max.X + tol &&
                openingCenter.Y >= hostBb.Min.Y - tol && openingCenter.Y <= hostBb.Max.Y + tol &&
                openingCenter.Z >= hostBb.Min.Z - tol && openingCenter.Z <= hostBb.Max.Z + tol)
            {
                return host;
            }
        }
        return null;
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildOpeningsTool.cs
git commit -m "feat(ifc): add ifc_rebuild_openings tool"
```

---

## Task 10: ifc_rebuild_family_instances — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcRebuildFamilyInstancesTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
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
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcRebuildFamilyInstancesTool.cs
git commit -m "feat(ifc): add ifc_rebuild_family_instances tool"
```

---

## Task 11: ifc_compare_original_vs_rebuilt — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcCompareOriginalVsRebuiltTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Compares an original IFC DirectShape with its rebuilt native Revit element.
/// Reports volume difference, bounding box overlap, and geometric similarity.
/// </summary>
public class IfcCompareOriginalVsRebuiltTool : ICortexTool
{
    public string Name => "ifc_compare_original_vs_rebuilt";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Compare original IFC element with its rebuilt native Revit counterpart";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var originalId = input["originalElementId"]?.Value<long>() ?? 0;
        var rebuiltId = input["rebuiltElementId"]?.Value<long>() ?? 0;

        if (originalId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "originalElementId is required");
        if (rebuiltId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "rebuiltElementId is required");

        var original = doc!.GetElement(ToolHelpers.ToElementId(originalId));
        var rebuilt = doc!.GetElement(ToolHelpers.ToElementId(rebuiltId));

        if (original == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Original element {originalId} not found");
        if (rebuilt == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Rebuilt element {rebuiltId} not found");

        var origVolume = IfcGeometryHelper.GetVolumeCubicMeters(original);
        var rebuVolume = IfcGeometryHelper.GetVolumeCubicMeters(rebuilt);

        var origBb = original.get_BoundingBox(null);
        var rebuBb = rebuilt.get_BoundingBox(null);

        double volumeDiffPercent = origVolume > 0.0001
            ? Math.Round((rebuVolume - origVolume) / origVolume * 100, 2)
            : 0;

        double bbOverlap = 0;
        if (origBb != null && rebuBb != null)
            bbOverlap = Math.Round(ComputeBbOverlap(origBb, rebuBb) * 100, 1);

        var qualityScore = ComputeQualityScore(volumeDiffPercent, bbOverlap);

        return CortexResult<object>.Ok(new
        {
            original = new
            {
                elementId = originalId,
                category = original.Category?.Name ?? "Unknown",
                volumeM3 = Math.Round(origVolume, 4),
                boundingBox = origBb != null ? FormatBb(origBb) : null,
            },
            rebuilt = new
            {
                elementId = rebuiltId,
                category = rebuilt.Category?.Name ?? "Unknown",
                volumeM3 = Math.Round(rebuVolume, 4),
                boundingBox = rebuBb != null ? FormatBb(rebuBb) : null,
            },
            comparison = new
            {
                volumeDifferencePercent = volumeDiffPercent,
                boundingBoxOverlapPercent = bbOverlap,
                qualityScore,
                qualityRating = qualityScore >= 90 ? "excellent"
                    : qualityScore >= 70 ? "good"
                    : qualityScore >= 50 ? "fair"
                    : "poor",
            },
        });
    }

    private static double ComputeBbOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        double overlapX = Math.Max(0, Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X));
        double overlapY = Math.Max(0, Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y));
        double overlapZ = Math.Max(0, Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z));
        double overlapVol = overlapX * overlapY * overlapZ;

        double aVol = (a.Max.X - a.Min.X) * (a.Max.Y - a.Min.Y) * (a.Max.Z - a.Min.Z);
        double bVol = (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y) * (b.Max.Z - b.Min.Z);
        double unionVol = aVol + bVol - overlapVol;

        return unionVol > 0 ? overlapVol / unionVol : 0;
    }

    private static double ComputeQualityScore(double volumeDiffPercent, double bbOverlap)
    {
        // Score from 0-100
        double volScore = Math.Max(0, 100 - Math.Abs(volumeDiffPercent) * 2);
        double bbScore = bbOverlap;
        return Math.Round((volScore + bbScore) / 2, 1);
    }

    private static object FormatBb(BoundingBoxXYZ bb)
    {
        return new
        {
            minMm = new { x = Math.Round(bb.Min.X * MmPerFoot, 0), y = Math.Round(bb.Min.Y * MmPerFoot, 0), z = Math.Round(bb.Min.Z * MmPerFoot, 0) },
            maxMm = new { x = Math.Round(bb.Max.X * MmPerFoot, 0), y = Math.Round(bb.Max.Y * MmPerFoot, 0), z = Math.Round(bb.Max.Z * MmPerFoot, 0) },
        };
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcCompareOriginalVsRebuiltTool.cs
git commit -m "feat(ifc): add ifc_compare_original_vs_rebuilt tool"
```

---

## Task 12: ifc_tag_unreconstructable_elements — C# Tool

**Files:**
- Create: `src/RevitCortex.Tools/IFC/IfcTagUnreconstructableElementsTool.cs`

- [ ] **Step 1: Create the tool**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Tags IFC-imported elements that cannot be rebuilt as native Revit elements.
/// Sets a value in the Comments parameter to mark them for manual review.
/// </summary>
public class IfcTagUnreconstructableElementsTool : ICortexTool
{
    public string Name => "ifc_tag_unreconstructable_elements";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tag IFC elements that cannot be rebuilt, marking them for manual review";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var tagValue = input["tagValue"]?.Value<string>() ?? "IFC_UNRECONSTRUCTABLE";

        List<DirectShape> targets;
        if (elementIds != null && elementIds.Length > 0)
        {
            targets = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            // Tag all DirectShapes that were marked as low-confidence in analysis
            var cached = session.Store.Get<List<object>>("ifc_analysis_results");
            if (cached == null || cached.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No analysis results in session. Run ifc_analyze_rebuildability first, or provide elementIds.",
                    suggestion: "Call ifc_analyze_rebuildability first, then call this tool");

            var lowConfidenceIds = cached
                .Cast<dynamic>()
                .Where(r => (double)r.rebuildConfidence < 0.5)
                .Select(r => (long)r.elementId)
                .ToList();

            targets = lowConfidenceIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }

        if (targets.Count == 0)
            return CortexResult<object>.Ok(new
            {
                tagged = 0,
                message = "No elements to tag",
            });

        if (!session.RequestConfirmation("tag unreconstructable elements", targets.Count,
            $"Set Comments to '{tagValue}' on {targets.Count} elements"))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        int tagged = 0;
        var results = new List<object>();

        using var tx = new Transaction(doc!, "RevitCortex: Tag Unreconstructable");
        tx.Start();

        foreach (var ds in targets)
        {
            try
            {
                var commentsParam = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentsParam != null && !commentsParam.IsReadOnly)
                {
                    commentsParam.Set(tagValue);
                    tagged++;
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "tagged",
                    });
                }
                else
                {
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "skipped",
                        reason = "Comments parameter not writable",
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    elementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            tagged,
            tagValue,
            total = targets.Count,
            results,
        });
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

```bash
git add src/RevitCortex.Tools/IFC/IfcTagUnreconstructableElementsTool.cs
git commit -m "feat(ifc): add ifc_tag_unreconstructable_elements tool"
```

---

## Task 13: TypeScript Tool Registrations (10 tools)

**Files:**
- Create: 10 TS files in `server/src/tools/`
- Modify: `server/src/tools/register.ts`

- [ ] **Step 1: Create all 10 TS tool files**

Each follows the exact same pattern. All import from `"../schemas/ifc.js"`.

| File | Function | Tool name | Schema | Description |
|------|----------|-----------|--------|-------------|
| `ifc_analyze_rebuildability.ts` | `registerIfcAnalyzeRebuildabilityTool` | `ifc_analyze_rebuildability` | `IfcAnalyzeRebuildabilityInput` | `"Analyze IFC-imported elements for native Revit reconstruction feasibility."` |
| `ifc_list_rebuild_candidates.ts` | `registerIfcListRebuildCandidatesTool` | `ifc_list_rebuild_candidates` | `IfcListRebuildCandidatesInput` | `"List IFC elements that can be rebuilt as native Revit elements above a confidence threshold."` |
| `ifc_rebuild_walls.ts` | `registerIfcRebuildWallsTool` | `ifc_rebuild_walls` | `IfcRebuildWallsInput` | `"Rebuild native Revit walls from IFC-imported DirectShape elements."` |
| `ifc_rebuild_floors.ts` | `registerIfcRebuildFloorsTool` | `ifc_rebuild_floors` | `IfcRebuildFloorsInput` | `"Rebuild native Revit floors from IFC-imported DirectShape elements."` |
| `ifc_rebuild_roofs.ts` | `registerIfcRebuildRoofsTool` | `ifc_rebuild_roofs` | `IfcRebuildRoofsInput` | `"Rebuild native Revit roofs from IFC-imported DirectShape elements."` |
| `ifc_rebuild_structural_members.ts` | `registerIfcRebuildStructuralMembersTool` | `ifc_rebuild_structural_members` | `IfcRebuildStructuralMembersInput` | `"Rebuild native Revit columns and beams from IFC-imported DirectShape elements."` |
| `ifc_rebuild_openings.ts` | `registerIfcRebuildOpeningsTool` | `ifc_rebuild_openings` | `IfcRebuildOpeningsInput` | `"Cut openings in rebuilt walls/floors based on IFC opening elements."` |
| `ifc_rebuild_family_instances.ts` | `registerIfcRebuildFamilyInstancesTool` | `ifc_rebuild_family_instances` | `IfcRebuildFamilyInstancesInput` | `"Rebuild doors, windows, and other family instances from IFC DirectShapes."` |
| `ifc_compare_original_vs_rebuilt.ts` | `registerIfcCompareOriginalVsRebuiltTool` | `ifc_compare_original_vs_rebuilt` | `IfcCompareOriginalVsRebuiltInput` | `"Compare original IFC element with its rebuilt native Revit counterpart."` |
| `ifc_tag_unreconstructable_elements.ts` | `registerIfcTagUnreconstructableElementsTool` | `ifc_tag_unreconstructable_elements` | `IfcTagUnreconstructableElementsInput` | `"Tag IFC elements that cannot be rebuilt, marking them for manual review."` |

Each file follows this template (replace placeholders):

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { {Schema} } from "../schemas/ifc.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function {registerFn}(server: McpServer): void {
  server.tool(
    "{toolName}",
    "{description}",
    {Schema}.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("{toolName}", args);
        });
        return toolResponse("{toolName}", result, Date.now() - start, args);
      } catch (error) {
        return toolError("{toolName}", error, Date.now() - start);
      }
    }
  );
}
```

- [ ] **Step 2: Add 10 imports to register.ts**

```typescript
import { registerIfcAnalyzeRebuildabilityTool } from "./ifc_analyze_rebuildability.js";
import { registerIfcListRebuildCandidatesTool } from "./ifc_list_rebuild_candidates.js";
import { registerIfcRebuildWallsTool } from "./ifc_rebuild_walls.js";
import { registerIfcRebuildFloorsTool } from "./ifc_rebuild_floors.js";
import { registerIfcRebuildRoofsTool } from "./ifc_rebuild_roofs.js";
import { registerIfcRebuildStructuralMembersTool } from "./ifc_rebuild_structural_members.js";
import { registerIfcRebuildOpeningsTool } from "./ifc_rebuild_openings.js";
import { registerIfcRebuildFamilyInstancesTool } from "./ifc_rebuild_family_instances.js";
import { registerIfcCompareOriginalVsRebuiltTool } from "./ifc_compare_original_vs_rebuilt.js";
import { registerIfcTagUnreconstructableElementsTool } from "./ifc_tag_unreconstructable_elements.js";
```

- [ ] **Step 3: Add 10 entries to toolRegistrations array**

```typescript
  { name: "ifc_analyze_rebuildability", register: registerIfcAnalyzeRebuildabilityTool },
  { name: "ifc_list_rebuild_candidates", register: registerIfcListRebuildCandidatesTool },
  { name: "ifc_rebuild_walls", register: registerIfcRebuildWallsTool },
  { name: "ifc_rebuild_floors", register: registerIfcRebuildFloorsTool },
  { name: "ifc_rebuild_roofs", register: registerIfcRebuildRoofsTool },
  { name: "ifc_rebuild_structural_members", register: registerIfcRebuildStructuralMembersTool },
  { name: "ifc_rebuild_openings", register: registerIfcRebuildOpeningsTool },
  { name: "ifc_rebuild_family_instances", register: registerIfcRebuildFamilyInstancesTool },
  { name: "ifc_compare_original_vs_rebuilt", register: registerIfcCompareOriginalVsRebuiltTool },
  { name: "ifc_tag_unreconstructable_elements", register: registerIfcTagUnreconstructableElementsTool },
```

- [ ] **Step 4: Build TS server**

Run: `cd server && npm run build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add server/src/tools/ifc_analyze_rebuildability.ts server/src/tools/ifc_list_rebuild_candidates.ts server/src/tools/ifc_rebuild_walls.ts server/src/tools/ifc_rebuild_floors.ts server/src/tools/ifc_rebuild_roofs.ts server/src/tools/ifc_rebuild_structural_members.ts server/src/tools/ifc_rebuild_openings.ts server/src/tools/ifc_rebuild_family_instances.ts server/src/tools/ifc_compare_original_vs_rebuilt.ts server/src/tools/ifc_tag_unreconstructable_elements.ts server/src/tools/register.ts
git commit -m "feat(ifc): add TS registrations for 10 IFC reconstruction tools"
```

---

## Task 14: Update Tests and Read-Only Whitelist

**Files:**
- Modify: `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs`
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs`

- [ ] **Step 1: Update expected tool count**

In `ToolRegistrationTests.cs`, change:
```csharp
Assert.True(AllToolTypes.Count >= 123,
```
To:
```csharp
Assert.True(AllToolTypes.Count >= 134,
```
And update the message string to `134`.

(133 = 123 existing + 10 new + the IfcGeometryHelper which is not a tool. Actually 11 new .cs files but IfcGeometryHelper is not ICortexTool. So 123 + 10 = 133. But we also have the tool count verification. Let's count: 10 new C# tools implementing ICortexTool = 133 total.)

Actually: change to `>= 133` and message `133`.

- [ ] **Step 2: Add read-only prefixes for IFC Step 2 tools**

In `CortexRouter.cs`, add to the `ReadOnlyPrefixes` array:
- `"ifc_analyze_"` — for `ifc_analyze_rebuildability`
- `"ifc_compare_"` — for `ifc_compare_original_vs_rebuilt`

The existing `ifc_list_` prefix already covers `ifc_list_rebuild_candidates`.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Run: `dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs src/RevitCortex.Plugin/CortexRouter.cs
git commit -m "test: update tool count and read-only whitelist for IFC Step 2"
```

---

## Task 15: Full Build & Test Verification

- [ ] **Step 1: Build C# for R25**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Build TypeScript server**

Run: `cd server && npm run build`
Expected: Build succeeded

- [ ] **Step 3: Run all tests**

Run: `dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj`
Expected: All tests pass

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "feat(ifc): complete IFC native reconstruction — 10 tools implemented (Step 2)"
```

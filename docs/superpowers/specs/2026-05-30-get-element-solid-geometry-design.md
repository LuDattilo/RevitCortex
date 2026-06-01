# Design: `get_element_solid_geometry`

**Date:** 2026-05-30
**Branch:** `feat/rebar-api` (active line; tool was discovered as a gap during the rebar live test)
**Status:** Approved for implementation

## Problem

No RevitCortex tool exposes an element's **real solid geometry** extents. Callers fall back
to `element.get_BoundingBox(null)`, which returns the *element* bounding box — for beams that
have been cut/joined by columns and slabs, this is larger and shifted relative to the armable
solid. During the 2026-05-30 rebar live test on Snowdon Towers, a CB24x24 beam had an element
BBox 610 mm tall but its real solid was only 356 mm tall; stirrups built from the element BBox
landed in empty space → Revit error "Rebar is placed completely outside of its host."

The only current workaround is a read-only `send_code_to_revit`, which is fragile (DLL
conflicts) and bypasses the native tool layer. See memory `feedback_rebar_use_real_solid_not_bbox`.

## Goal

A read-only tool that returns, in mm and model coordinates, the bounding box / centroid /
volume / face+edge counts of each real solid of an element (and an aggregate), so rebar and
other elements can be positioned inside the host's physical body.

## Component 1 — Tool class

`src/RevitCortex.Tools/Elements/GetElementSolidGeometryTool.cs`

- `ICortexTool`: Name `get_element_solid_geometry`, Category `Elements`,
  `RequiresDocument = true`, `IsDynamic = false`. Read-only (`get_` prefix → router treats as read-only).
- **Input:** `elementId` (long, required, > 0), `maxSolids` (int, default 20, clamped to >= 1).
- **Logic:**
  1. Resolve doc from `session.Store.Get<object>("activeDocument")`. Null → `InvalidInput`.
  2. `elementId <= 0` → `InvalidInput`.
  3. `doc.GetElement(ToElementId(elementId))`. Null → `ElementNotFound`.
  4. `element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })`.
  5. Collect `Solid`s with `Volume > 1e-6`, unwrapping `GeometryInstance.GetInstanceGeometry()`
     (same idiom as `IfcGeometryHelper.GetSolids`, threshold `1e-6` per the task).
  6. Empty geometry / no qualifying solids → `Ok` with `solidCount: 0` and an explanatory
     `message` (annotation / line-based / empty element — **not** an error).
- **Per solid** (only the first `maxSolids` materialized into the `solids` array):
  - `bb = solid.GetBoundingBox()`; min/max in **model coords** via
    `bb.Transform.OfPoint(bb.Min)` / `bb.Transform.OfPoint(bb.Max)`, then × 304.8 mm.
    (Note: `GetBoundingBox()` is in the solid's local space; the transform maps to model space.)
  - `centroid` = `solid.ComputeCentroid()` × 304.8 (already model space).
  - `volume` m³ = `solid.Volume × 0.0283168`.
  - `faceCount` = `solid.Faces.Size`; `edgeCount` = `solid.Edges.Size`.
- **Aggregate** (over **all** qualifying solids, not just the capped subset):
  - `boundingBox.min`/`max` = component-wise union of every solid's model-space bbox corners (mm).
  - `volume` m³ = sum of all solid volumes.
  - `solidCount` (total qualifying), `solidsReturned` (== min(count, maxSolids)),
    `faceCount`/`edgeCount` totals. Truncation is surfaced, never silent
    (per `feedback_no_silent_skip_in_pbi_export` principle).
- **Cross-target:** `ToElementId` with `#if REVIT2024_OR_GREATER` (established idiom). No
  `record`/`init`/range features. Builds on net48 (R24) and net8 (R25).

## Component 2 — Server wrapper

`src/RevitCortex.Server/Tools/ElementTools.cs`

```csharp
[McpServerTool(Name = "get_element_solid_geometry"),
 Description("Get an element's REAL solid geometry extents (bounding box, centroid, volume, face/edge counts) in mm and model coordinates. Unlike the element bounding box, this reflects the actual solid after joins/cuts — essential for placing rebar/elements inside hosts cut by columns or slabs.")]
public static async Task<string> GetElementSolidGeometry(
    RevitConnectionManager revit,
    [Description("Revit element ID to inspect")] long elementId,
    [Description("Max solids to detail individually. Default: 20")] int maxSolids = 20,
    CancellationToken ct = default)
{
    var p = new JObject { ["elementId"] = elementId, ["maxSolids"] = maxSolids };
    var result = await revit.ExecuteAsync("get_element_solid_geometry", p, ct);
    return result.ToString();
}
```

No `ToolResponseShaper` — payload is bounded by `maxSolids`.

## Component 3 — Contract test

`src/RevitCortex.Tests/Server/ServerToolContractTests.cs`

`GetElementSolidGeometry_ExposesElementIdAndMaxSolids`: assert param sequence
(`revit`, `elementId`, `maxSolids`, `ct`), types (`long`, `int`), `maxSolids` default 20,
and a non-null `Description`. Matches the existing reflection style.

## Component 4 — Bookkeeping

- `ToolCount_MatchesExpected`: 195 → 196 (add comment line).
- Regenerate `tool-schemas.txt` via `node server/generate-tool-schemas-csharp.mjs`.
- Document in `docs/USER_GUIDE.md` under the Elements section.

## Verification

- Build `Debug R24` (net48) **and** `Debug R25` (net8) — Plugin + Tools.
- `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
  → expect 222 passed / 1 skipped / 0 failed.
- Before any release: also build R23/R26/R27 (global rule). Not the gate for this change.

## Out of scope (YAGNI)

- No per-face/per-edge geometry detail (only counts).
- No cross-section profile extraction (the rebar code insets from bbox + centroid).
- No `compact` flag (payload already bounded).

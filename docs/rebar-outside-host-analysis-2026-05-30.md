# Rebar outside-host analysis - Snowdon Towers

Date: 2026-05-30
Model: `Snowdon Towers Sample Structural.rvt`
Revit: 2025.4
Scope: investigate why Rebar creation frequently hits or later produces "Rebar is placed completely outside of its host."

## Executive summary

At the time of this analysis the active model has no current "outside of its host" warnings:

- total Revit warnings: 2
- outside-host warnings: 0
- audited Rebar elements: 398
- Rebar elements with valid host: 398
- Rebar elements missing host: 0
- Rebar bbox-vs-host bbox suspects: 0

So the model is currently clean. The failures are reproducible from the creation workflow and audit history, not from remaining invalid Rebar elements.

## Evidence collected

RevitCortex MCP stdio was unavailable (`Transport closed`), but the Revit plugin TCP bridge was active on `127.0.0.1:8080`. Calls were made through the same JSON-RPC bridge used by the server.

Model context:

- Project: Snowdon Towers
- File: `C:\Users\luigi.dattilo\Desktop\Snowdon Towers Sample Structural.rvt`
- Links loaded: Architectural, Facades, HVAC, Electrical, Plumbing, Site
- Revit warning snapshot: floor shape-editing warning and wall insert conflict only

Representative host geometry:

| Host id | Category | BBox dimensions mm | Notes |
| --- | --- | --- | --- |
| 606069 | Structural Columns | 610 x 610 x 3531 | Vertical column, long axis Z |
| 606873 | Structural Framing | 2858 x 610 x 610 | Beam along X |
| 606885 | Structural Framing | 610 x 6693 x 762 | Beam along Y |
| 606916 | Structural Framing | 610 x 5131 x 1308 | Beam along Y with deeper section |
| 608011 | Structural Framing | 5338 x 610 x 610 | Beam along X |
| 670055 | Floors | 18974 x 21946 x 254 | Slab with area reinforcement |

Representative good placement:

- Host `606069` bbox: X `[-21641..-21031]`, Y `[5550..6160]`, Z `[-5334..-1803]` mm
- Rebar `1033177` bbox from centerline: X `[-21603..-21074]`, Y `[5593..6117]`, Z `[-5294..-5284]` mm
- Result: inside host, about 38 mm cover from host faces, matching cover type "Interior (framing, columns)".

## Root causes found

### 1. Global bounding-box coordinates are being used as if they were host-local axes

Several batch scripts infer length/width/height from `host.get_BoundingBox(null)` and then create offsets like:

```csharp
pos + new XYZ(0, -hw, -hh)
pos + new XYZ(0,  hw,  hh)
```

That only works by accident for an X-aligned beam whose cross-section is global Y/Z. It fails or becomes fragile for:

- vertical columns, where the long axis is Z and the cross-section is X/Y
- Y-aligned beams, where the long axis is Y and the cross-section is X/Z
- rotated framing, where neither cross-section axis is a global basis vector

This is the strongest model-specific cause. The model contains hosts in multiple principal orientations, so a global-axis recipe will eventually place some curves outside the host volume.

### 2. Rebar curve plane normal is sometimes hard-coded

The audit shows creation attempts with `normal={"x":0,"y":0,"z":1}` across different framing hosts. For `CreateFromCurves`, the normal must match the plane of the supplied curves and the intended bar-set distribution direction. A normal that is valid for a horizontal column hoop is not generally valid for a beam stirrup or a vertical side loop.

Observed failures related to this family of issues:

- `curves do not form a valid CurveLoop` on hosts `606877`, `606879`, `606881`, `606883`
- repeated rebar creation attempts changing normal/plane until successful

### 3. Layout rules are applied with missing or invalid `arrayLengthMm`

The RevitCortex helper currently parses omitted `arrayLengthMm` as `0`. Then `SetLayoutAsFixedNumber`, `SetLayoutAsMaximumSpacing`, or `SetLayoutAsMinimumClearSpacing` throws:

```text
the set length arrayLength isn't acceptable.
Parameter name: arrayLength
```

Observed in audit:

- `create_rebar_from_shape`, host `606069`, layout `fixed_number` without `arrayLengthMm`
- `create_rebar_from_shape`, host `606069`, layout `maximum_spacing` without `arrayLengthMm`
- `create_rebar_from_curves`, host `606069`, layout `maximum_spacing` with missing/invalid array length

This is not literally the outside-host warning, but it is part of the same creation-failure cluster.

### 4. Tool-level host validation checks host eligibility, not candidate geometry containment

`RequireHost` correctly rejects non-host elements, but `create_rebar_from_shape`, `create_rebar_from_curves`, and `create_free_form_rebar` do not preflight the candidate origin/curves/loops against the host bbox or solid.

When a caller gives global coordinates outside the host, Revit is the first component to discover it. That produces either a transaction failure or a Revit warning instead of an actionable `InvalidInput` response with host/candidate extents.

## Why the model is now clean

The current model has no active outside-host warnings and the read-only audit found no Rebar centerline bbox completely outside its host bbox. Earlier invalid/recently-created bars appear to have been cleaned up; for example `1033176` existed during earlier checks but now returns `ElementNotFound`.

## Recommended fixes

1. Add a host-local frame helper for Rebar generation:
   - beams: use `LocationCurve` direction as longitudinal axis
   - columns: use vertical Z or family instance transform as longitudinal axis
   - cross-section axes: derive orthonormal `axisU` / `axisV` from transform or cross-products
   - generate curves as `center + axisLong*t + axisU*u + axisV*v`, never with hard-coded global offsets

2. Add preflight geometry validation before Revit transactions:
   - curves/free-form: compute candidate curve bbox in internal feet
   - shape-driven: at minimum validate origin and placement plane near/inside host bbox
   - compare to host bbox expanded by tolerance and cover
   - return `InvalidInput` with host bbox, candidate bbox, host id, and suggested axis correction

3. Validate layout input before calling Revit:
   - require positive `arrayLengthMm` for `fixed_number`, `maximum_spacing`, and `minimum_clear_spacing`
   - reject missing/zero `arrayLengthMm` before transaction
   - optionally infer it from host local length minus cover when `autoArrayLength=true`

4. Add an audit command or diagnostic mode:
   - `check_rebar_host_containment(hostId?, maxResults?)`
   - report active outside-host warnings
   - list Rebars whose centerline bbox is outside host bbox
   - include host bbox and rebar bbox in mm

5. For batch generation on this model, do not mix columns and beams in one global-axis routine. Split by host axis:
   - vertical columns: hoop plane XY, normal Z, distribution Z
   - X beams: stirrup plane YZ, normal X, distribution X
   - Y beams: stirrup plane XZ, normal Y, distribution Y

## Immediate operating rule

Before creating Rebar on a host:

1. Read host bbox/category/cover.
2. Determine host local longitudinal axis.
3. Build curves in the host local frame.
4. Check candidate bbox intersects the host bbox with cover/tolerance.
5. Only then call `create_rebar_from_curves` or `create_rebar_from_shape`.

This would prevent the recurring "Rebar is placed completely outside of its host" class from surfacing late as a Revit warning.

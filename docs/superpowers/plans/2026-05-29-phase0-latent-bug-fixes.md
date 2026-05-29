# Phase 0 — Latent Bug Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make 9 tools actually perform the operations their `Description`/parsed parameters advertise but currently don't.

**Architecture:** Each fix is a localized edit to one tool file in `src/RevitCortex.Tools/`. Most add a missing `switch` case or apply a parsed-but-ignored parameter. Three (B1, B2, B7) add a genuinely new branch using a Revit API not yet referenced by that tool. One file at a time; build R25 + R24 after each; single phase commit at the end after the full-target build + tests.

**Tech Stack:** C# multi-target (net48 for R23/R24, net8+ for R25/R26, net10 for R27), Autodesk.Revit.DB, Newtonsoft.Json.Linq, xUnit.

**Cross-target rule (CLAUDE.md):** no `record`/`init`/`GetValueOrDefault`/`Index`/`Range`; `ElementId(long)` vs `ElementId(int)` via `#if REVIT2024_OR_GREATER`. Build BOTH `Debug R25` and `Debug R24` after every file — green R25 ≠ green R24.

**Build/test commands:**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~<Name>"
```

> **Testing note:** these tools all require a live `Document`, so behavior cannot be unit-tested off-Revit (the existing test suite uses `FakeTool`/`FakeAnalyzer`, not a real Revit doc). Verification for Phase 0 is: (1) both target builds compile, (2) full test suite still green (no regressions), (3) the code path is provably present by inspection. Live Revit verification is deferred to a session with Revit open, like the parameter tools were.

---

### Task B3: tag_walls — apply useLeader, add tagTypeId + orientation + specific wall IDs

**Files:**
- Modify: `src/RevitCortex.Tools/Annotations/TagWallsTool.cs`

**Root cause:** `useLeader` parsed at line 29, but `IndependentTag.Create(... false ...)` at line 84 hardcodes `false`. Orientation hardcoded `Horizontal`. Always tags ALL walls.

- [ ] **Step 1: Update Description + parse new optional params**

Replace the `Description` (line 22) and the parse block (line 29):

```csharp
    public string Description => "Tags walls in the current view. Tags all walls by default, or specific ones via wallIds. Supports useLeader, tagTypeId, and orientation (horizontal/vertical).";
```

Replace line 29 (`var useLeader = ...`) with:

```csharp
        var useLeader = input["useLeader"]?.Value<bool>() ?? false;
        var orientationStr = input["orientation"]?.Value<string>() ?? "horizontal";
        var tagOrientation = orientationStr.Equals("vertical", StringComparison.OrdinalIgnoreCase)
            ? TagOrientation.Vertical : TagOrientation.Horizontal;
        var requestedTagTypeId = input["tagTypeId"]?.Value<long?>() ?? 0;
        var wallIds = input["wallIds"]?.ToObject<List<long>>();
```

- [ ] **Step 2: Honor wallIds (specific subset) in the collector**

Replace the wall collector (lines 35-39) with:

```csharp
            var wallsQuery = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>();

            var walls = (wallIds != null && wallIds.Count > 0)
                ? wallsQuery.Where(w => wallIds.Contains(ToolHelpers.GetElementIdValue(w.Id))).ToList()
                : wallsQuery.ToList();
```

- [ ] **Step 3: Honor requestedTagTypeId before the OST_WallTags fallback**

Insert, immediately before the existing `// Find wall tag type` block (line 41):

```csharp
            FamilySymbol? tagType = null;
            if (requestedTagTypeId > 0)
            {
                tagType = doc.GetElement(ToolHelpers.ToElementId(requestedTagTypeId)) as FamilySymbol;
            }
```

Then change the existing `var tagType = new FilteredElementCollector...` (line 42) to assign into the existing variable instead of redeclaring — i.e. replace `var tagType =` with `tagType ??=` and wrap the multi-line collector in parentheses so the `??=` binds:

```csharp
            tagType ??= new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_WallTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();
```

(The `if (tagType == null)` multi-category fallback at line 48-56 stays as-is.)

- [ ] **Step 4: Apply useLeader + orientation in the Create call**

Replace lines 84-85:

```csharp
                    IndependentTag.Create(doc, view.Id, reference, useLeader, TagMode.TM_ADDBY_CATEGORY,
                        tagOrientation, midPoint);
```

- [ ] **Step 5: Verify ToolHelpers.ToElementId exists**

Run: `grep -n "ToElementId" src/RevitCortex.Tools/Utilities/ToolHelpers.cs`
Expected: a `public static ElementId ToElementId(long ...)` method. If absent, use the inline `#if REVIT2024_OR_GREATER new ElementId(id) #else new ElementId((int)id) #endif` pattern instead.

- [ ] **Step 6: Build both targets**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj` then `-c "Debug R24"`.
Expected: 0 errors both.

---

### Task B5: align_viewports — implement the "model" alignMode

**Files:**
- Modify: `src/RevitCortex.Tools/Sheets/AlignViewportsTool.cs`

**Root cause:** `alignMode` parsed (line 32) but the loop always does `SetBoxCenter` (box/placement alignment). "model" should align so the same model coordinate sits at the same sheet position across viewports.

**Approach (net48-safe):** In "model" mode, for each target compute the offset between the source view's model-origin projection and the target's, and adjust the target box center so a chosen model point lands at the same paper location. The robust, API-simple implementation: align by matching each viewport's box center *minus* the paper-space delta of its view origin. We use `Viewport.GetBoxCenter()` + the view's `CropBox`/`Outline` is complex; instead use the documented approach of aligning the box centers offset by the difference in the views' project positions via `Viewport`'s box outline. Given complexity and net48 constraints, "model" mode aligns the box *outline min corner* (so equal-scale plans of the same region line up) rather than box center.

- [ ] **Step 1: Update Description to be accurate**

Replace line 21:

```csharp
    public string Description => "Aligns viewports across sheets. alignMode 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same model region line up.";
```

- [ ] **Step 2: Branch the alignment on alignMode**

Replace the source-center capture (line 49) with a mode-aware source anchor:

```csharp
            var useModel = alignMode.Equals("model", StringComparison.OrdinalIgnoreCase);
            var sourceCenter = sourceVp.GetBoxCenter();
            var sourceOutline = sourceVp.GetBoxOutline();
            var sourceAnchor = useModel ? sourceOutline.MinimumPoint : sourceCenter;
```

- [ ] **Step 3: Apply the mode-aware anchor per target**

Replace the per-target apply (lines 70-71) with:

```csharp
                    if (useModel)
                    {
                        var tOutline = targetVp.GetBoxOutline();
                        var delta = sourceAnchor - tOutline.MinimumPoint;
                        targetVp.SetBoxCenter(targetVp.GetBoxCenter() + delta);
                    }
                    else
                    {
                        targetVp.SetBoxCenter(sourceAnchor);
                    }
                    results.Add(new { viewportId = tid, success = true });
```

- [ ] **Step 4: Build both targets**

Run R25 then R24 build. Expected: 0 errors. (`Viewport.GetBoxOutline()` exists on all targets.)

---

### Task B6: create_line_based_element — apply beam baseOffset

**Files:**
- Modify: `src/RevitCortex.Tools/Elements/CreateLineBasedElementTool.cs`

**Root cause:** lines 234-237 fetch `offsetParam` and compute nothing — the beam's start/end elevation offset is never set. `baseOffset` (computed line 120) is applied to walls (line 177) but dropped for beams.

- [ ] **Step 1: Apply baseOffset to the family instance**

Replace lines 233-237 (the `// Set base offset if applicable` block down to `createdIds.Add(...)`) with:

```csharp
                            // Apply base offset to the instance (start+end elevation for beams, else free-host offset)
                            if (Math.Abs(baseOffset) > 1e-9)
                            {
                                var startElev = instance.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
                                var endElev = instance.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);
                                if (startElev != null && !startElev.IsReadOnly && endElev != null && !endElev.IsReadOnly)
                                {
                                    startElev.Set(baseOffset);
                                    endElev.Set(baseOffset);
                                }
                                else
                                {
                                    var freeOffset = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                                    if (freeOffset != null && !freeOffset.IsReadOnly)
                                        freeOffset.Set(baseOffset);
                                }
                            }
                            createdIds.Add(ToolHelpers.GetElementIdValue(instance.Id));
```

- [ ] **Step 2: Build both targets**

Run R25 then R24 build. Expected: 0 errors.

---

### Task B8: create_dimensions — honor dimensionStyleId in point-to-point branch

**Files:**
- Modify: `src/RevitCortex.Tools/Annotations/CreateDimensionsTool.cs` (read first; line numbers below approximate from audit)

- [ ] **Step 1: Read the file to locate both dimension-creation branches**

Run: `grep -n "dimensionStyleId\|NewDimension\|DimensionType\|point" src/RevitCortex.Tools/Annotations/CreateDimensionsTool.cs`
Identify the element-mode branch (which applies `DimensionType`) and the point-to-point branch (which doesn't).

- [ ] **Step 2: Resolve the style once and apply it in BOTH branches**

In the point-to-point branch, after the `Dimension` is created (the `doc.Create.NewDimension(...)` call), apply the same style resolution the element-mode branch uses. Pattern (adapt variable names to the file):

```csharp
            // Apply dimension style if provided (point-to-point branch parity with element branch)
            if (dimensionStyleId > 0)
            {
                var dimType = doc.GetElement(ToolHelpers.ToElementId(dimensionStyleId)) as DimensionType;
                if (dimType != null)
                    createdDimension.DimensionType = dimType;
            }
```

> If the element-mode branch resolves the style differently (e.g. by name), mirror that exact logic instead — the goal is parity, not a second mechanism.

- [ ] **Step 3: Build both targets**

Run R25 then R24 build. Expected: 0 errors.

---

### Task B4: measure_between_elements — implement closest_points

**Files:**
- Modify: `src/RevitCortex.Tools/Elements/MeasureBetweenElementsTool.cs`

**Root cause:** `ResolvePoint` (lines 83-110) always returns bbox center; `closest_points` mode is documented (line 21) but never computes closest points between the two elements' geometry.

**Approach (net48-safe, robust):** closest-point-between-two-elements needs BOTH elements together (it's pairwise, not per-element). Add a dedicated pairwise path used only when `measureType == "closest_points"` and both refs are elements. Use solid-to-solid distance via `Solid` faces when available, falling back to bounding-box closest points. Bounding-box closest point is deterministic and always available.

- [ ] **Step 1: Add a pairwise closest-points short-circuit in Execute**

After `var p2 = ResolvePoint(...)` is currently computed (line 51), the distance is `p1.DistanceTo(p2)`. Replace the closest-points handling by computing the two points pairwise BEFORE the per-point resolve when in that mode. Insert immediately after line 49's `try {` opening (before the two `ResolvePoint` calls), guarded:

```csharp
            XYZ p1, p2;
            if (measureType.Equals("closest_points", StringComparison.OrdinalIgnoreCase)
                && elementId1 > 0 && elementId2 > 0)
            {
                var e1 = doc.GetElement(ToElementId(elementId1));
                var e2 = doc.GetElement(ToElementId(elementId2));
                if (e1 == null) throw new ArgumentException($"Element {elementId1} not found");
                if (e2 == null) throw new ArgumentException($"Element {elementId2} not found");
                ClosestPoints(e1, e2, out p1, out p2);
            }
            else
            {
                p1 = ResolvePoint(doc, elementId1, rawPoint1, measureType);
                p2 = ResolvePoint(doc, elementId2, rawPoint2, measureType);
            }
```

Delete the now-duplicated original lines 50-51 (`XYZ p1 = ResolvePoint(...)` / `XYZ p2 = ResolvePoint(...)`).

- [ ] **Step 2: Add the ClosestPoints helper (bbox closest-corner pair)**

Add a new private static method after `ResolvePoint` (after line 110):

```csharp
    /// <summary>
    /// Computes the closest points between two elements using their axis-aligned
    /// bounding boxes (deterministic, available on all targets). For each axis the
    /// nearest in-range coordinate is chosen, giving the closest points of the two AABBs.
    /// </summary>
    private static void ClosestPoints(Element e1, Element e2, out XYZ p1, out XYZ p2)
    {
        var bb1 = e1.get_BoundingBox(null);
        var bb2 = e2.get_BoundingBox(null);
        if (bb1 == null || bb2 == null)
        {
            // Fall back to location/centroid distance
            p1 = CenterOf(e1);
            p2 = CenterOf(e2);
            return;
        }

        p1 = new XYZ(
            ClampAxis(bb1.Min.X, bb1.Max.X, bb2.Min.X, bb2.Max.X, true),
            ClampAxis(bb1.Min.Y, bb1.Max.Y, bb2.Min.Y, bb2.Max.Y, true),
            ClampAxis(bb1.Min.Z, bb1.Max.Z, bb2.Min.Z, bb2.Max.Z, true));
        p2 = new XYZ(
            ClampAxis(bb2.Min.X, bb2.Max.X, bb1.Min.X, bb1.Max.X, true),
            ClampAxis(bb2.Min.Y, bb2.Max.Y, bb1.Min.Y, bb1.Max.Y, true),
            ClampAxis(bb2.Min.Z, bb2.Max.Z, bb1.Min.Z, bb1.Max.Z, true));
    }

    // Nearest point on [aMin,aMax] to the interval [bMin,bMax] on one axis.
    private static double ClampAxis(double aMin, double aMax, double bMin, double bMax, bool _)
    {
        var target = (bMin + bMax) / 2.0;
        if (target < aMin) return aMin;
        if (target > aMax) return aMax;
        return target;
    }

    private static XYZ CenterOf(Element e)
    {
        var bb = e.get_BoundingBox(null);
        if (bb != null) return (bb.Min + bb.Max) / 2.0;
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        throw new ArgumentException("Element has no measurable geometry");
    }
```

- [ ] **Step 3: Build both targets**

Run R25 then R24 build. Expected: 0 errors.

---

### Task B1: modify_schedule — add set_filter + clear_filter actions

**Files:**
- Modify: `src/RevitCortex.Tools/Project/ModifyScheduleTool.cs`

**Root cause:** Description (line 21) claims "set/clear filters" but the action whitelist (lines 56-65) and switch (lines 73-81) omit them.

- [ ] **Step 1: Add the two actions to the whitelist**

Replace the whitelist `if` (lines 56-65) so `set_filter` and `clear_filter` are accepted:

```csharp
            if (normalizedAction != "add_field" &&
                normalizedAction != "remove_field" &&
                normalizedAction != "set_sorting" &&
                normalizedAction != "clear_sorting" &&
                normalizedAction != "set_filter" &&
                normalizedAction != "clear_filter" &&
                normalizedAction != "rename")
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: add_field, remove_field, set_sorting, clear_sorting, set_filter, clear_filter, rename");
            }
```

- [ ] **Step 2: Add the two cases to the dispatch switch**

In the switch (lines 73-81), add before the `_ =>` arm:

```csharp
                "set_filter" => SetFilter(schedule, input),
                "clear_filter" => ClearFilter(schedule),
```

- [ ] **Step 3: Implement SetFilter + ClearFilter helpers**

Add after `ClearSorting` (after line 165):

```csharp
    private static object SetFilter(ViewSchedule schedule, JObject input)
    {
        var fieldName = input["filterField"]?.Value<string>() ?? input["fieldName"]?.Value<string>();
        var op = (input["filterType"]?.Value<string>() ?? input["operator"]?.Value<string>() ?? "equal").ToLowerInvariant();
        var valueToken = input["filterValue"] ?? input["value"];
        if (string.IsNullOrEmpty(fieldName))
            return new { error = "filterField required" };

        var def = schedule.Definition;
        ScheduleFieldId? fieldId = null;
        for (int i = 0; i < def.GetFieldCount(); i++)
        {
            var f = def.GetField(i);
            if (f.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            {
                fieldId = f.FieldId;
                break;
            }
        }
        if (fieldId == null)
            return new { error = $"Field '{fieldName}' not present in schedule. Add it first with add_field." };

        var filterType = op switch
        {
            "notequal" or "not_equal" => ScheduleFilterType.NotEqual,
            "greater" or "greaterthan" => ScheduleFilterType.GreaterThan,
            "greaterorequal" or "greaterthanorequal" => ScheduleFilterType.GreaterThanOrEqual,
            "less" or "lessthan" => ScheduleFilterType.LessThan,
            "lessorequal" or "lessthanorequal" => ScheduleFilterType.LessThanOrEqual,
            "contains" => ScheduleFilterType.Contains,
            "notcontains" or "does_not_contain" => ScheduleFilterType.NotContains,
            "beginswith" => ScheduleFilterType.BeginsWith,
            "endswith" => ScheduleFilterType.EndsWith,
            "hasvalue" or "has_value" => ScheduleFilterType.HasParameter,
            "hasnovalue" or "is_empty" => ScheduleFilterType.HasNoValue,
            _ => ScheduleFilterType.Equal
        };

        ScheduleFilter filter;
        if (filterType == ScheduleFilterType.HasParameter || filterType == ScheduleFilterType.HasNoValue)
        {
            filter = new ScheduleFilter(fieldId, filterType);
        }
        else if (valueToken != null && valueToken.Type == JTokenType.Integer)
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken.Value<int>());
        }
        else if (valueToken != null && (valueToken.Type == JTokenType.Float))
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken.Value<double>());
        }
        else
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken?.Value<string>() ?? "");
        }

        def.AddFilter(filter);
        return new { action = "set_filter", field = fieldName, filterType = filterType.ToString() };
    }

    private static object ClearFilter(ViewSchedule schedule)
    {
        var def = schedule.Definition;
        int count = def.GetFilterCount();
        def.ClearFilters();
        return new { action = "clear_filter", clearedCount = count };
    }
```

> **Verify API surface before building:** `ScheduleFilterType` enum member names vary slightly across Revit versions (e.g. `HasParameter`/`HasValue`). After step 4 if a member doesn't resolve, run `grep` is useless (it's in the Revit DLL) — instead try the build error message which lists valid members, and adjust. `ScheduleDefinition.AddFilter/ClearFilters/GetFilterCount` exist on all targets.

- [ ] **Step 4: Build both targets**

Run R25 then R24 build. Expected: 0 errors. If `ScheduleFilterType` member names differ, fix per the build error and rebuild.

---

### Task B2: batch_export — add PDF export

**Files:**
- Modify: `src/RevitCortex.Tools/Project/BatchExportTool.cs`

**Root cause:** Description (line 23) promises PDF; the `format` switch (lines 69-165) has no PDF case → falls to "Unsupported format".

**Approach:** `PDFExportOptions` + `Document.Export(folder, options, viewIds)` exists from Revit 2022. All our targets (R23–R27) have it, so no `#if` strictly needed — but the API signature is `Export(string folder, IList<ElementId> views, PDFExportOptions options)`. Each view/sheet exported individually for per-file naming parity with the other formats.

- [ ] **Step 1: Add the PDF case to the switch**

Insert a new `case` before `default:` (line 162):

```csharp
                case "PDF":
                {
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            var pdfOptions = new PDFExportOptions
                            {
                                FileName = name,
                                Combine = false
                            };
                            doc.Export(outputDir, new List<ElementId> { id }, pdfOptions);
                            results.Add(new { name = view.Name, file = $"{name}.pdf", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
```

- [ ] **Step 2: Update the default-case suggestion + Description to include PDF**

Change line 164's suggestion to `"Use: DWG, DXF, DGN, PDF, IMAGE"` and the Description (line 23) to:

```csharp
    public string Description => "Exports multiple views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats.";
```

- [ ] **Step 3: Build both targets**

Run R25 then R24 build. Expected: 0 errors. If `PDFExportOptions`/`Export(folder, ids, pdfOptions)` doesn't resolve on net48 (R24), wrap the entire PDF case in `#if REVIT2022_OR_GREATER ... #else (return unsupported) #endif` — but R24 is net48 *and* Revit 2024 which has the API, so it should resolve. Confirm by build.

---

### Task B7: create_view — add elevation + drafting view types

**Files:**
- Modify: `src/RevitCortex.Tools/Views/CreateViewTool.cs`

**Root cause:** Description (line 22) advertises "elevation" but the switch (lines 45-68) has no elevation or drafting case → falls to default error.

- [ ] **Step 1: Add elevation + drafting cases to the switch**

Insert before `default:` (line 64):

```csharp
                case "elevation":
                    createdView = CreateElevationView(doc, input);
                    break;
                case "drafting":
                case "draftingview":
                    var vftDraft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (vftDraft != null) createdView = ViewDrafting.Create(doc, vftDraft.Id);
                    break;
```

Update the default-case suggestion (line 67) to `"Use: floorplan, ceilingplan, section, elevation, drafting, 3d"`.

- [ ] **Step 2: Implement CreateElevationView helper**

Add after `CreateSectionView` (after line 175):

```csharp
    private static ViewSection? CreateElevationView(Document doc, JObject input)
    {
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
        if (vft == null) return null;

        // Elevation needs a marker placed on a plan view at an origin point.
        var ownerPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate);
        if (ownerPlan == null) return null;

        var originX = (input["originX"]?.Value<double>() ?? 0) / MmPerFoot;
        var originY = (input["originY"]?.Value<double>() ?? 0) / MmPerFoot;
        var originZ = (input["originZ"]?.Value<double>() ?? 0) / MmPerFoot;
        var origin = new XYZ(originX, originY, originZ);

        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, input["scale"]?.Value<int?>() ?? 100);
        // Index 0=N? Revit elevation marker indices: 0,1,2,3 around the marker. Use the requested direction index.
        var dirStr = (input["direction"]?.Value<string>() ?? "north").ToLowerInvariant();
        int index = dirStr switch { "east" => 1, "south" => 2, "west" => 3, _ => 0 };
        return marker.CreateElevation(doc, ownerPlan.Id, index);
    }
```

> **Verify API:** `ElevationMarker.CreateElevationMarker(Document, ElementId, XYZ, double scale)` and `marker.CreateElevation(Document, ElementId ownerViewId, int index)` exist on all targets. The `scale` overload that takes `int` may be `double` in some versions — if the build complains, cast `(double)`.

- [ ] **Step 3: Build both targets**

Run R25 then R24 build. Expected: 0 errors.

---

### Task B9: duplicate_sheet_with_content — copy loose sheet annotations

**Files:**
- Modify: `src/RevitCortex.Tools/Sheets/DuplicateSheetWithContentTool.cs` (read first)

**Root cause:** Description claims "all annotations" copied; only viewports/schedules/revisions are. Loose detail items / text notes / generic annotations placed directly on the sheet are skipped.

- [ ] **Step 1: Read the file to find where viewports/schedules are copied**

Run: `grep -n "Viewport\|Schedule\|CopyElements\|GetAllViewports\|annotation\|TextNote\|FilteredElementCollector" src/RevitCortex.Tools/Sheets/DuplicateSheetWithContentTool.cs`
Identify the source-sheet collection and the destination-sheet `ElementId destSheet.Id`, and whether `ElementTransformUtils.CopyElements` is already used.

- [ ] **Step 2: Add a pass copying sheet-owned annotation elements**

After the viewport/schedule copy, before the result is returned, add a collection of detail/annotation elements owned by the source sheet (OwnerViewId == source sheet) and copy them to the destination sheet view with `ElementTransformUtils.CopyElements(sourceSheet, ids, destSheet, Transform.Identity, new CopyPasteOptions())`. Pattern (adapt variable names):

```csharp
            // Copy loose annotations placed directly on the sheet (text notes, detail items, generic annotations)
            var annoCats = new[]
            {
                BuiltInCategory.OST_TextNotes,
                BuiltInCategory.OST_GenericAnnotation,
                BuiltInCategory.OST_DetailComponents,
                BuiltInCategory.OST_Lines
            };
            var annoIds = new FilteredElementCollector(doc, sourceSheet.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && annoCats.Contains((BuiltInCategory)ToolHelpers.GetCategoryIdValue(e.Category)))
                .Select(e => e.Id)
                .ToList();
            if (annoIds.Count > 0)
            {
                try
                {
                    ElementTransformUtils.CopyElements(sourceSheet, annoIds, destSheet, Transform.Identity, new CopyPasteOptions());
                }
                catch (Exception ex) { warnings.Add($"Some sheet annotations not copied: {ex.Message}"); }
            }
```

> If the file has no `warnings` list, add one or fold the message into the existing result. If `ToolHelpers.GetCategoryIdValue` doesn't exist, use the `#if REVIT2024_OR_GREATER e.Category.Id.Value #else e.Category.Id.IntegerValue #endif` pattern.

- [ ] **Step 3: Verify the Description matches reality**

If, after step 2, some annotation categories still aren't copied, adjust the Description so it doesn't overstate. Otherwise leave it.

- [ ] **Step 4: Build both targets**

Run R25 then R24 build. Expected: 0 errors.

---

### Task FINAL: full-target build, tests, docs, commit

- [ ] **Step 1: Build all five targets**

```bash
for cfg in "Debug R23" "Debug R24" "Debug R25" "Debug R26" "Debug R27"; do
  dotnet build -c "$cfg" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj || echo "FAILED: $cfg"
done
```
Expected: 0 errors on R23/R24/R25/R26; R27 only if .NET 10+ SDK present (else NETSDK1045 is acceptable per CLAUDE.md).

- [ ] **Step 2: Run the full test suite (no regressions)**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: previous baseline (224 passed / 1 skipped) still holds.

- [ ] **Step 3: Update tool schemas + USER_GUIDE for the new params/actions**

- Regenerate: `node server/generate-tool-schemas-csharp.mjs` (updates `tool-schemas.txt`)
- Update `docs/USER_GUIDE.md` entries for: `modify_schedule` (set_filter/clear_filter), `batch_export` (PDF), `tag_walls` (useLeader/tagTypeId/orientation/wallIds), `measure_between_elements` (closest_points now real), `create_view` (elevation/drafting).
- Update the MCP tool definitions in `src/RevitCortex.Server/Tools/` to expose the new params (ParameterTools/ProjectTools/etc.) where the server schema needs them — check which file declares each tool and add the params to its description/schema.

- [ ] **Step 4: Commit the phase**

```bash
git add -A
git commit -F .git/COMMIT_PHASE0.txt   # write the message to this file first
```
Message:
```
fix(tools): Phase 0 — implement 9 latent operations tools advertised but never performed

B1 modify_schedule set_filter/clear_filter; B2 batch_export PDF; B3 tag_walls
useLeader+tagTypeId+orientation+wallIds; B4 measure_between_elements closest_points
(AABB pairwise); B5 align_viewports model mode; B6 create_line_based_element beam
baseOffset; B7 create_view elevation+drafting; B8 create_dimensions p2p
dimensionStyleId parity; B9 duplicate_sheet_with_content loose annotations.

Each tool now performs what its Description/parsed params promised. All targets
R23-R26 green; tests unchanged. Live Revit verification deferred.
```
(use a single-quoted here-doc on bash, or write to a file and `-F` per the PowerShell/bash caveat in CLAUDE.md memory.)

---

## Self-Review notes

- **Spec coverage:** B1–B9 each map to a task. The 3 doc/impl flag mismatches (get_materials compact etc.) are NOT in Phase 0 scope — they're separate from the 9 behavioral bugs and tracked in the gap-analysis spec for a later doc-alignment pass.
- **Type consistency:** helper names used consistently (`ClosestPoints`/`ClampAxis`/`CenterOf`; `SetFilter`/`ClearFilter`; `CreateElevationView`). `ToolHelpers.ToElementId`/`GetElementIdValue`/`GetCategoryIdValue` usages are guarded with "verify exists, else inline #if" fallbacks because the helper surface wasn't fully read.
- **Placeholder scan:** no TBD/TODO. Two tasks (B8, B9) require reading the file first because exact line numbers weren't captured in the audit — the read step is the first step of each, and the code to add is concrete.
- **Risk:** B4 closest_points uses AABB approximation, not true solid distance — documented in code comments; honest and net48-safe. B7 elevation marker index→direction mapping may need live tuning; build-safe regardless.

# Element Tools Migration — Batch 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate 8 element tools from the fork to RevitCortex, covering core read/write/modify operations.

**Architecture:** Same as Batch 1 — each tool implements `ICortexTool`, receives `CortexSession`, returns `CortexResult<T>`. Document via `session.Store.Get<object>("activeDocument") as Document`. UI operations need `UIDocument` via `new UIDocument(doc)`. Threading handled by `RevitThreadDispatcher` (already in place).

**Tech Stack:** C# (Revit API, Newtonsoft.Json), TypeScript (Zod, MCP SDK)

**Fork reference (read-only):** `C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit`

---

## File Map

### C# Tools (RevitCortex.Tools/Elements/)

| File | Tool | Type |
|------|------|------|
| `GetSelectedElementsTool.cs` | get_selected_elements | READ |
| `GetCurrentViewElementsTool.cs` | get_current_view_elements | READ |
| `GetLinkedElementsTool.cs` | get_linked_elements | READ |
| `GetElementsInSpatialVolumeTool.cs` | get_elements_in_spatial_volume | READ |
| `DeleteElementTool.cs` | delete_element | WRITE |
| `OperateElementTool.cs` | operate_element | WRITE |
| `ChangeElementTypeTool.cs` | change_element_type | WRITE |
| `ModifyElementTool.cs` | modify_element | WRITE |

### TypeScript (server/)

| File | Responsibility |
|------|---------------|
| `server/src/schemas/elements.ts` | **Modify** — add 8 Zod schemas |
| `server/src/tools/get_selected_elements.ts` | MCP registration |
| `server/src/tools/get_current_view_elements.ts` | MCP registration |
| `server/src/tools/get_linked_elements.ts` | MCP registration |
| `server/src/tools/get_elements_in_spatial_volume.ts` | MCP registration |
| `server/src/tools/delete_element.ts` | MCP registration |
| `server/src/tools/operate_element.ts` | MCP registration |
| `server/src/tools/change_element_type.ts` | MCP registration |
| `server/src/tools/modify_element.ts` | MCP registration |
| `server/src/tools/register.ts` | **Modify** — add 8 registrations |

---

## Task 1: Read tools — get_selected_elements + get_current_view_elements

**Files:**
- Create: `src/RevitCortex.Tools/Elements/GetSelectedElementsTool.cs`
- Create: `src/RevitCortex.Tools/Elements/GetCurrentViewElementsTool.cs`

**Fork references (read-only):**
- `mcp-servers-for-revit/commandset/Services/GetSelectedElementsEventHandler.cs`
- `mcp-servers-for-revit/commandset/Services/GetCurrentViewElementsEventHandler.cs`

- [ ] **Step 1: Write GetSelectedElementsTool.cs**

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetSelectedElementsTool : ICortexTool
{
    public string Name => "get_selected_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var limit = input["limit"]?.Value<int>() ?? 500;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var uidoc = new UIDocument(doc);
        var selectedIds = uidoc.Selection.GetElementIds();

        if (selectedIds.Count == 0)
            return CortexResult<object>.Ok(new
            {
                message = "No elements selected",
                elements = new List<object>()
            });

        var results = new List<object>();
        var count = 0;

        foreach (var id in selectedIds)
        {
            if (count >= limit) break;
            var element = doc.GetElement(id);
            if (element == null) continue;

#if REVIT2024_OR_GREATER
            var elemId = element.Id.Value;
#else
            var elemId = (long)element.Id.IntegerValue;
#endif
            results.Add(new
            {
                id = elemId,
                uniqueId = element.UniqueId,
                name = element.Name,
                category = element.Category?.Name
            });
            count++;
        }

        return CortexResult<object>.Ok(new
        {
            message = $"Found {selectedIds.Count} selected elements, returning {results.Count}",
            elements = results
        });
    }
}
```

- [ ] **Step 2: Write GetCurrentViewElementsTool.cs**

Read the fork's `GetCurrentViewElementsEventHandler.cs` first. Key features to port:
- Dual category filtering (model + annotation) via `ElementMulticategoryFilter`
- Hidden element filtering via `element.IsHidden(activeView)`
- Field filtering (LocationX/Y/Z, StartX/Y/Z, EndX/Y/Z, Length, Comments, Mark, Level, Family, Type)
- Default category lists when none specified
- Limit + truncation flag

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetCurrentViewElementsTool : ICortexTool
{
    public string Name => "get_current_view_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var activeView = doc.ActiveView;
        if (activeView == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active view");

        var modelCategories = input["modelCategoryList"]?.ToObject<List<string>>();
        var annotationCategories = input["annotationCategoryList"]?.ToObject<List<string>>();
        var includeHidden = input["includeHidden"]?.Value<bool>() ?? false;
        var limit = input["limit"]?.Value<int>() ?? 500;
        var fields = input["fields"]?.ToObject<List<string>>();

        try
        {
            // Resolve categories to BuiltInCategory
            var allBics = new List<BuiltInCategory>();
            ResolveCategoryList(modelCategories, allBics);
            ResolveCategoryList(annotationCategories, allBics);

            // If no categories specified, use defaults
            if (allBics.Count == 0)
            {
                allBics.AddRange(new[]
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Windows, BuiltInCategory.OST_Furniture,
                    BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Stairs,
                    BuiltInCategory.OST_Dimensions, BuiltInCategory.OST_TextNotes
                });
            }

            var collector = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType();

            if (allBics.Count > 0)
                collector = collector.WherePasses(new ElementMulticategoryFilter(allBics));

            var elements = collector.ToElements();

            // Filter hidden elements
            if (!includeHidden)
                elements = elements.Where(e => !e.IsHidden(activeView)).ToList();

            var truncated = elements.Count > limit;
            var limited = elements.Take(limit).ToList();

            var results = new List<object>();
            foreach (var elem in limited)
            {
                results.Add(BuildElementInfo(elem, doc, fields));
            }

#if REVIT2024_OR_GREATER
            var viewId = activeView.Id.Value;
#else
            var viewId = (long)activeView.Id.IntegerValue;
#endif

            return CortexResult<object>.Ok(new
            {
                viewId,
                viewName = activeView.Name,
                totalElementsInView = elements.Count,
                filteredElementCount = results.Count,
                truncated,
                elements = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static void ResolveCategoryList(List<string>? categoryNames, List<BuiltInCategory> output)
    {
        if (categoryNames == null) return;
        foreach (var name in categoryNames)
        {
            if (Enum.TryParse<BuiltInCategory>(name, out var bic))
                output.Add(bic);
        }
    }

    private static object BuildElementInfo(Element elem, Document doc, List<string>? fields)
    {
#if REVIT2024_OR_GREATER
        var elemId = elem.Id.Value;
#else
        var elemId = (long)elem.Id.IntegerValue;
#endif
        var info = new Dictionary<string, object?>
        {
            ["id"] = elemId,
            ["uniqueId"] = elem.UniqueId,
            ["name"] = elem.Name,
            ["category"] = elem.Category?.Name
        };

        // Add location if available
        if (elem.Location is LocationPoint lp)
        {
            info["locationX"] = Math.Round(lp.Point.X * 304.8, 1);
            info["locationY"] = Math.Round(lp.Point.Y * 304.8, 1);
            info["locationZ"] = Math.Round(lp.Point.Z * 304.8, 1);
        }
        else if (elem.Location is LocationCurve lc)
        {
            var start = lc.Curve.GetEndPoint(0);
            var end = lc.Curve.GetEndPoint(1);
            info["startX"] = Math.Round(start.X * 304.8, 1);
            info["startY"] = Math.Round(start.Y * 304.8, 1);
            info["startZ"] = Math.Round(start.Z * 304.8, 1);
            info["endX"] = Math.Round(end.X * 304.8, 1);
            info["endY"] = Math.Round(end.Y * 304.8, 1);
            info["endZ"] = Math.Round(end.Z * 304.8, 1);
        }

        // Common properties
        var typeElem = doc.GetElement(elem.GetTypeId());
        if (typeElem != null)
        {
            info["familyName"] = (typeElem as FamilySymbol)?.FamilyName ?? typeElem.Name;
            info["typeName"] = typeElem.Name;
        }

        // Optional field filtering
        if (fields != null && fields.Count > 0)
        {
            foreach (var field in fields)
            {
                var param = elem.LookupParameter(field);
                if (param != null && param.HasValue)
                    info[field] = param.AsValueString() ?? param.AsString();
            }
        }

        return info;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 4: Run existing tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

Expected: 19 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools/Elements/GetSelectedElementsTool.cs src/RevitCortex.Tools/Elements/GetCurrentViewElementsTool.cs
git commit -m "feat: migrate get_selected_elements and get_current_view_elements tools"
```

---

## Task 2: Read tools — get_linked_elements + get_elements_in_spatial_volume

**Files:**
- Create: `src/RevitCortex.Tools/Elements/GetLinkedElementsTool.cs`
- Create: `src/RevitCortex.Tools/Elements/GetElementsInSpatialVolumeTool.cs`

**Fork references (read-only):**
- `mcp-servers-for-revit/commandset/Services/GetLinkedElementsEventHandler.cs`
- `mcp-servers-for-revit/commandset/Services/GetElementsInSpatialVolumeEventHandler.cs`

- [ ] **Step 1: Write GetLinkedElementsTool.cs**

Read the fork's EventHandler first. Key features:
- Filter RevitLinkInstance by partial name match (case-insensitive)
- Get linked document via `linkInstance.GetLinkDocument()`
- Category resolution in linked document
- Parameter extraction via LookupParameter with AsValueString/AsString fallback
- Per-link maxElements limit

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetLinkedElementsTool : ICortexTool
{
    public string Name => "get_linked_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var linkName = input["linkName"]?.ToString();
        var categories = input["categories"]?.ToObject<List<string>>();
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>();
        var maxElements = input["maxElements"]?.Value<int>() ?? 5000;

        try
        {
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            // Filter by name if specified
            if (!string.IsNullOrEmpty(linkName))
            {
                linkInstances = linkInstances
                    .Where(li => li.Name.IndexOf(linkName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (linkInstances.Count == 0)
                return CortexResult<object>.Ok(new
                {
                    message = "No linked models found",
                    linkCount = 0,
                    links = new List<object>()
                });

            var linksResult = new List<object>();
            foreach (var linkInstance in linkInstances)
            {
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                var collector = new FilteredElementCollector(linkDoc)
                    .WhereElementIsNotElementType();

                // Category filter
                if (categories != null && categories.Count > 0)
                {
                    var bics = new List<BuiltInCategory>();
                    foreach (var cat in categories)
                    {
                        if (Enum.TryParse<BuiltInCategory>(cat, out var bic))
                            bics.Add(bic);
                    }
                    if (bics.Count > 0)
                        collector = collector.WherePasses(new ElementMulticategoryFilter(bics));
                }

                var elements = collector.ToElements();
                var limited = elements.Take(maxElements).ToList();

                var elemResults = new List<object>();
                foreach (var elem in limited)
                {
#if REVIT2024_OR_GREATER
                    var elemId = elem.Id.Value;
#else
                    var elemId = (long)elem.Id.IntegerValue;
#endif
                    var info = new Dictionary<string, object?>
                    {
                        ["elementId"] = elemId,
                        ["category"] = elem.Category?.Name,
                        ["name"] = elem.Name
                    };

                    // Extract requested parameters
                    if (parameterNames != null)
                    {
                        foreach (var paramName in parameterNames)
                        {
                            var param = elem.LookupParameter(paramName);
                            if (param != null && param.HasValue)
                                info[paramName] = param.AsValueString() ?? param.AsString();
                        }
                    }

                    elemResults.Add(info);
                }

#if REVIT2024_OR_GREATER
                var linkId = linkInstance.Id.Value;
#else
                var linkId = (long)linkInstance.Id.IntegerValue;
#endif
                linksResult.Add(new
                {
                    linkName = linkInstance.Name,
                    linkId,
                    documentTitle = linkDoc.Title,
                    elementCount = elemResults.Count,
                    elements = elemResults
                });
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Found elements across {linksResult.Count} linked models",
                linkCount = linksResult.Count,
                links = linksResult
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Write GetElementsInSpatialVolumeTool.cs**

Read the fork's EventHandler first. Key features:
- Three volume types: room, area, custom bounding box
- MM to feet conversion (÷ 304.8) for custom coordinates
- `BoundingBoxIntersectsFilter` for spatial queries
- Excludes the spatial element itself from results
- Category filtering per volume
- Per-volume maxElements limit with truncation flag

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetElementsInSpatialVolumeTool : ICortexTool
{
    public string Name => "get_elements_in_spatial_volume";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmToFeet = 1.0 / 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var volumeType = input["volumeType"]?.ToString()?.ToLower() ?? "room";
        var volumeIds = input["volumeIds"]?.ToObject<List<long>>();
        var categoryFilter = input["categoryFilter"]?.ToObject<List<string>>();
        var maxElementsPerVolume = input["maxElementsPerVolume"]?.Value<int>() ?? 100;

        try
        {
            var volumes = new List<object>();
            var totalElements = 0;

            if (volumeType == "custom")
            {
                // Custom bounding box
                var minX = input["customMinX"]?.Value<double>() ?? 0;
                var minY = input["customMinY"]?.Value<double>() ?? 0;
                var minZ = input["customMinZ"]?.Value<double>() ?? 0;
                var maxX = input["customMaxX"]?.Value<double>() ?? 0;
                var maxY = input["customMaxY"]?.Value<double>() ?? 0;
                var maxZ = input["customMaxZ"]?.Value<double>() ?? 0;

                var min = new XYZ(minX * MmToFeet, minY * MmToFeet, minZ * MmToFeet);
                var max = new XYZ(maxX * MmToFeet, maxY * MmToFeet, maxZ * MmToFeet);

                var result = QueryVolume(doc, min, max, null, categoryFilter, maxElementsPerVolume, "custom", 0, "Custom Volume");
                totalElements += ((dynamic)result).elementCount;
                volumes.Add(result);
            }
            else
            {
                // Room or Area
                var bic = volumeType == "area" ? BuiltInCategory.OST_Areas : BuiltInCategory.OST_Rooms;
                var spatialElements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var spatialElem in spatialElements)
                {
                    // Filter by IDs if specified
#if REVIT2024_OR_GREATER
                    var spatialId = spatialElem.Id.Value;
#else
                    var spatialId = (long)spatialElem.Id.IntegerValue;
#endif
                    if (volumeIds != null && volumeIds.Count > 0 && !volumeIds.Contains(spatialId))
                        continue;

                    // Skip rooms with zero area
                    if (spatialElem is Room room && room.Area <= 0) continue;

                    var bb = spatialElem.get_BoundingBox(null);
                    if (bb == null) continue;

                    var volumeName = GetSpatialName(spatialElem);
                    var result = QueryVolume(doc, bb.Min, bb.Max, spatialElem.Id,
                        categoryFilter, maxElementsPerVolume, volumeType, spatialId, volumeName);
                    totalElements += ((dynamic)result).elementCount;
                    volumes.Add(result);
                }
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Found {totalElements} elements across {volumes.Count} volumes",
                totalElements,
                volumeCount = volumes.Count,
                volumes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static object QueryVolume(Document doc, XYZ min, XYZ max, ElementId? excludeId,
        List<string>? categoryFilter, int maxElements, string volumeType, long volumeId, string volumeName)
    {
        var outline = new Outline(min, max);
        var bbFilter = new BoundingBoxIntersectsFilter(outline);
        var collector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(bbFilter);

        // Category filter
        if (categoryFilter != null && categoryFilter.Count > 0)
        {
            var bics = new List<BuiltInCategory>();
            foreach (var cat in categoryFilter)
            {
                if (Enum.TryParse<BuiltInCategory>(cat, out var bic))
                    bics.Add(bic);
            }
            if (bics.Count > 0)
                collector = collector.WherePasses(new ElementMulticategoryFilter(bics));
        }

        var elements = collector.ToElements()
            .Where(e => excludeId == null || e.Id != excludeId)
            .ToList();

        var truncated = elements.Count > maxElements;
        var limited = elements.Take(maxElements).ToList();

        var elemResults = new List<object>();
        foreach (var elem in limited)
        {
#if REVIT2024_OR_GREATER
            var elemId = elem.Id.Value;
#else
            var elemId = (long)elem.Id.IntegerValue;
#endif
            var typeElem = doc.GetElement(elem.GetTypeId());
            elemResults.Add(new
            {
                elementId = elemId,
                name = elem.Name,
                category = elem.Category?.Name,
                familyName = (typeElem as FamilySymbol)?.FamilyName,
                typeName = typeElem?.Name
            });
        }

        return new
        {
            volumeType,
            volumeId,
            volumeName,
            elementCount = elemResults.Count,
            totalElementCount = elements.Count,
            truncated,
            elements = elemResults
        };
    }

    private static string GetSpatialName(Element elem)
    {
        if (elem is Room room)
        {
            var nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            var name = nameParam?.AsString() ?? "";
            return $"{room.Number} - {name}".Trim(' ', '-');
        }

        var areaName = elem.LookupParameter("Name")?.AsString() ?? elem.Name;
        return areaName;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 4: Run existing tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools/Elements/GetLinkedElementsTool.cs src/RevitCortex.Tools/Elements/GetElementsInSpatialVolumeTool.cs
git commit -m "feat: migrate get_linked_elements and get_elements_in_spatial_volume tools"
```

---

## Task 3: Write tools — delete_element + operate_element

**Files:**
- Create: `src/RevitCortex.Tools/Elements/DeleteElementTool.cs`
- Create: `src/RevitCortex.Tools/Elements/OperateElementTool.cs`

**Fork references (read-only):**
- `mcp-servers-for-revit/commandset/Services/DeleteElementEventHandler.cs`
- `mcp-servers-for-revit/commandset/Services/OperateElementEventHandler.cs`

- [ ] **Step 1: Write DeleteElementTool.cs**

Read the fork's EventHandler first. Key features:
- **dryRun** (default true): Preview what would be deleted without committing
- Batch deletion via `doc.Delete(ICollection<ElementId>)`
- Invalid ID validation and reporting
- Transaction with commit/rollback

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class DeleteElementTool : ICortexTool
{
    public string Name => "delete_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Provide an array of element IDs to delete");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        // Validate elements exist
        var validIds = new List<ElementId>();
        var invalidIds = new List<long>();
        var elementInfos = new List<object>();

        foreach (var id in elementIds)
        {
#if REVIT2024_OR_GREATER
            var elementId = new ElementId(id);
#else
            var elementId = new ElementId((int)id);
#endif
            var element = doc.GetElement(elementId);
            if (element != null)
            {
                validIds.Add(elementId);
                elementInfos.Add(new
                {
                    elementId = id,
                    name = element.Name,
                    category = element.Category?.Name
                });
            }
            else
            {
                invalidIds.Add(id);
            }
        }

        if (validIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "None of the specified elements were found");

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                message = $"DRY RUN: Would delete {validIds.Count} elements",
                dryRun = true,
                wouldDelete = elementInfos,
                invalidIds = invalidIds.Count > 0 ? invalidIds : null
            });
        }

        // Actual deletion
        try
        {
            using (var tx = new Transaction(doc, "RevitCortex: Delete Elements"))
            {
                tx.Start();
                doc.Delete(validIds);
                tx.Commit();
            }

            return CortexResult<object>.Ok(new
            {
                message = $"Deleted {validIds.Count} elements",
                dryRun = false,
                deletedCount = validIds.Count,
                invalidIds = invalidIds.Count > 0 ? invalidIds : null
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Deletion failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Write OperateElementTool.cs**

Read the fork's EventHandler first. Key features:
- Actions: Select, SelectionBox, SetColor, SetTransparency, Hide, TempHide, Isolate, Unhide, ResetIsolate, Delete
- Select uses UIDocument, no transaction needed
- SelectionBox creates section box in 3D view with 1ft offset
- SetColor uses OverrideGraphicSettings with FillPatternElement
- Transparency clamped 0-100
- Hide/Unhide/Isolate use view-specific methods

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class OperateElementTool : ICortexTool
{
    public string Name => "operate_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var data = input["data"] as JObject ?? input;

        var elementIds = data["elementIds"]?.ToObject<List<long>>();
        if (elementIds == null || elementIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds is required");

        var action = data["action"]?.ToString()?.ToLower();
        if (string.IsNullOrEmpty(action))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action is required",
                suggestion: "Valid actions: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, resetisolate, delete");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        // Resolve element IDs
        var revitIds = new List<ElementId>();
        foreach (var id in elementIds)
        {
#if REVIT2024_OR_GREATER
            revitIds.Add(new ElementId(id));
#else
            revitIds.Add(new ElementId((int)id));
#endif
        }

        try
        {
            switch (action)
            {
                case "select":
                    return SelectElements(doc, revitIds);

                case "selectionbox":
                    return CreateSelectionBox(doc, revitIds);

                case "setcolor":
                    var colorValue = data["colorValue"]?.ToObject<int[]>();
                    if (colorValue == null || colorValue.Length != 3)
                        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                            "colorValue must be [R, G, B] array");
                    return SetElementColor(doc, revitIds, colorValue);

                case "settransparency":
                    var transparency = data["transparencyValue"]?.Value<int>() ?? 50;
                    transparency = Math.Max(0, Math.Min(100, transparency));
                    return SetElementTransparency(doc, revitIds, transparency);

                case "hide":
                    return HideElements(doc, revitIds, false);

                case "temphide":
                    return HideElements(doc, revitIds, true);

                case "isolate":
                    return IsolateElements(doc, revitIds);

                case "unhide":
                    return UnhideElements(doc, revitIds);

                case "resetisolate":
                    return ResetIsolation(doc);

                case "delete":
                    return DeleteElements(doc, revitIds);

                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unknown action: {action}",
                        suggestion: "Valid actions: select, selectionbox, setcolor, settransparency, hide, temphide, isolate, unhide, resetisolate, delete");
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Operation failed: {ex.Message}");
        }
    }

    private static CortexResult<object> SelectElements(Document doc, List<ElementId> ids)
    {
        var uidoc = new UIDocument(doc);
        uidoc.Selection.SetElementIds(ids);
        return CortexResult<object>.Ok(new { message = $"Selected {ids.Count} elements" });
    }

    private static CortexResult<object> CreateSelectionBox(Document doc, List<ElementId> ids)
    {
        var uidoc = new UIDocument(doc);

        // Find or switch to 3D view
        var view3d = doc.ActiveView as View3D;
        if (view3d == null)
        {
            view3d = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            if (view3d == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No 3D view available");

            uidoc.ActiveView = view3d;
        }

        // Calculate aggregate bounding box
        BoundingBoxXYZ? aggregate = null;
        foreach (var id in ids)
        {
            var elem = doc.GetElement(id);
            var bb = elem?.get_BoundingBox(null);
            if (bb == null) continue;

            if (aggregate == null)
            {
                aggregate = new BoundingBoxXYZ
                {
                    Min = bb.Min,
                    Max = bb.Max
                };
            }
            else
            {
                aggregate.Min = new XYZ(
                    Math.Min(aggregate.Min.X, bb.Min.X),
                    Math.Min(aggregate.Min.Y, bb.Min.Y),
                    Math.Min(aggregate.Min.Z, bb.Min.Z));
                aggregate.Max = new XYZ(
                    Math.Max(aggregate.Max.X, bb.Max.X),
                    Math.Max(aggregate.Max.Y, bb.Max.Y),
                    Math.Max(aggregate.Max.Z, bb.Max.Z));
            }
        }

        if (aggregate == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No valid bounding boxes found");

        // Expand by 1 foot
        var offset = new XYZ(1, 1, 1);
        aggregate.Min -= offset;
        aggregate.Max += offset;

        using (var tx = new Transaction(doc, "RevitCortex: Selection Box"))
        {
            tx.Start();
            view3d.IsSectionBoxActive = true;
            view3d.SetSectionBox(aggregate);
            tx.Commit();
        }

        uidoc.ShowElements(ids);
        return CortexResult<object>.Ok(new { message = $"Created section box around {ids.Count} elements" });
    }

    private static CortexResult<object> SetElementColor(Document doc, List<ElementId> ids, int[] rgb)
    {
        var color = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);

        // Find a solid fill pattern
        var fillPattern = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern().IsFillableRegion);

        using (var tx = new Transaction(doc, "RevitCortex: Set Color"))
        {
            tx.Start();
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(color);
            if (fillPattern != null)
                ogs.SetSurfaceForegroundPatternId(fillPattern.Id);

            foreach (var id in ids)
                doc.ActiveView.SetElementOverrides(id, ogs);

            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = $"Set color [{rgb[0]},{rgb[1]},{rgb[2]}] on {ids.Count} elements" });
    }

    private static CortexResult<object> SetElementTransparency(Document doc, List<ElementId> ids, int transparency)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Set Transparency"))
        {
            tx.Start();
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceTransparency(transparency);

            foreach (var id in ids)
                doc.ActiveView.SetElementOverrides(id, ogs);

            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = $"Set transparency {transparency}% on {ids.Count} elements" });
    }

    private static CortexResult<object> HideElements(Document doc, List<ElementId> ids, bool temporary)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Hide Elements"))
        {
            tx.Start();
            if (temporary)
                doc.ActiveView.HideElementsTemporary(ids);
            else
                doc.ActiveView.HideElements(ids);
            tx.Commit();
        }

        var mode = temporary ? "temporarily" : "permanently";
        return CortexResult<object>.Ok(new { message = $"Hidden {ids.Count} elements {mode}" });
    }

    private static CortexResult<object> IsolateElements(Document doc, List<ElementId> ids)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Isolate Elements"))
        {
            tx.Start();
            doc.ActiveView.IsolateElementsTemporary(ids);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = $"Isolated {ids.Count} elements" });
    }

    private static CortexResult<object> UnhideElements(Document doc, List<ElementId> ids)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Unhide Elements"))
        {
            tx.Start();
            doc.ActiveView.UnhideElements(ids);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = $"Unhidden {ids.Count} elements" });
    }

    private static CortexResult<object> ResetIsolation(Document doc)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Reset Isolation"))
        {
            tx.Start();
            doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = "Reset isolation mode" });
    }

    private static CortexResult<object> DeleteElements(Document doc, List<ElementId> ids)
    {
        using (var tx = new Transaction(doc, "RevitCortex: Delete Elements"))
        {
            tx.Start();
            doc.Delete(ids);
            tx.Commit();
        }

        return CortexResult<object>.Ok(new { message = $"Deleted {ids.Count} elements" });
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 4: Run existing tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools/Elements/DeleteElementTool.cs src/RevitCortex.Tools/Elements/OperateElementTool.cs
git commit -m "feat: migrate delete_element and operate_element tools"
```

---

## Task 4: Write tools — change_element_type + modify_element

**Files:**
- Create: `src/RevitCortex.Tools/Elements/ChangeElementTypeTool.cs`
- Create: `src/RevitCortex.Tools/Elements/ModifyElementTool.cs`

**Fork references (read-only):**
- `mcp-servers-for-revit/commandset/Services/ChangeElementTypeEventHandler.cs`
- `mcp-servers-for-revit/commandset/Services/ModifyElementEventHandler.cs`

- [ ] **Step 1: Write ChangeElementTypeTool.cs**

Read the fork's EventHandler first. Key features:
- Target resolution: by typeId, by typeName + familyName, by typeName alone
- `element.ChangeTypeId(targetTypeId)` for the actual change
- Per-element try/catch for partial success
- Transaction wrapping

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class ChangeElementTypeTool : ICortexTool
{
    public string Name => "change_element_type";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds is required");

        var targetTypeId = input["targetTypeId"]?.Value<long>() ?? 0;
        var targetTypeName = input["targetTypeName"]?.ToString();
        var targetFamilyName = input["targetFamilyName"]?.ToString();

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        // Resolve target type
        ElementId? resolvedTypeId = null;

        if (targetTypeId > 0)
        {
#if REVIT2024_OR_GREATER
            resolvedTypeId = new ElementId(targetTypeId);
#else
            resolvedTypeId = new ElementId((int)targetTypeId);
#endif
            if (doc.GetElement(resolvedTypeId) == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Target type ID {targetTypeId} not found");
        }
        else if (!string.IsNullOrEmpty(targetTypeName))
        {
            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements();

            Element? match = null;

            if (!string.IsNullOrEmpty(targetFamilyName))
            {
                match = allTypes.FirstOrDefault(t =>
                    t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase) &&
                    (t as ElementType)?.FamilyName?.Equals(targetFamilyName, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (match == null)
            {
                match = allTypes.FirstOrDefault(t =>
                    t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Type '{targetTypeName}' not found",
                    suggestion: "Verify the type name exists in the project. Use get_element_parameters to check available types.");

            resolvedTypeId = match.Id;
        }
        else
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide targetTypeId or targetTypeName",
                suggestion: "Specify the target type by ID or name");
        }

        var results = new List<object>();
        var successCount = 0;
        var failCount = 0;

        using (var tx = new Transaction(doc, "RevitCortex: Change Element Type"))
        {
            tx.Start();

            foreach (var id in elementIds)
            {
#if REVIT2024_OR_GREATER
                var elementId = new ElementId(id);
#else
                var elementId = new ElementId((int)id);
#endif
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    results.Add(new { elementId = id, success = false, message = "Element not found" });
                    failCount++;
                    continue;
                }

                try
                {
                    element.ChangeTypeId(resolvedTypeId);
                    results.Add(new { elementId = id, success = true, message = "Type changed" });
                    successCount++;
                }
                catch (Exception ex)
                {
                    results.Add(new { elementId = id, success = false, message = ex.Message });
                    failCount++;
                }
            }

            if (successCount > 0)
                tx.Commit();
            else
                tx.RollBack();
        }

        var targetName = doc.GetElement(resolvedTypeId)?.Name ?? "Unknown";
        return CortexResult<object>.Ok(new
        {
            message = $"Changed type to '{targetName}': {successCount} succeeded, {failCount} failed",
            targetTypeName = targetName,
            successCount,
            failCount,
            results
        });
    }
}
```

- [ ] **Step 2: Write ModifyElementTool.cs**

Read the fork's EventHandler first. Key features:
- Four actions: move, rotate, mirror, copy
- `ElementTransformUtils.MoveElements/RotateElements/MirrorElements/CopyElements`
- MM to feet conversion for all coordinates
- Rotation: degrees → radians, Z-axis rotation line
- Mirror: plane from origin + normal vector
- Copy: returns new element IDs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class ModifyElementTool : ICortexTool
{
    public string Name => "modify_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    private const double MmToFeet = 1.0 / 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds is required");

        var action = input["action"]?.ToString()?.ToLower();
        if (string.IsNullOrEmpty(action))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "action is required",
                suggestion: "Valid actions: move, rotate, mirror, copy");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var revitIds = elementIds.Select(id =>
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }).ToList();

        try
        {
            using (var tx = new Transaction(doc, $"RevitCortex: {action} elements"))
            {
                tx.Start();

                switch (action)
                {
                    case "move":
                    {
                        var translation = ParseXYZ(input["translation"], "translation");
                        if (translation == null)
                            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                "translation is required for move",
                                suggestion: "Provide {\"x\": mm, \"y\": mm, \"z\": mm}");

                        ElementTransformUtils.MoveElements(doc, revitIds, translation);
                        tx.Commit();
                        return CortexResult<object>.Ok(new
                        {
                            message = $"Moved {revitIds.Count} elements"
                        });
                    }

                    case "rotate":
                    {
                        var center = ParseXYZ(input["rotationCenter"], "rotationCenter");
                        var angle = input["rotationAngle"]?.Value<double>();
                        if (center == null || angle == null)
                            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                "rotationCenter and rotationAngle are required for rotate");

                        var axis = Line.CreateBound(center, center + XYZ.BasisZ);
                        var radians = angle.Value * Math.PI / 180.0;
                        ElementTransformUtils.RotateElements(doc, revitIds, axis, radians);
                        tx.Commit();
                        return CortexResult<object>.Ok(new
                        {
                            message = $"Rotated {revitIds.Count} elements by {angle}°"
                        });
                    }

                    case "mirror":
                    {
                        var origin = ParseXYZ(input["mirrorPlaneOrigin"], "mirrorPlaneOrigin");
                        var normal = ParseXYZ(input["mirrorPlaneNormal"], "mirrorPlaneNormal");
                        if (origin == null || normal == null)
                            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                "mirrorPlaneOrigin and mirrorPlaneNormal are required for mirror");

                        var normalizedNormal = normal.Normalize();
                        var plane = Plane.CreateByNormalAndOrigin(normalizedNormal, origin);
                        ElementTransformUtils.MirrorElements(doc, revitIds, plane, false);
                        tx.Commit();
                        return CortexResult<object>.Ok(new
                        {
                            message = $"Mirrored {revitIds.Count} elements"
                        });
                    }

                    case "copy":
                    {
                        var offset = ParseXYZ(input["copyOffset"], "copyOffset");
                        if (offset == null)
                            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                "copyOffset is required for copy",
                                suggestion: "Provide {\"x\": mm, \"y\": mm, \"z\": mm}");

                        var newIds = ElementTransformUtils.CopyElements(doc, revitIds, offset);
                        tx.Commit();

                        var newIdValues = newIds.Select(id =>
                        {
#if REVIT2024_OR_GREATER
                            return id.Value;
#else
                            return (long)id.IntegerValue;
#endif
                        }).ToList();

                        return CortexResult<object>.Ok(new
                        {
                            message = $"Copied {revitIds.Count} elements",
                            newElementIds = newIdValues
                        });
                    }

                    default:
                        tx.RollBack();
                        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                            $"Unknown action: {action}",
                            suggestion: "Valid actions: move, rotate, mirror, copy");
                }
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"{action} failed: {ex.Message}");
        }
    }

    private static XYZ? ParseXYZ(JToken? token, string fieldName)
    {
        if (token == null) return null;

        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;

        return new XYZ(x * MmToFeet, y * MmToFeet, z * MmToFeet);
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 4: Run existing tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tools/Elements/ChangeElementTypeTool.cs src/RevitCortex.Tools/Elements/ModifyElementTool.cs
git commit -m "feat: migrate change_element_type and modify_element tools"
```

---

## Task 5: TypeScript — Zod schemas + MCP registrations for 8 tools

**Files:**
- Modify: `server/src/schemas/elements.ts` — add 8 Zod schemas
- Create: `server/src/tools/get_selected_elements.ts`
- Create: `server/src/tools/get_current_view_elements.ts`
- Create: `server/src/tools/get_linked_elements.ts`
- Create: `server/src/tools/get_elements_in_spatial_volume.ts`
- Create: `server/src/tools/delete_element.ts`
- Create: `server/src/tools/operate_element.ts`
- Create: `server/src/tools/change_element_type.ts`
- Create: `server/src/tools/modify_element.ts`
- Modify: `server/src/tools/register.ts` — add 8 registrations

- [ ] **Step 1: Add Zod schemas to server/src/schemas/elements.ts**

Append to the existing file:

```typescript
export const GetSelectedElementsInput = z.object({
  limit: z.number().int().optional().default(500).describe("Max elements to return. Default: 500"),
});

export const GetCurrentViewElementsInput = z.object({
  modelCategoryList: z.array(z.string()).optional().describe("Model categories (OST_*) to include"),
  annotationCategoryList: z.array(z.string()).optional().describe("Annotation categories to include"),
  includeHidden: z.boolean().optional().default(false).describe("Include hidden elements. Default: false"),
  limit: z.number().int().optional().default(500).describe("Max elements. Default: 500"),
  fields: z.array(z.string()).optional().describe("Specific parameter names to extract"),
});

export const GetLinkedElementsInput = z.object({
  linkName: z.string().optional().describe("Filter linked models by name (partial match)"),
  categories: z.array(z.string()).optional().describe("Category codes (OST_*) to filter"),
  parameterNames: z.array(z.string()).optional().describe("Parameter names to extract per element"),
  maxElements: z.number().int().optional().default(5000).describe("Max elements per link. Default: 5000"),
});

const PointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().describe("Z coordinate in mm"),
});

export const GetElementsInSpatialVolumeInput = z.object({
  volumeType: z.enum(["room", "area", "custom"]).optional().default("room").describe("Volume type"),
  volumeIds: z.array(z.number()).optional().describe("Specific room/area IDs to search"),
  categoryFilter: z.array(z.string()).optional().describe("Categories (OST_*) to filter"),
  maxElementsPerVolume: z.number().int().optional().default(100).describe("Max elements per volume"),
  customMinX: z.number().optional().describe("Custom bounding box min X (mm)"),
  customMinY: z.number().optional().describe("Custom bounding box min Y (mm)"),
  customMinZ: z.number().optional().describe("Custom bounding box min Z (mm)"),
  customMaxX: z.number().optional().describe("Custom bounding box max X (mm)"),
  customMaxY: z.number().optional().describe("Custom bounding box max Y (mm)"),
  customMaxZ: z.number().optional().describe("Custom bounding box max Z (mm)"),
});

export const DeleteElementInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to delete"),
  dryRun: z.boolean().optional().default(true).describe("Preview mode — true = show what would be deleted without deleting. Default: true"),
});

export const OperateElementInput = z.object({
  data: z.object({
    elementIds: z.array(z.number()).min(1).describe("Element IDs to operate on"),
    action: z.enum([
      "select", "selectionbox", "setcolor", "settransparency",
      "hide", "temphide", "isolate", "unhide", "resetisolate", "delete"
    ]).describe("Operation to perform"),
    colorValue: z.array(z.number().min(0).max(255)).length(3).optional().describe("RGB color [R, G, B] for setcolor"),
    transparencyValue: z.number().min(0).max(100).optional().describe("Transparency 0-100 for settransparency"),
  }),
});

export const ChangeElementTypeInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to change type"),
  targetTypeId: z.number().optional().describe("Target type element ID (preferred)"),
  targetTypeName: z.string().optional().describe("Target type name to search for"),
  targetFamilyName: z.string().optional().describe("Family name to narrow type search"),
});

export const ModifyElementInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to modify"),
  action: z.enum(["move", "rotate", "mirror", "copy"]).describe("Modification action"),
  translation: PointSchema.optional().describe("Translation vector for move (mm)"),
  rotationCenter: PointSchema.optional().describe("Rotation center point (mm)"),
  rotationAngle: z.number().optional().describe("Rotation angle in degrees"),
  mirrorPlaneOrigin: PointSchema.optional().describe("Mirror plane origin (mm)"),
  mirrorPlaneNormal: PointSchema.optional().describe("Mirror plane normal vector"),
  copyOffset: PointSchema.optional().describe("Copy offset vector (mm)"),
});
```

- [ ] **Step 2: Create 8 tool registration files**

Each follows the same pattern as Batch 1 tools. Create one file per tool:

`server/src/tools/get_selected_elements.ts`:
- Import `GetSelectedElementsInput`, tool name `"get_selected_elements"`
- Description: `"Get info about currently selected elements in Revit"`

`server/src/tools/get_current_view_elements.ts`:
- Import `GetCurrentViewElementsInput`, tool name `"get_current_view_elements"`
- Description: `"Get all elements from the current active view with optional category/field filtering"`

`server/src/tools/get_linked_elements.ts`:
- Import `GetLinkedElementsInput`, tool name `"get_linked_elements"`
- Description: `"Query elements from linked Revit models by category with parameter extraction"`

`server/src/tools/get_elements_in_spatial_volume.ts`:
- Import `GetElementsInSpatialVolumeInput`, tool name `"get_elements_in_spatial_volume"`
- Description: `"Find elements within rooms, areas, or custom bounding boxes"`

`server/src/tools/delete_element.ts`:
- Import `DeleteElementInput`, tool name `"delete_element"`
- Description: `"Delete elements by ID. Defaults to dryRun=true (preview mode). Set dryRun=false to actually delete."`

`server/src/tools/operate_element.ts`:
- Import `OperateElementInput`, tool name `"operate_element"`
- Description: `"Perform UI operations on elements: select, color, hide, unhide, isolate, section box, transparency, delete"`

`server/src/tools/change_element_type.ts`:
- Import `ChangeElementTypeInput`, tool name `"change_element_type"`
- Description: `"Change the family type of elements. Specify target by type ID, type name, or family+type name."`

`server/src/tools/modify_element.ts`:
- Import `ModifyElementInput`, tool name `"modify_element"`
- Description: `"Move, rotate, mirror, or copy elements. All coordinates in millimeters."`

All tool files follow this template pattern:

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { <SchemaName> } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function register<ToolName>Tool(server: McpServer): void {
  server.tool(
    "<tool_name>",
    "<description>",
    <SchemaName>.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("<tool_name>", args);
        });
        logToolCall({ tool: "<tool_name>", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "<tool_name>", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
```

- [ ] **Step 3: Update server/src/tools/register.ts**

Add 8 new imports and registrations to the existing file.

- [ ] **Step 4: Build TypeScript**

```bash
cd server && npx tsc --noEmit && npm run build
```

- [ ] **Step 5: Commit**

```bash
cd ..
git add server/
git commit -m "feat: add TypeScript MCP registrations for 8 element tools (batch 2)"
```

---

## Task 6: Full build verification

- [ ] **Step 1: Build C# for R23 and R25**

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 2: Run tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

Expected: 19 tests pass.

- [ ] **Step 3: Build TS**

```bash
cd server && npx tsc --noEmit && npm run build
```

- [ ] **Step 4: Fix any issues and commit**

Only commit if changes were made.

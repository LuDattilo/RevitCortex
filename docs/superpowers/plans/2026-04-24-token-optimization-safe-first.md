# Safe-First Token Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce MCP token consumption by shrinking oversized tool responses and fixing wrapper-to-tool contract mismatches without breaking existing client behavior.

**Architecture:** Work in three safe layers. First, align MCP wrapper signatures with the tool implementations so filters and limits actually apply. Second, add additive compact and summary flags at the C# MCP server layer, shaping large JSON payloads before they reach the model. Third, update docs and schemas so the lower-cost paths become the obvious paths for future sessions. Avoid changing existing defaults unless a dedicated follow-up patch proves compatibility.

**Tech Stack:** C#/.NET 8, xUnit, ModelContextProtocol.Server, Newtonsoft.Json.Linq, RevitCortex.Server, RevitCortex.Tools

---

### Task 1: Add Server Contract Tests For High-Risk Wrapper Mismatches

**Files:**
- Modify: `src/RevitCortex.Tests/RevitCortex.Tests.csproj`
- Create: `src/RevitCortex.Tests/Server/ServerToolContractTests.cs`

- [ ] **Step 1: Write the failing test**

```xml
<!-- src/RevitCortex.Tests/RevitCortex.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\RevitCortex.Core\RevitCortex.Core.csproj" />
  <ProjectReference Include="..\RevitCortex.Plugin\RevitCortex.Plugin.csproj" />
  <ProjectReference Include="..\RevitCortex.Tools\RevitCortex.Tools.csproj" />
  <ProjectReference Include="..\RevitCortex.Server\RevitCortex.Server.csproj" />
</ItemGroup>
```

```csharp
// src/RevitCortex.Tests/Server/ServerToolContractTests.cs
using System.Linq;
using System.Reflection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server;

public class ServerToolContractTests
{
    [Fact]
    public void GetCurrentViewElements_ExposesExplicitCategoryLists()
    {
        var method = typeof(ViewTools).GetMethod(nameof(ViewTools.GetCurrentViewElements));
        Assert.NotNull(method);

        var parameterNames = method!.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("modelCategoryList", parameterNames);
        Assert.Contains("annotationCategoryList", parameterNames);
    }

    [Fact]
    public void GetScheduleData_ExposesMaxRows()
    {
        var method = typeof(ViewTools).GetMethod(nameof(ViewTools.GetScheduleData));
        Assert.NotNull(method);

        var parameterNames = method!.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("maxRows", parameterNames);
    }

    [Fact]
    public void WorkflowModelAudit_ExposesStructuredAuditFlags()
    {
        var method = typeof(ProjectTools).GetMethod(nameof(ProjectTools.WorkflowModelAudit));
        Assert.NotNull(method);

        var parameterNames = method!.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("includeWarnings", parameterNames);
        Assert.Contains("includeFamilies", parameterNames);
        Assert.Contains("maxWarnings", parameterNames);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~ServerToolContractTests`

Expected: FAIL because `ViewTools.GetCurrentViewElements`, `ViewTools.GetScheduleData`, and `ProjectTools.WorkflowModelAudit` do not yet expose the expected parameters.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/RevitCortex.Server/Tools/ViewTools.cs
[McpServerTool(Name = "get_current_view_elements"), Description("List elements visible in the currently active view.")]
public static async Task<string> GetCurrentViewElements(
    RevitConnectionManager revit,
    [Description("Maximum number of elements to return")] int? limit = 50,
    [Description("Model category filters (e.g. OST_Walls, OST_Doors)")] string[]? modelCategoryList = null,
    [Description("Annotation category filters (e.g. OST_Dimensions, OST_TextNotes)")] string[]? annotationCategoryList = null,
    [Description("Legacy single-category filter; mapped into modelCategoryList for backward compatibility")] string? categoryFilter = null,
    [Description("Specific fields to include in the response")] string[]? fields = null,
    CancellationToken ct = default)
{
    var p = new JObject();
    if (limit != null) p["limit"] = limit;
    if (modelCategoryList != null) p["modelCategoryList"] = new JArray(modelCategoryList);
    if (annotationCategoryList != null) p["annotationCategoryList"] = new JArray(annotationCategoryList);
    if (categoryFilter != null && modelCategoryList == null) p["modelCategoryList"] = new JArray(categoryFilter);
    if (fields != null) p["fields"] = new JArray(fields);
    var result = await revit.ExecuteAsync("get_current_view_elements", p, ct);
    return result.ToString();
}

[McpServerTool(Name = "get_schedule_data"), Description("Export schedule data as JSON from an existing schedule view.")]
public static async Task<string> GetScheduleData(
    RevitConnectionManager revit,
    [Description("Schedule view element ID")] long scheduleId,
    [Description("Maximum number of body rows to return. Default: 500")] int? maxRows = null,
    CancellationToken ct = default)
{
    var p = new JObject { ["scheduleId"] = scheduleId };
    if (maxRows != null) p["maxRows"] = maxRows;
    var result = await revit.ExecuteAsync("get_schedule_data", p, ct);
    return result.ToString();
}
```

```csharp
// src/RevitCortex.Server/Tools/ProjectTools.cs
[McpServerTool(Name = "workflow_model_audit"), Description("Run a complete model audit workflow.")]
public static async Task<string> WorkflowModelAudit(
    RevitConnectionManager revit,
    [Description("Include warnings in the response. Default: true")] bool? includeWarnings = null,
    [Description("Include family lists in the response. Default: true")] bool? includeFamilies = null,
    [Description("Maximum grouped warnings returned. Default: 50")] int? maxWarnings = null,
    CancellationToken ct = default)
{
    var p = new JObject();
    if (includeWarnings != null) p["includeWarnings"] = includeWarnings;
    if (includeFamilies != null) p["includeFamilies"] = includeFamilies;
    if (maxWarnings != null) p["maxWarnings"] = maxWarnings;
    var result = await revit.ExecuteAsync("workflow_model_audit", p, ct);
    return result.ToString();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~ServerToolContractTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tests/RevitCortex.Tests.csproj src/RevitCortex.Tests/Server/ServerToolContractTests.cs src/RevitCortex.Server/Tools/ViewTools.cs src/RevitCortex.Server/Tools/ProjectTools.cs
git commit -m "test: pin MCP server contracts for token-safe wrappers"
```

### Task 2: Add Pure JSON Response Shaping At The MCP Server Layer

**Files:**
- Create: `src/RevitCortex.Server/Tools/ToolResponseShaper.cs`
- Create: `src/RevitCortex.Tests/Server/ToolResponseShaperTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/RevitCortex.Tests/Server/ToolResponseShaperTests.cs
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server;

public class ToolResponseShaperTests
{
    [Fact]
    public void ShapeAvailableFamilyTypesCompact_RemovesUniqueIds()
    {
        var payload = JArray.Parse("""
        [
          { "familyTypeId": 1, "uniqueId": "abc", "familyName": "Door", "typeName": "900x2100", "category": "Doors" }
        ]
        """);

        var shaped = ToolResponseShaper.Shape("get_available_family_types", payload, compact: true, summaryOnly: false);

        Assert.Equal(1, shaped["count"]!.Value<int>());
        Assert.Null(shaped["items"]![0]!["uniqueId"]);
    }

    [Fact]
    public void ShapeSchedulableFieldsSummaryOnly_ReturnsNamesOnly()
    {
        var payload = JObject.Parse("""
        {
          "category": "OST_Rooms",
          "scheduleType": "regular",
          "fieldCount": 2,
          "fields": [
            { "name": "Name", "fieldType": "Instance", "parameterId": 1 },
            { "name": "Number", "fieldType": "Instance", "parameterId": 2 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("list_schedulable_fields", payload, compact: true, summaryOnly: true);

        Assert.Equal(new[] { "Name", "Number" }, shaped["fieldNames"]!.ToObject<string[]>());
        Assert.Null(shaped["fields"]);
    }

    [Fact]
    public void ShapeRoomOpeningsSummaryOnly_KeepsCountsAndDropsNestedArrays()
    {
        var payload = JObject.Parse("""
        {
          "totalRooms": 1,
          "totalDoors": 4,
          "totalWindows": 2,
          "rooms": [
            {
              "roomId": 100,
              "roomName": "Office",
              "roomNumber": "A-101",
              "doorCount": 4,
              "doors": [{ "elementId": 1 }],
              "windowCount": 2,
              "windows": [{ "elementId": 2 }]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_room_openings", payload, compact: true, summaryOnly: true);

        Assert.Equal(4, shaped["rooms"]![0]!["doorCount"]!.Value<int>());
        Assert.Null(shaped["rooms"]![0]!["doors"]);
        Assert.Null(shaped["rooms"]![0]!["windows"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~ToolResponseShaperTests`

Expected: FAIL because `ToolResponseShaper` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/RevitCortex.Server/Tools/ToolResponseShaper.cs
using Newtonsoft.Json.Linq;

namespace RevitCortex.Server.Tools;

internal static class ToolResponseShaper
{
    public static JToken Shape(string toolName, JToken payload, bool compact, bool summaryOnly)
    {
        if (!compact && !summaryOnly) return payload;

        return toolName switch
        {
            "get_available_family_types" => ShapeAvailableFamilyTypes(payload),
            "list_schedulable_fields"    => ShapeSchedulableFields(payload, summaryOnly),
            "get_room_openings"          => ShapeRoomOpenings(payload, summaryOnly),
            _                            => payload
        };
    }

    private static JToken ShapeAvailableFamilyTypes(JToken payload)
    {
        var items = payload.Children<JObject>()
            .Select(x => new JObject
            {
                ["familyTypeId"] = x["familyTypeId"],
                ["familyName"] = x["familyName"],
                ["typeName"] = x["typeName"],
                ["category"] = x["category"]
            });

        return new JObject
        {
            ["count"] = items.Count(),
            ["items"] = new JArray(items)
        };
    }

    private static JToken ShapeSchedulableFields(JToken payload, bool summaryOnly)
    {
        if (!summaryOnly) return payload;

        var names = payload["fields"]!.Children<JObject>().Select(x => x["name"]);
        return new JObject
        {
            ["category"] = payload["category"],
            ["scheduleType"] = payload["scheduleType"],
            ["fieldCount"] = payload["fieldCount"],
            ["fieldNames"] = new JArray(names)
        };
    }

    private static JToken ShapeRoomOpenings(JToken payload, bool summaryOnly)
    {
        if (!summaryOnly) return payload;

        var rooms = payload["rooms"]!.Children<JObject>().Select(room => new JObject
        {
            ["roomId"] = room["roomId"],
            ["roomName"] = room["roomName"],
            ["roomNumber"] = room["roomNumber"],
            ["doorCount"] = room["doorCount"],
            ["windowCount"] = room["windowCount"]
        });

        return new JObject
        {
            ["totalRooms"] = payload["totalRooms"],
            ["totalDoors"] = payload["totalDoors"],
            ["totalWindows"] = payload["totalWindows"],
            ["rooms"] = new JArray(rooms)
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~ToolResponseShaperTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Server/Tools/ToolResponseShaper.cs src/RevitCortex.Tests/Server/ToolResponseShaperTests.cs
git commit -m "feat: add JSON response shaper for high-token server tools"
```

### Task 3: Wire Compact And Summary Flags Into The Highest-Cost MCP Wrappers

**Files:**
- Modify: `src/RevitCortex.Server/Tools/ElementTools.cs`
- Modify: `src/RevitCortex.Server/Tools/ProjectTools.cs`
- Modify: `src/RevitCortex.Tests/Server/ServerToolContractTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// append to src/RevitCortex.Tests/Server/ServerToolContractTests.cs
[Fact]
public void HighCostWrappers_ExposeCompactFlags()
{
    var familyTypes = typeof(ProjectTools).GetMethod(nameof(ProjectTools.GetAvailableFamilyTypes))!;
    var schedulable = typeof(ProjectTools).GetMethod(nameof(ProjectTools.ListSchedulableFields))!;
    var roomOpenings = typeof(ElementTools).GetMethod(nameof(ElementTools.GetRoomOpenings))!;

    Assert.Contains("compact", familyTypes.GetParameters().Select(p => p.Name));
    Assert.Contains("compact", schedulable.GetParameters().Select(p => p.Name));
    Assert.Contains("summaryOnly", schedulable.GetParameters().Select(p => p.Name));
    Assert.Contains("compact", roomOpenings.GetParameters().Select(p => p.Name));
    Assert.Contains("summaryOnly", roomOpenings.GetParameters().Select(p => p.Name));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~HighCostWrappers_ExposeCompactFlags`

Expected: FAIL because the wrapper methods do not yet expose these flags.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/RevitCortex.Server/Tools/ProjectTools.cs
[McpServerTool(Name = "get_available_family_types"), Description("List available family types in the Revit project.")]
public static async Task<string> GetAvailableFamilyTypes(
    RevitConnectionManager revit,
    [Description("Filter by category")] string? category = null,
    [Description("Return a compact payload without uniqueId-heavy rows. Default: false")] bool compact = false,
    CancellationToken ct = default)
{
    var p = new JObject();
    if (category != null) p["category"] = category;
    var result = await revit.ExecuteAsync("get_available_family_types", p, ct);
    return ToolResponseShaper.Shape("get_available_family_types", result, compact, summaryOnly: false).ToString();
}

[McpServerTool(Name = "list_schedulable_fields"), Description("Discover available schedulable fields for a category.")]
public static async Task<string> ListSchedulableFields(
    RevitConnectionManager revit,
    [Description("Category name (e.g. OST_Rooms). Default: OST_Rooms")] string? categoryName = null,
    [Description("Schedule type: regular | key | material-takeoff. Default: regular")] string? scheduleType = null,
    [Description("Return a compact payload. Default: false")] bool compact = false,
    [Description("Return names/counts only. Default: false")] bool summaryOnly = false,
    CancellationToken ct = default)
{
    var p = new JObject();
    if (categoryName != null) p["categoryName"] = categoryName;
    if (scheduleType != null) p["scheduleType"] = scheduleType;
    var result = await revit.ExecuteAsync("list_schedulable_fields", p, ct);
    return ToolResponseShaper.Shape("list_schedulable_fields", result, compact, summaryOnly).ToString();
}
```

```csharp
// src/RevitCortex.Server/Tools/ElementTools.cs
[McpServerTool(Name = "get_room_openings"), Description("Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName.")]
public static async Task<string> GetRoomOpenings(
    RevitConnectionManager revit,
    [Description("Room element IDs to query")] long[]? roomIds = null,
    [Description("Room numbers to query")] string[]? roomNumbers = null,
    [Description("Level name filter")] string? levelName = null,
    [Description("Element type: doors | windows | both. Default: both")] string? elementType = null,
    [Description("Include room parameters in response. Default: false")] bool? includeRoomParams = null,
    [Description("Include opening element parameters in response. Default: false")] bool? includeElementParams = null,
    [Description("Specific parameter names to extract")] string[]? parameterNames = null,
    [Description("Max elements per room. Default: 100")] int? maxElementsPerRoom = null,
    [Description("Return a compact payload. Default: false")] bool compact = false,
    [Description("Return counts without nested opening arrays. Default: false")] bool summaryOnly = false,
    CancellationToken ct = default)
{
    var p = new JObject();
    if (roomIds != null) p["roomIds"] = new JArray(roomIds.Cast<object>().ToArray());
    if (roomNumbers != null) p["roomNumbers"] = new JArray(roomNumbers);
    if (levelName != null) p["levelName"] = levelName;
    if (elementType != null) p["elementType"] = elementType;
    if (includeRoomParams != null) p["includeRoomParams"] = includeRoomParams;
    if (includeElementParams != null) p["includeElementParams"] = includeElementParams;
    if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
    if (maxElementsPerRoom != null) p["maxElementsPerRoom"] = maxElementsPerRoom;
    var result = await revit.ExecuteAsync("get_room_openings", p, ct);
    return ToolResponseShaper.Shape("get_room_openings", result, compact, summaryOnly).ToString();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter FullyQualifiedName~ServerToolContractTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Server/Tools/ElementTools.cs src/RevitCortex.Server/Tools/ProjectTools.cs src/RevitCortex.Tests/Server/ServerToolContractTests.cs
git commit -m "feat: expose compact flags on highest-cost MCP wrappers"
```

### Task 4: Regenerate Schemas And Document The Low-Token Paths

**Files:**
- Modify: `CLAUDE.md`
- Modify: `tool-schemas.txt`

- [ ] **Step 1: Write the failing documentation delta**

```md
<!-- add to CLAUDE.md Token Optimization section -->
- `get_current_view_elements`: prefer `modelCategoryList` / `annotationCategoryList`; use legacy `categoryFilter` only for backward compatibility.
- `get_schedule_data`: always set `maxRows` for inspection workflows; do not pull the default 500 rows unless exporting.
- `get_available_family_types`: use `compact: true` for discovery; avoid full rows unless a type ID is needed.
- `list_schedulable_fields`: use `summaryOnly: true` when you only need field names.
- `get_room_openings`: use `summaryOnly: true` for per-room counts before requesting nested door/window detail.
```

- [ ] **Step 2: Run schema generation and inspect diff**

Run: `node server/generate-tool-schemas-csharp.mjs`

Expected: `tool-schemas.txt` updates to include `compact`, `summaryOnly`, `modelCategoryList`, `annotationCategoryList`, and `maxRows` in the relevant signatures.

- [ ] **Step 3: Write minimal documentation implementation**

```md
<!-- CLAUDE.md -->
**High-cost discovery tools**:
- `get_available_family_types` -> default to `compact: true` for browsing
- `list_schedulable_fields` -> default to `summaryOnly: true` for schema discovery
- `get_room_openings` -> default to `summaryOnly: true` for room counts, then re-run with detail only for the chosen room(s)

**Wrapper alignment**:
- `get_current_view_elements` now honors `modelCategoryList` / `annotationCategoryList`
- `get_schedule_data` now supports `maxRows`
- `workflow_model_audit` now honors `includeWarnings`, `includeFamilies`, and `maxWarnings`
```

- [ ] **Step 4: Run verification**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`

Expected: PASS

Run: `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md tool-schemas.txt
git commit -m "docs: document token-safe MCP usage patterns"
```

## Self-Review

- Spec coverage: this plan covers the three highest-value safe interventions identified in analysis: wrapper alignment, additive compaction, and docs/schema propagation. It deliberately excludes risky default tightening for `get_linked_elements`, `get_warnings`, and `get_room_openings`; those belong in a separate follow-up once usage data is re-checked after compact mode ships.
- Placeholder scan: no `TODO`, `TBD`, or implicit “write tests later” steps remain.
- Type consistency: wrapper methods, test names, and file paths are consistent across tasks.

Plan complete and saved to `docs/superpowers/plans/2026-04-24-token-optimization-safe-first.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**

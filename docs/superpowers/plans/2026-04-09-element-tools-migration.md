# Element Tools Migration — Batch 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the first 3 element tools from the fork to RevitCortex, establishing the migration pattern for all remaining tools.

**Architecture:** Each tool implements `ICortexTool`, receives `CortexSession`, returns `CortexResult<T>`. A new `RevitThreadDispatcher` ensures all tool execution happens on Revit's main thread via ExternalEvent. Tools access `Document` through `session.Store.Get<object>("activeDocument")`. Each tool has a matching TypeScript Zod schema + MCP registration.

**Tech Stack:** C# (Revit API, Newtonsoft.Json), TypeScript (Zod, MCP SDK)

**Fork reference (read-only):** `C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit`

---

## Critical: Revit Threading Model

The fork uses `IExternalEventHandler` + `ManualResetEvent` per command to marshal work onto Revit's main thread. In RevitCortex, `SocketService` calls `CortexRouter.Route()` from a background thread, but Revit API calls **must** run on the UI thread.

**Solution:** Add a `RevitThreadDispatcher` in the Plugin layer that wraps tool execution in a single shared `ExternalEvent`. The router calls `dispatcher.Execute(tool, input, session)` which:
1. Queues the work
2. Raises the ExternalEvent
3. Waits via ManualResetEvent
4. Returns the CortexResult

This way, `ICortexTool.Execute()` always runs on Revit's main thread — tools never worry about threading.

---

## File Map

### Infrastructure (Plugin)

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Plugin/Threading/RevitThreadDispatcher.cs` | ExternalEvent + ManualResetEvent, marshals tool execution to UI thread |
| `src/RevitCortex.Plugin/Threading/ToolExecutionHandler.cs` | IExternalEventHandler that runs ICortexTool.Execute() |
| `src/RevitCortex.Plugin/CortexRouter.cs` | **Modify** — use dispatcher for tool execution |
| `src/RevitCortex.Plugin/RevitCortexApp.cs` | **Modify** — create dispatcher with UIApplication |

### Migrated Tools (Tools project)

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Tools/Elements/GetElementParametersTool.cs` | Read parameters from elements |
| `src/RevitCortex.Tools/Elements/AIElementFilterTool.cs` | Filter elements by category/type/params |
| `src/RevitCortex.Tools/Elements/SetElementParametersTool.cs` | Set parameter values on elements |

### TypeScript (server)

| File | Responsibility |
|------|---------------|
| `server/src/schemas/elements.ts` | Zod schemas for all 3 element tools |
| `server/src/tools/get_element_parameters.ts` | MCP registration |
| `server/src/tools/ai_element_filter.ts` | MCP registration |
| `server/src/tools/set_element_parameters.ts` | MCP registration |
| `server/src/tools/register.ts` | **Modify** — add 3 new registrations |

---

## Task 1: RevitThreadDispatcher — thread-safe tool execution

**Files:**
- Create: `src/RevitCortex.Plugin/Threading/ToolExecutionHandler.cs`
- Create: `src/RevitCortex.Plugin/Threading/RevitThreadDispatcher.cs`
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs`
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs`

- [ ] **Step 1: Write ToolExecutionHandler.cs**

```csharp
using System;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

    public ICortexTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public CortexSession? PendingSession { get; set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        try
        {
            if (PendingTool == null || PendingInput == null || PendingSession == null)
            {
                Result = CortexResult<object>.Fail(
                    CortexErrorCode.Unknown, "No pending tool execution");
                return;
            }

            Result = PendingTool.Execute(PendingInput, PendingSession);
        }
        catch (Exception ex)
        {
            Result = CortexResult<object>.Fail(
                CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
        }
        finally
        {
            _resetEvent.Set();
        }
    }

    public void PrepareExecution(ICortexTool tool, JObject input, CortexSession session)
    {
        PendingTool = tool;
        PendingInput = input;
        PendingSession = session;
        Result = null;
        _resetEvent.Reset();
    }

    public bool WaitForCompletion(int timeoutMs = 120000)
    {
        return _resetEvent.WaitOne(timeoutMs);
    }

    public string GetName() => "RevitCortex Tool Execution";
}
```

- [ ] **Step 2: Write RevitThreadDispatcher.cs**

```csharp
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class RevitThreadDispatcher
{
    private readonly ToolExecutionHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private readonly object _lock = new object();

    public RevitThreadDispatcher(ToolExecutionHandler handler, ExternalEvent externalEvent)
    {
        _handler = handler;
        _externalEvent = externalEvent;
    }

    /// <summary>
    /// Execute a tool on Revit's main thread. Blocks the calling thread until completion.
    /// </summary>
    public CortexResult<object> Execute(ICortexTool tool, JObject input, CortexSession session,
        int timeoutMs = 120000)
    {
        lock (_lock)
        {
            _handler.PrepareExecution(tool, input, session);

            var raiseResult = _externalEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Revit rejected the event request: {raiseResult}",
                    suggestion: "Revit may be busy with another operation. Try again.");
            }

            if (!_handler.WaitForCompletion(timeoutMs))
            {
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Tool '{tool.Name}' timed out after {timeoutMs}ms",
                    suggestion: "The operation took too long. Try with fewer elements.");
            }

            return _handler.Result ?? CortexResult<object>.Fail(
                CortexErrorCode.Unknown, "No result from tool execution");
        }
    }
}
```

- [ ] **Step 3: Modify CortexRouter.cs — add dispatcher**

Add a `RevitThreadDispatcher?` field and use it in `Route()`:

```csharp
// Add field
private RevitThreadDispatcher? _dispatcher;

// Add method to set dispatcher (called by RevitCortexApp after creation)
public void SetDispatcher(RevitThreadDispatcher dispatcher)
{
    _dispatcher = dispatcher;
}
```

Change the `Route()` method's tool execution line from:
```csharp
return tool.Execute(input, _session);
```
to:
```csharp
if (_dispatcher != null)
    return _dispatcher.Execute(tool, input, _session);
else
    return tool.Execute(input, _session);  // fallback for testing without Revit
```

- [ ] **Step 4: Modify RevitCortexApp.cs — create dispatcher**

In `OnStartup`, after creating the router, add:

```csharp
// Create thread dispatcher for Revit main thread execution
var executionHandler = new ToolExecutionHandler();
var externalEvent = ExternalEvent.Create(executionHandler);
var dispatcher = new RevitThreadDispatcher(executionHandler, externalEvent);
_router.SetDispatcher(dispatcher);
```

Add the required using:
```csharp
using RevitCortex.Plugin.Threading;
```

- [ ] **Step 5: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

- [ ] **Step 6: Run existing tests (should still pass)**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

Expected: 19 tests pass. Router tests still work because dispatcher is optional (null fallback).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add RevitThreadDispatcher for thread-safe tool execution on Revit UI thread"
```

---

## Task 2: Migrate get_element_parameters (C#)

**Files:**
- Create: `src/RevitCortex.Tools/Elements/GetElementParametersTool.cs`

**Fork reference:** Read `C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit\commandset\Services\GetElementParametersEventHandler.cs` for the Revit API logic.

- [ ] **Step 1: Write GetElementParametersTool.cs**

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetElementParametersTool : ICortexTool
{
    public string Name => "get_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required and cannot be empty",
                suggestion: "Provide an array of Revit element IDs, e.g. {\"elementIds\": [606873]}");

        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? true;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();

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
                results.Add(new
                {
                    elementId = id,
                    error = $"Element {id} not found"
                });
                continue;
            }

            var parameters = new List<object>();

            // Instance parameters
            foreach (Parameter param in element.Parameters)
            {
                parameters.Add(ExtractParameter(param, false));
            }

            // Type parameters
            if (includeTypeParams)
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeElement = doc.GetElement(typeId);
                    if (typeElement != null)
                    {
                        foreach (Parameter param in typeElement.Parameters)
                        {
                            parameters.Add(ExtractParameter(param, true));
                        }
                    }
                }
            }

            results.Add(new
            {
#if REVIT2024_OR_GREATER
                elementId = element.Id.Value,
#else
                elementId = element.Id.IntegerValue,
#endif
                elementName = element.Name,
                category = element.Category?.Name,
                parameters
            });
        }

        return CortexResult<object>.Ok(new
        {
            message = $"Retrieved parameters for {results.Count} elements",
            elements = results
        });
    }

    private static object ExtractParameter(Parameter param, bool isType)
    {
        var prefix = isType ? "[Type] " : "";
        object? value = null;

        if (param.HasValue)
        {
            value = param.StorageType switch
            {
                StorageType.String => param.AsString(),
                StorageType.Integer => param.AsInteger(),
                StorageType.Double => param.AsDouble(),
#if REVIT2024_OR_GREATER
                StorageType.ElementId => param.AsElementId().Value,
#else
                StorageType.ElementId => param.AsElementId().IntegerValue,
#endif
                _ => param.AsValueString()
            };
        }

        return new
        {
            name = prefix + (param.Definition?.Name ?? "Unknown"),
            value,
            hasValue = param.HasValue,
            isReadOnly = param.IsReadOnly,
            storageType = param.StorageType.ToString()
        };
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: migrate get_element_parameters tool from fork"
```

---

## Task 3: Migrate ai_element_filter (C#)

**Files:**
- Create: `src/RevitCortex.Tools/Elements/AIElementFilterTool.cs`

**Fork reference:** Read `C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit\commandset\Services\AIElementFilterEventHandler.cs` for the Revit API logic. Also read `commandset/Models/Common/FilterSetting.cs` for the input model.

- [ ] **Step 1: Write AIElementFilterTool.cs**

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

public class AIElementFilterTool : ICortexTool
{
    public string Name => "ai_element_filter";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // The fork wraps params in a "data" object
        var data = input["data"] as JObject ?? input;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var filterCategory = data["filterCategory"]?.ToString();
        var includeTypes = data["includeTypes"]?.Value<bool>() ?? false;
        var includeInstances = data["includeInstances"]?.Value<bool>() ?? true;
        var maxElements = data["maxElements"]?.Value<int>() ?? 100;

        try
        {
            var collector = new FilteredElementCollector(doc);

            // Apply category filter
            if (!string.IsNullOrEmpty(filterCategory))
            {
                if (Enum.TryParse<BuiltInCategory>(filterCategory, out var bic))
                {
                    collector = collector.OfCategory(bic);
                }
                else
                {
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unknown category: {filterCategory}",
                        suggestion: "Use OST_* codes like OST_Walls, OST_Doors, OST_Rooms");
                }
            }

            // Apply instance/type filter
            if (includeInstances && !includeTypes)
                collector = collector.WhereElementIsNotElementType();
            else if (includeTypes && !includeInstances)
                collector = collector.WhereElementIsElementType();

            var elements = collector.ToElements();

            // Apply max limit
            var limited = elements.Take(maxElements).ToList();

            var results = new List<object>();
            foreach (var elem in limited)
            {
#if REVIT2024_OR_GREATER
                var elemId = elem.Id.Value;
#else
                var elemId = (long)elem.Id.IntegerValue;
#endif
                results.Add(new
                {
                    elementId = elemId,
                    name = elem.Name,
                    category = elem.Category?.Name,
                    typeName = doc.GetElement(elem.GetTypeId())?.Name
                });
            }

            // Cache results in session for follow-up operations
            var ids = results.Select(r => ((dynamic)r).elementId).Cast<long>().ToArray();
            session.Store.Set("lastFilterResults", ids);

            return CortexResult<object>.Ok(new
            {
                message = $"Found {elements.Count} elements, returning {results.Count}",
                totalCount = elements.Count,
                returnedCount = results.Count,
                elements = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Filter failed: {ex.Message}");
        }
    }
}
```

Note: This is a simplified version. The fork's `AIElementFilterEventHandler` has additional filtering (by family symbol, spatial bounds, view visibility). The subagent implementer should read the fork EventHandler and port the additional filters that are relevant. The core pattern above is correct — expand it with the fork's additional filter branches.

- [ ] **Step 2: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: migrate ai_element_filter tool from fork"
```

---

## Task 4: Migrate set_element_parameters (C#)

**Files:**
- Create: `src/RevitCortex.Tools/Elements/SetElementParametersTool.cs`

**Fork reference:** Read `C:\Users\luigi.dattilo\Desktop\ClaudeCode\mcp-servers-for-revit\commandset\Services\SetElementParametersEventHandler.cs` for the Revit API logic.

- [ ] **Step 1: Write SetElementParametersTool.cs**

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class SetElementParametersTool : ICortexTool
{
    public string Name => "set_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var requests = input["requests"]?.ToObject<List<SetParameterRequest>>();
        if (requests == null || requests.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "requests array is required",
                suggestion: "Provide [{\"elementId\": 123, \"parameterName\": \"Comments\", \"value\": \"test\"}]");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();
        var successCount = 0;
        var failCount = 0;

        using (var tx = new Transaction(doc, "RevitCortex: Set Parameters"))
        {
            tx.Start();

            foreach (var req in requests)
            {
#if REVIT2024_OR_GREATER
                var elementId = new ElementId(req.ElementId);
#else
                var elementId = new ElementId((int)req.ElementId);
#endif
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    results.Add(new { elementId = req.ElementId, success = false, error = "Element not found" });
                    failCount++;
                    continue;
                }

                var param = element.LookupParameter(req.ParameterName);
                if (param == null)
                {
                    // Try type parameters
                    var typeId = element.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
                        param = typeElem?.LookupParameter(req.ParameterName);
                    }
                }

                if (param == null)
                {
                    results.Add(new { elementId = req.ElementId, success = false,
                        error = $"Parameter '{req.ParameterName}' not found" });
                    failCount++;
                    continue;
                }

                if (param.IsReadOnly)
                {
                    results.Add(new { elementId = req.ElementId, success = false,
                        error = $"Parameter '{req.ParameterName}' is read-only" });
                    failCount++;
                    continue;
                }

                try
                {
                    bool set = SetParameterValue(param, req.Value);
                    if (set)
                    {
                        results.Add(new { elementId = req.ElementId, success = true,
                            parameterName = req.ParameterName });
                        successCount++;
                    }
                    else
                    {
                        results.Add(new { elementId = req.ElementId, success = false,
                            error = "Failed to set value" });
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { elementId = req.ElementId, success = false,
                        error = ex.Message });
                    failCount++;
                }
            }

            if (successCount > 0)
                tx.Commit();
            else
                tx.RollBack();
        }

        return CortexResult<object>.Ok(new
        {
            message = $"Set parameters: {successCount} succeeded, {failCount} failed",
            successCount,
            failCount,
            results
        });
    }

    private static bool SetParameterValue(Parameter param, object? value)
    {
        if (value == null) return false;

        switch (param.StorageType)
        {
            case StorageType.String:
                return param.Set(value.ToString());
            case StorageType.Integer:
                if (value is long l) return param.Set((int)l);
                if (value is int i) return param.Set(i);
                if (int.TryParse(value.ToString(), out var parsed)) return param.Set(parsed);
                return false;
            case StorageType.Double:
                if (value is double d) return param.Set(d);
                if (double.TryParse(value.ToString(), out var dp)) return param.Set(dp);
                return false;
            default:
                return false;
        }
    }

    private class SetParameterRequest
    {
        public long ElementId { get; set; }
        public string ParameterName { get; set; } = "";
        public object? Value { get; set; }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: migrate set_element_parameters tool from fork"
```

---

## Task 5: TypeScript — Zod schemas and tool registrations

**Files:**
- Create: `server/src/schemas/elements.ts`
- Create: `server/src/tools/get_element_parameters.ts`
- Create: `server/src/tools/ai_element_filter.ts`
- Create: `server/src/tools/set_element_parameters.ts`
- Modify: `server/src/tools/register.ts`

- [ ] **Step 1: Create server/src/schemas/elements.ts**

```typescript
import { z } from "zod";

export const GetElementParametersInput = z.object({
  elementIds: z
    .array(z.number())
    .min(1)
    .describe("Array of Revit element IDs to query"),
  includeTypeParameters: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include type-level parameters. Default: true"),
});

export const AIElementFilterInput = z.object({
  data: z.object({
    filterCategory: z
      .string()
      .optional()
      .describe("BuiltInCategory code, e.g. OST_Walls, OST_Doors, OST_Rooms"),
    includeTypes: z
      .boolean()
      .optional()
      .default(false)
      .describe("Include type elements. Default: false"),
    includeInstances: z
      .boolean()
      .optional()
      .default(true)
      .describe("Include instance elements. Default: true"),
    maxElements: z
      .number()
      .int()
      .optional()
      .default(100)
      .describe("Max elements to return. Default: 100"),
  }),
});

export const SetElementParametersInput = z.object({
  requests: z
    .array(
      z.object({
        elementId: z.number().describe("Revit element ID"),
        parameterName: z.string().describe("Parameter name to set"),
        value: z
          .union([z.string(), z.number(), z.boolean()])
          .describe("Value to set"),
      })
    )
    .min(1)
    .describe("Array of parameter set requests"),
});
```

- [ ] **Step 2: Create server/src/tools/get_element_parameters.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetElementParametersTool(server: McpServer): void {
  server.tool(
    "get_element_parameters",
    "Get all parameters (instance and type) of one or more Revit elements by their IDs.",
    GetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_element_parameters", args);
        });
        logToolCall({ tool: "get_element_parameters", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_element_parameters", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
```

- [ ] **Step 3: Create server/src/tools/ai_element_filter.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { AIElementFilterInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerAIElementFilterTool(server: McpServer): void {
  server.tool(
    "ai_element_filter",
    "Intelligent Revit element query. Filter by category (OST_*), type, instances. Returns element IDs, names, categories.",
    AIElementFilterInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("ai_element_filter", args);
        });
        logToolCall({ tool: "ai_element_filter", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "ai_element_filter", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
```

- [ ] **Step 4: Create server/src/tools/set_element_parameters.ts**

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SetElementParametersInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSetElementParametersTool(server: McpServer): void {
  server.tool(
    "set_element_parameters",
    "Set parameter values on one or more Revit elements. Supports string, number, and boolean values.",
    SetElementParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("set_element_parameters", args);
        });
        logToolCall({ tool: "set_element_parameters", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "set_element_parameters", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}
```

- [ ] **Step 5: Update server/src/tools/register.ts**

Add the 3 new imports and registrations:

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerSayHelloTool } from "./say_hello.js";
import { registerGetElementParametersTool } from "./get_element_parameters.js";
import { registerAIElementFilterTool } from "./ai_element_filter.js";
import { registerSetElementParametersTool } from "./set_element_parameters.js";
import { logInfo } from "../logging/logger.js";

const toolRegistrations: Array<{ name: string; register: (s: McpServer) => void }> = [
  { name: "say_hello", register: registerSayHelloTool },
  { name: "get_element_parameters", register: registerGetElementParametersTool },
  { name: "ai_element_filter", register: registerAIElementFilterTool },
  { name: "set_element_parameters", register: registerSetElementParametersTool },
];

export function registerTools(server: McpServer): void {
  for (const { name, register } of toolRegistrations) {
    try {
      register(server);
      logInfo(`Registered tool: ${name}`);
    } catch (error) {
      logInfo(`Failed to register tool ${name}: ${error}`);
    }
  }
  logInfo(`Total tools registered: ${toolRegistrations.length}`);
}
```

- [ ] **Step 6: Build TypeScript**

```bash
cd server
npm run build:check
npm run build
```

Expected: No TypeScript errors, build/index.js updated.

- [ ] **Step 7: Commit**

```bash
cd ..
git add -A
git commit -m "feat: add TypeScript MCP registrations for 3 element tools with Zod schemas"
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

All must succeed. R23 validates net48 compatibility, R25 validates net8.0.

- [ ] **Step 2: Run tests**

```bash
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj --verbosity normal
```

Expected: 19 tests pass.

- [ ] **Step 3: Build TS**

```bash
cd server && npm run build:check && npm run build
```

- [ ] **Step 4: Fix any issues and commit**

```bash
git add -A
git commit -m "fix: resolve build issues from element tools migration"
```

Only commit if changes were made.

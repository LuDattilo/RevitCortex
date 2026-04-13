# RevitCortex -- AI Assistant Guide

## Project Overview

RevitCortex is a next-generation MCP (Model Context Protocol) server for Autodesk Revit. It improves on the original mcp-servers-for-revit with typed errors, session state, and dynamic tool discovery. Tools are rewritten from scratch -- not copied from the fork reference.

## Supported Revit Versions

| Revit Version | Target Framework |
|---------------|-----------------|
| 2023          | net48           |
| 2024          | net48           |
| 2025          | net8.0-windows  |
| 2026          | net8.0-windows  |
| 2027          | net10.0-windows |

## Cross-Target Compatibility (net48 vs net8+)

R2023 and R2024 target **net48**. Code that compiles on net8+ may fail on net48. Before using any C# feature, check this list:

| Feature | net8+ | net48 | Fix |
|---------|-------|-------|-----|
| `record` types | OK | **ERROR** CS0518 (`IsExternalInit` missing) | Use `class` with readonly properties and constructor |
| `Dictionary.GetValueOrDefault()` | OK | **ERROR** CS1061 | Use `TryGetValue` with ternary |
| `init` property accessors | OK | **ERROR** CS0518 | Use `{ get; }` + constructor |
| `Index`/`Range` (`^1`, `..`) | OK | **ERROR** | Use `.Length - 1`, `.Substring()` |
| `IAsyncEnumerable<T>` | OK | **ERROR** | Not available on net48 |
| `file`-scoped types | OK | **ERROR** | Use `internal` |
| Default interface methods | OK | **ERROR** | Move to abstract class or separate method |

**Rule**: After adding/modifying any C# file, always build for BOTH `Debug R25` (net8) AND `Debug R24` (net48) before committing. A green R25 build does NOT guarantee R24 will compile.

## Project Structure

```
RevitCortex/
  RevitCortex.sln
  nuget.config
  src/
    RevitCortex.Core/         Core types (no Revit dependency)
      Discovery/                DocumentCapabilities, IDocumentAnalyzer
      Results/                  CortexResult<T>, CortexError, CortexErrorCode
      Session/                  CortexSession, ISessionStore, SessionStore
      Tools/                    ICortexTool interface
    RevitCortex.Plugin/       Revit add-in (ExternalApplication)
      Communication/            SocketService, JsonRpcModels
      Discovery/                DocumentAnalyzer, LocaleDetector
      CortexRouter.cs           Request dispatch
      RevitCortexApp.cs         Entry point
    RevitCortex.Tools/        Tool implementations
      Meta/                     SayHelloTool
    RevitCortex.Tests/        Unit tests (xUnit)
      Discovery/                DocumentCapabilitiesTests
      Results/                  CortexResultTests
      Router/                   CortexRouterTests, FakeTool, FakeAnalyzer
      Session/                  SessionStoreTests
  server/                     TypeScript MCP server (stdio transport)
    src/
      connection/               TCP connection to Plugin
      logging/                  Structured logging
      schemas/                  Zod schemas
      tools/                    Tool definitions
    index.ts                    Entry point
```

## Architecture: Layer Cake

```
MCP Server (TypeScript) -> SocketService -> CortexRouter -> ICortexTool
                                                |
                                          CortexSession
                                                |
                                    DocumentCapabilities
```

- **MCP Server (TypeScript)** -- Zod validation, JSON schema generation, stdio transport to Claude.
- **SocketService (C#)** -- TCP listener, JSON-RPC framing between TS server and C# plugin.
- **CortexRouter (C#)** -- Deserializes the JSON-RPC request, finds the matching ICortexTool by name, invokes it with CortexSession, returns CortexResult.
- **ICortexTool (C#)** -- Unified interface for all tools.
- **CortexSession (C#)** -- Shared state facade passed to every tool: session store, document capabilities, detected locale.
- **CortexResult\<T\> (C#)** -- Typed response envelope with success/error discriminator and structured error codes.
- **DocumentAnalyzer (C#)** -- Scans the active Revit document to populate DocumentCapabilities, enabling/disabling dynamic tools.

## Build Commands

### C# (from repo root)

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2025
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2024
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2023
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2026
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2027
```

### TypeScript MCP Server

```bash
cd server && npm install && npm run build
```

### Tests

```bash
dotnet test -c "Debug R25"
```

## Key Patterns

### CortexResult\<T\>

Every tool returns a typed result. Never throw exceptions or return raw strings.

```csharp
// Success
return CortexResult<object>.Ok(new { greeting = "Hello" });

// Failure with structured error
return CortexResult<object>.Fail(
    CortexErrorCode.ElementNotFound,
    "Element 12345 does not exist in the active document",
    suggestion: "Check the element ID or ensure the correct document is open");
```

Error codes: `ElementNotFound` (100), `PermissionDenied` (200), `TransactionFailed` (300), `InvalidInput` (400), `Timeout` (500), `Cancelled` (600), `Unknown` (900).

### ICortexTool

Every tool implements this interface:

```csharp
public interface ICortexTool
{
    string Name { get; }              // MCP tool name, e.g. "get_element_parameters"
    string Category { get; }          // Domain group, e.g. "Elements"
    bool RequiresDocument { get; }    // Needs an open Revit document?
    bool IsDynamic { get; }           // Only visible when capabilities match?
    CortexResult<object> Execute(JObject input, CortexSession session);
}
```

### CortexSession

Facade passed to every tool. Provides shared state without direct Revit globals.

```csharp
public class CortexSession
{
    public ISessionStore Store { get; }
    public DocumentCapabilities Capabilities { get; }
    public string DetectedLocale { get; }

    public void Reinitialize(DocumentCapabilities capabilities, string locale);
}
```

- `Store` -- key/value cache that persists across tool calls within a session.
- `Capabilities` -- discovered at document open by DocumentAnalyzer.
- `DetectedLocale` -- detected by LocaleDetector ("en", "it", "fr", "de").

### IsDynamic Convention

Tools with `IsDynamic = true` are only registered/visible when the active document has matching `DocumentCapabilities`. This keeps the tool list relevant and avoids errors from calling tools on incompatible documents.

When DocumentAnalyzer scans a document:
1. It populates `DocumentCapabilities` (present categories, worksets, phases, etc.).
2. It calls `capabilities.EnableTool("tool_name")` for each dynamic tool whose prerequisites are met.
3. CortexRouter only exposes tools where `!IsDynamic || capabilities.IsToolEnabled(tool.Name)`.

## Token Optimization

`tool-schemas.txt` in the project root contains compact one-line-per-tool signatures. Regenerate after adding/modifying tool schemas:

```bash
node server/generate-tool-schemas.mjs
```

### Fundamental Rule

> Before calling any tool, ask: **do I already have this information in the current conversation context?** If yes, do not call the tool again.

### Default Parameters to Override

RevitCortex defaults are calibrated for completeness, not efficiency. Override these when appropriate.

**get_project_info**: The **first call** of the session must be complete (all includes = true) to establish model context. Subsequent calls should filter: `{"includeLevels": false, "includeLinks": false, "includePhases": false, "includeWorksets": false}`.

**get_element_parameters**: Leave `includeTypeParameters` at `true` (default) for most workflows. Override to `false` only for counting/statistics tasks where only instance data is needed.

**get_warnings**: Never use default 500 in normal operations. Use `maxWarnings: 10` for quick checks, `maxWarnings: 50` for category analysis.

**export_room_data**: Use `maxResults: 20` unless the full building is needed.

**audit_families**: Always filter by category in daily operations: `{"categoryFilter": "OST_Doors", "includeUnused": false}`.

**bulk_modify_parameter_values**: With `dryRun: true`, read only `modifiedCount` and `skippedCount` from the response, not the full element list. Then execute with `dryRun: false`.

**get_linked_elements**: Specify `categories` and `maxElements` to limit response. The `parameterNames` parameter is **additive**: without it the tool returns only `elementId`, `category`, `name` -- not all parameters. Add `parameterNames` only when specific linked element data is needed.

**lines_per_view_count**: Always use `threshold >= 20` on models with >100 views. The tool has an automatic cap at 300 views.

### Tool Selection Hierarchy

When multiple tools can achieve the same goal, use the most targeted one.

**Model status** (ascending token cost):
1. `check_model_health` -- score + issues only (~200 tok)
2. `analyze_model_statistics` with `compact: true` (~400 tok)
3. `workflow_model_audit` with filters (~800 tok)
4. `workflow_model_audit` full (~3000 tok)

**Finding elements**:
- Simple filter (1 parameter, exact value) -> `export_elements_data` with `filterParameterName`/`filterValue`
- Complex filter (ranges, AND/OR, multi-parameter) -> `ai_element_filter`
- Current view elements -> `get_current_view_elements` with `fields` and `limit`
- Elements in a room/volume -> `get_elements_in_spatial_volume` with `categoryFilter` and reduced `maxElementsPerVolume`

**Modifying parameters**:
- 1 element, 1-3 parameters -> `set_element_parameters`
- N elements, same parameter/value -> `bulk_modify_parameter_values`
- N elements, different parameters each -> `sync_csv_parameters`
- Copy parameters between elements -> `match_element_properties` (always specify `parameterNames`)

**Clash detection**:
- `clash_detection` -> quick check with count and ID list
- `workflow_clash_review` -> when a 3D view with automatic section box is needed for visual review

On architectural models, columns are `OST_Columns`, not `OST_StructuralColumns`. Always specify the correct category for the model type.

### Session Patterns

Input token cost grows with every previous response in context. A session with 30 tool calls on a large model can accumulate 300,000+ context tokens from tool responses alone.

**Session A -- Morning Check** (open and close):
1. `check_model_health`
2. `get_warnings` with `maxWarnings: 10`
3. Optional: `clash_detection` on specific discipline pair
-> Close session, record only relevant numbers.

**Session B -- Parameter Updates** (open and close):
1. `export_elements_data` with specific filter
2. `bulk_modify_parameter_values` or `sync_csv_parameters`
3. Spot check with `get_element_parameters` on 1-2 sample elements
-> Close session.

**Session C -- Documentation / Export** (open and close):
1. `workflow_data_roundtrip` or `export_to_excel`
2. `create_preset_schedule` if needed
3. `export_schedule` on specific scheduleId
-> Close session.

**Session D -- Complex BIM Operations** (open during work):
Use a dedicated session per distinct BIM task. Do not mix QA tasks with authoring in the same long session.

**Dirty context rule**: If the context already has responses with >50 lines of JSON data (export room, schedule, audit families etc.) and you need to start an unrelated task: **open a new conversation**. The cost of restarting is near zero. The cost of dragging 200K token context for every new turn is real.

### Anti-Waste Patterns

**Avoid**: calling `get_project_info` without filters on subsequent calls; `ai_element_filter` with `maxElements: 1000` without need; global `audit_families` to find a single category; reading the full dryRun element list instead of just the counters.

**Correct**: first call complete then filtered; `export_elements_data` with categories/filter/parameterNames/maxElements; `audit_families` with specific `categoryFilter`; dryRun -> read only `modifiedCount`/`skippedCount` -> execute.

**get_compound_structure**: `structuralLayerIndex: -1` on rainscreen or non-structural walls is architecturally correct, not a data error. Do not re-call the tool to verify.

### Tool Behavioral Notes

| Tool | Limitation | Correct Behavior |
|------|-----------|-----------------|
| `tag_rooms` / `tag_walls` | Operates only on the active Revit view | Activate the correct view before calling |
| `color_elements` | Requires a model view (not Sheet) | Verify active view with `get_current_view_info` first |
| `create_dimensions` | Z must exactly match the level elevation | Use elevation from `get_project_info` levels |
| `set_element_phase` | Available only on models with phases (`doc.Phases > 0`) | Check `phases` in `get_project_info`, NOT `isWorkshared` -- phases are independent of worksharing |
| `create_grid` | Label ignored if already exists in model | Use non-conflicting labels; the tool adds a warning in the response |
| `lines_per_view_count` | Heavy on models with many views | Always use `threshold >= 20`; tool has automatic 300-view cap |

### Token Estimates by Task Type

| Task | Tool calls | Estimated response tokens |
|------|-----------|--------------------------|
| Quick morning check | 2-3 | 500-800 |
| Parameter update 50 elements | 3-4 | 1,000-2,000 |
| Clash detection 2 disciplines | 2 | 400-600 |
| Export + re-import data | 3 | 800-1,500 |
| Create 5 sheets + viewports | 4-6 | 600-1,000 |
| Family audit specific category | 2 | 400-700 |
| Full model cleanup | 6-8 | 2,000-4,000 |
| Full bulk test | 130+ | 55,000+ |

When cumulative session tokens exceed ~15,000, consider opening a new conversation for subsequent tasks.

## Confirmation Dialogs for Destructive Operations

Destructive tools (delete, purge, rename, modify parameters, etc.) show a native Revit TaskDialog before executing. This is implemented via `CortexSession.RequestConfirmation(action, count)`.

When the user cancels, tools return a `CortexResult.Fail` with `CortexErrorCode.Cancelled`:
```json
{"success": false, "error": {"code": "Cancelled", "message": "Operation cancelled by user"}}
```

**Tools with confirmation:** delete_element, delete_selection, delete_material, purge_unused, wipe_empty_tags, set_element_parameters, set_compound_structure, batch_rename, override_graphics, set_element_phase, set_element_workset, change_element_type, load_family.

When adding new destructive tools, always call `session.RequestConfirmation("action_verb", elementCount)` before the Transaction.

## UI Components

The plugin includes a Revit ribbon panel and dockable chat panel:

- **Commands/** -- IExternalCommand classes: ToggleConnection, ToggleCortexPanel, OpenSettings
- **UI/CortexPanel** -- WPF dockable chat panel with prompt chips, chat export, status indicator
- **UI/CortexChatClient** -- Anthropic API client (tool use loop, thinking, retry on 429/529)
- **UI/SettingsWindow** -- General settings, API key, tools enable/disable
- **UI/IconFactory** -- Generates ribbon icons programmatically (no PNG files)
- **UI/ConfirmationHelper** -- TaskDialog for destructive operations

The server is **off by default** -- user must click "Cortex Switch" in the ribbon to start it.

Settings are stored in `~/.revitcortex/settings.json` (port, log level, model, disabled tools).

## Deploy

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

Deploys Plugin + Tools to `C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex\`. Restart Revit after deploy.

## IMPORTANT: Detect Revit Language First

Revit localizes category and parameter names based on installation language (EN, IT, FR, DE, etc.). **Do NOT assume the language.** At the start of every session, call `get_element_parameters` on any element (or `get_project_info`) and check the parameter names in the response:

- If you see "Level", "Comments", "Type Name" -- English
- If you see "Livello", "Commenti", "Nome del tipo" -- Italiano
- If you see "Niveau", "Commentaires", "Nom du type" -- Francais
- If you see "Ebene", "Kommentare", "Typname" -- Deutsch

Then use the corresponding column from the locale mapping tables below for ALL subsequent tool calls.

## Tool-Specific Corrections

### `ai_element_filter`
- **REQUIRED:** `data` wrapper object -- do NOT pass filter params at root level
- Example: `{"data": {"filterCategory": "OST_StructuralFraming", "includeInstances": true, "maxElements": 5}}`

### `filter_by_parameter_value`
- When filtering on type parameters (e.g., type name), set `parameterType: "type"`
- Default `parameterType: "both"` may NOT resolve type-level string parameters correctly
- Instance-level type name parameter often has `hasValue: false` -- always use `parameterType: "type"` for type name filtering

### `color_elements`
- Uses **localized display category names** (depends on Revit language)
- Only works on views that **contain visible elements** of that category
- Will fail on DrawingSheet/Cover Sheet views -- switch to a FloorPlan or 3D view first

### `get_element_parameters`
- `elementIds` must be an array of numbers: `[606873]` not `"[606873]"`
- Type parameters are prefixed with `[Type]` in the response

### `create_view`
- Can timeout when executed in parallel with other write operations
- Best to run alone or with minimal concurrent writes

### `operate_element`
- **REQUIRED:** `data` wrapper object
- Example: `{"data": {"elementIds": [123], "action": "select"}}`

### `send_code_to_revit`
- Document variable is `document` (not `doc`, `Doc`, or `uidoc`)
- For UIDocument: `new UIDocument(document)`
- ElementId uses `.Value` on R2024+ and `.IntegerValue` on R2023

## Handling User Input Situations

When a tool requires user selection or interaction that cannot be automated:
1. **Never block** -- if the user needs to select elements, instruct them and wait for the next message
2. **Use `get_selected_elements`** -- if the user says "selected elements", call this first. If empty, ask them to select
3. **Cancelled operations** -- if a tool returns `cancelled: true`, acknowledge it and ask if they want to retry
4. **dryRun pattern** -- for destructive operations, run with `dryRun: true` first to preview the results, then with `dryRun: false` to execute. The confirmation dialog will ask the user

## OST Category Codes (language-independent)

| Code | Elements |
|------|----------|
| `OST_StructuralFraming` | Beams, joists, braces |
| `OST_StructuralColumns` | Columns |
| `OST_StructuralFoundation` | Foundations |
| `OST_Walls` | Walls |
| `OST_Floors` | Floors |
| `OST_Doors` | Doors |
| `OST_Windows` | Windows |
| `OST_Rooms` | Rooms |
| `OST_GenericModel` | Generic models |
| `OST_Ceilings` | Ceilings |
| `OST_Roofs` | Roofs |
| `OST_Stairs` | Stairs |
| `OST_Railings` | Railings |
| `OST_CurtainWallPanels` | Curtain panels |
| `OST_CurtainWallMullions` | Mullions |

## Revit Locale Mappings

### Category Display Names

| English | Italiano | Francais | Deutsch |
|---------|----------|----------|---------|
| Structural Framing | Telaio strutturale | Ossature | Tragwerk |
| Structural Columns | Pilastri strutturali | Poteaux porteurs | Stutzen |
| Walls | Muri | Murs | Wande |
| Floors | Pavimenti | Sols | Geschossdecken |
| Doors | Porte | Portes | Turen |
| Windows | Finestre | Fenetres | Fenster |
| Rooms | Vani | Pieces | Raume |
| Generic Models | Modelli generici | Modeles generiques | Allgemeine Modelle |
| Sheets | Tavole | Feuilles | Planlisten |
| Levels | Livelli | Niveaux | Ebenen |
| Grids | Griglie | Quadrillages | Raster |
| Materials | Materiali | Materiaux | Materialien |

### Parameter Names

| English | Italiano | Francais | Deutsch |
|---------|----------|----------|---------|
| Type Name | Nome del tipo | Nom du type | Typname |
| Family Name | Nome famiglia | Nom de famille | Familienname |
| Family and Type | Famiglia e tipo | Famille et type | Familie und Typ |
| Level | Livello | Niveau | Ebene |
| Comments | Commenti | Commentaires | Kommentare |
| Mark | Contrassegno | Repere | Kennzeichen |
| Phase Created | Fase di creazione | Phase de creation | Erstellungsphase |
| Length | Lunghezza | Longueur | Lange |
| Volume | Volume | Volume | Volumen |
| Area | Area | Superficie | Flache |
| Description | Descrizione | Description | Beschreibung |

## Performance Notes

- **Read operations**: Safe to run 5+ in parallel
- **Write operations**: Max 3-4 in parallel to avoid timeouts
- **Heavy queries** (analyze_model_statistics, purge_unused): Run individually or max 2 concurrent
- **create_view 3D**: Particularly heavy -- avoid running with other writes

## Security Requirements (NFR)

See `docs/SECURITY.md` for the full security analysis. The following are mandatory non-functional requirements.

### Sandbox for `send_code_to_revit`

Code submitted via `send_code_to_revit` is validated at runtime before execution. The following namespace patterns are **prohibited** and will cause the tool to return `CortexErrorCode.PermissionDenied`:

- `System.IO` -- filesystem access
- `System.Net` -- network access
- `System.Diagnostics.Process` -- process spawning
- `Microsoft.Win32` -- registry access
- `System.Reflection.Emit` -- dynamic code generation
- `System.Runtime.InteropServices` -- native interop

The sandbox is implemented in `CodeSandbox.Validate(string code)` in RevitCortex.Core. All tools that execute user-provided code MUST call this before execution. The sandbox can be bypassed only by disabling `send_code_to_revit` entirely in settings.

### Audit Log

Every tool execution is logged to `~/.revitcortex/audit.jsonl` with:

```json
{"ts": "ISO8601", "tool": "tool_name", "input_summary": "...", "result": "ok|fail", "error_code": null, "elements_affected": 0}
```

The audit log is append-only. Implemented in `AuditLogger` in RevitCortex.Core. CortexRouter calls `AuditLogger.Log()` after every tool invocation. Log rotation is not automatic -- the file grows indefinitely (acceptable for local use).

### Read-Only Mode

When `readOnlyMode: true` is set in `~/.revitcortex/settings.json`, CortexRouter rejects all write tools with `CortexErrorCode.PermissionDenied`. This is enforced in the router via `CortexRouter.ReadOnlyMode`.

Read-only classification uses a **naming convention** (no interface change needed):

- **Read-only prefixes**: `get_`, `list_`, `find_`, `analyze_`, `check_`, `measure_`, `audit_`, `export_`, `say_hello`, `clash_detection`, `lines_per_view_count`
- **Everything else**: considered a write tool, blocked in read-only mode

The check is in `CortexRouter.IsReadOnlyTool(string toolName)` (public static, testable).

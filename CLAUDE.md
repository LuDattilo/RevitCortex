# RevitCortex -- AI Assistant Guide

## AI Skill Router

Per task operativi BIM o di sviluppo C#, la knowledge base è organizzata in `ai-skills/revitcortex/`.
Il router `ai-skills/revitcortex/SKILL.md` indica quali reference caricare per ogni tipo di richiesta.

Quando questo CLAUDE.md cresce, le regole specifiche dovrebbero migrare nei reference della skill:
- Workflow BIM → `ai-skills/revitcortex/references/operator_*.md`
- Pattern sviluppo C# → `ai-skills/revitcortex/references/developer_*.md`
- Mappa fonti → `ai-skills/revitcortex/references/index_41_Workflow_Source_Map.md`

Questo CLAUDE.md resta la fonte canonica per regole globali e build/release matrix.

Flussi operativi collaudati: vedere WORKFLOWS.md

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

### R27 build requires .NET 10+ SDK

Revit 2027 targets `net10.0-windows7.0`. Building `Debug R27`/`Release R27` requires a .NET SDK ≥ 10 on the build machine; with only SDK 8 the build fails with `NETSDK1045`. `global.json` pins to SDK 8 with `rollForward: latestMajor`, so SDK 11 preview (or any future SDK) is accepted automatically when SDK 10 is absent. Runtime: end-user PCs need .NET 10 runtime to load the R27 plugin (Revit 2027 itself ships .NET 10).

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
  src/RevitCortex.Server/     C# MCP server (stdio transport)
    Program.cs                  Entry point (MCP hosting)
    Connection/RevitBridge.cs   TCP bridge to Plugin
    Tools/                      157 tool definitions (9 files)
```

## Architecture: Layer Cake

```
MCP Server (C#) -> SocketService -> CortexRouter -> ICortexTool
                                                |
                                          CortexSession
                                                |
                                    DocumentCapabilities
```

- **MCP Server (C#)** -- ModelContextProtocol SDK, stdio transport to Claude. Located in `src/RevitCortex.Server/`.
- **SocketService (C#)** -- TCP listener, JSON-RPC framing between C# MCP server and C# plugin.
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

### C# MCP Server

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

### Tests

Target the test project with an explicit configuration — **do NOT run `dotnet test` on the solution**:

```bash
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```

Solution-wide `dotnet test` fails NuGet restore with `'.*' is not a valid version string`, because the Plugin/Tools csprojs use `Version="$(RevitVersion).*"` and `$(RevitVersion)` is only set inside the configuration-conditional `PropertyGroup`s. A solution restore leaves it empty → literal `.*`. Targeting the csproj with `-c "Debug R25"` populates `$(RevitVersion)=2025`.

Scope to a subset with `--filter "FullyQualifiedName~CortexSessionConfirmationTests"`.

**RevitAPIUI-dependent tests**: tests that load a Plugin type implementing `IExternalEventHandler` or taking `UIApplication` (e.g. `ToolExecutionHandlerTests`) need the real `RevitAPIUI.dll`, which the Nice3point reference-only NuGets do not copy to test output. Mark them `[RequiresRevitApiFact]` (subclass of `[Fact]` in `src/RevitCortex.Tests/RequiresRevitApiFactAttribute.cs`) so they **skip** rather than fail where Revit is absent. Expected clean result without Revit installed: 221 passed / 1 skipped / 0 failed.

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
node server/generate-tool-schemas-csharp.mjs
```

### Compact Responses (per-call)

Some HIGH-payload tools accept a `compact: true` flag that strips per-item metadata to reduce tokens. Counters, identifiers and item counts are always preserved (safety contract enforced by `ToolResponseShaper`).

Tools that accept `compact`: `get_element_parameters`, `get_available_family_types`, `audit_families`, `list_schedulable_fields`, `get_room_openings`, `get_shared_parameters`, `get_linked_file_instances`, `get_elements_in_spatial_volume`, `get_materials`, `export_room_data`, `ifc_list_export_configurations`, `ifc_analyze_rebuildability`, `ifc_list_rebuild_candidates`, `workflow_model_audit`. Default is `false` (full payload).

### Fundamental Rule

> Before calling any tool, ask: **do I already have this information in the current conversation context?** If yes, do not call the tool again.

> Quando in una sessione si trova un flusso che funziona e non e gia in WORKFLOWS.md, aggiornare WORKFLOWS.md prima di chiudere la sessione -- questo vale anche se il flusso sembra ovvio.

### Default Parameters to Override

RevitCortex defaults are calibrated for completeness, not efficiency. Override these when appropriate.

**get_project_info**: The **first call** of the session must be complete (all includes = true) to establish model context. Subsequent calls should filter: `{"includeLevels": false, "includeLinks": false, "includePhases": false, "includeWorksets": false}`.

**get_element_parameters**: Leave `includeTypeParameters` at `true` (default) for most workflows. Override to `false` only for counting/statistics tasks where only instance data is needed. Use `compact: true` to strip metadata (hasValue/isReadOnly/isShared/storageType/groupName) and skip empty params — typical 60-70% payload reduction.

**get_warnings**: Never use default 500 in normal operations. Use `maxWarnings: 10` for quick checks, `maxWarnings: 50` for category analysis.

**export_room_data**: Use `maxResults: 20` unless the full building is needed.

**audit_families**: Always filter by category in daily operations: `{"categoryFilter": "OST_Doors", "includeUnused": false}`. Use `compact: true` to drop audit-only booleans (isInPlace/isEditable/isUnused/kind) — typical 45-50% payload reduction.

**bulk_modify_parameter_values**: With `dryRun: true`, read only `modifiedCount` and `skippedCount` from the response, not the full element list. Then execute with `dryRun: false`.

**get_linked_elements**: Specify `categories` and `maxElements` to limit response. The `parameterNames` parameter is **additive**: without it the tool returns only `elementId`, `category`, `name` -- not all parameters. Add `parameterNames` only when specific linked element data is needed.

**lines_per_view_count**: Single document-wide pass — safe on any model size. Detail lines are reported per view; model lines (no owner view) as one project-wide count. Use `threshold` only to shrink the response.

**get_current_view_elements**: Prefer `modelCategoryList` / `annotationCategoryList`. Use legacy `categoryFilter` only for backward compatibility.

**get_schedule_data**: Always set `maxRows` for inspection workflows. Do not pull the default 500 rows unless exporting.

**get_available_family_types**: Use `compact: true` for discovery. Avoid full rows unless a type ID is needed. Filter via `categoryList: ["OST_Doors"]` (array, not string) and `familyNameFilter` for substring search.

**list_schedulable_fields**: Use `summaryOnly: true` when you only need field names.

**get_room_openings**: Use `summaryOnly: true` for per-room counts before requesting nested door/window detail.

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
- **Elements with empty custom parameter** -> NEVER guess parameter names. First: `get_element_parameters` on 1 sample element to discover exact names. Then: `export_elements_data` with `parameterNames` + `filter_by_parameter_value` with `condition: "is_empty"`. Do NOT use `send_code_to_revit` -- unnecessary and fragile with DLL conflicts (archintelligence, BIM360).
- **Discover custom parameter names** (WBS_*, Code_*, etc.) -> `get_element_parameters` on 1 sample element ID; never assume name format.

**Modifying parameters**:
- 1 element, 1-3 parameters -> `set_element_parameters`
- N elements, same parameter/value -> `bulk_modify_parameter_values`
- N elements, different parameters each -> `sync_csv_parameters`
- Copy parameters between elements -> `match_element_properties` (always specify `parameterNames`)

**Clash detection**:
- `clash_detection` -> quick check with count and ID list
- `workflow_clash_review` -> when a 3D view with automatic section box is needed for visual review

**High-cost discovery tools**:
- `get_available_family_types` -> default to `compact: true` for browsing
- `list_schedulable_fields` -> default to `summaryOnly: true` for schema discovery
- `get_room_openings` -> default to `summaryOnly: true` for room counts, then re-run with detail only for the chosen room(s)

**Wrapper alignment**:
- `get_current_view_elements` now honors `modelCategoryList` / `annotationCategoryList`
- `get_schedule_data` now supports `maxRows`
- `workflow_model_audit` now honors `includeWarnings`, `includeFamilies`, and `maxWarnings`

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
| `lines_per_view_count` | Model lines are not view-specific | Reported once at project level (`modelLinesInProject`), not per view; per-view counts cover detail lines only |

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

The plugin includes a Revit ribbon panel with two buttons (Cortex Switch, Settings) and a settings window for port, log level, and tool visibility.

- **Commands/** -- IExternalCommand classes: ToggleConnection, OpenSettings
- **UI/SettingsWindow** -- General settings, tools enable/disable
- **UI/IconFactory** -- Generates ribbon icons programmatically (no PNG files)
- **UI/ConfirmationHelper** -- TaskDialog for destructive operations

The server is **off by default** -- user must click "Cortex Switch" in the ribbon to start it.

Settings are stored in `~/.revitcortex/settings.json` (port, log level, disabled tools).

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

**NEVER use autonomously for bulk/batch operations.** When an operation involves many elements or would benefit from a custom script, ALWAYS ask the user first:

> "Posso usare `send_code_to_revit` per eseguire questa operazione in modo più efficiente con uno script C#, oppure preferisci che proceda con i tool standard (potrebbe richiedere più chiamate)?"

Only proceed with `send_code_to_revit` after explicit user consent. Reasons to ask rather than assume:
- Scripts bypass the native tool safety layer (dryRun, confirmation dialogs)
- DLL conflicts (archintelligence, BIM360, other add-ins) can crash `send_code_to_revit` silently
- The user may prefer full traceability via discrete tool calls

Specific guidance:
- Document variable is `document` (not `doc`, `Doc`, or `uidoc`)
- For UIDocument: `new UIDocument(document)`
- ElementId uses `.Value` on R2024+ and `.IntegerValue` on R2023

## Handling User Input Situations

When a tool requires user selection or interaction that cannot be automated:
1. **Never block** -- if the user needs to select elements, instruct them and wait for the next message
2. **Use `get_selected_elements`** -- if the user says "selected elements", call this first. If empty, ask them to select
3. **Cancelled operations** -- if a tool returns `cancelled: true`, acknowledge it and ask if they want to retry
4. **dryRun pattern** -- for destructive operations, run with `dryRun: true` first to preview the results, then with `dryRun: false` to execute. The confirmation dialog will ask the user
5. **Script escalation** -- if the task would benefit from `send_code_to_revit` (bulk ops, complex logic, 100+ elements), DO NOT switch automatically. Ask the user: propose the script approach AND the native-tool approach, explain the trade-offs, and wait for their choice. The native approach may require more tool calls but is always safer and more traceable.

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

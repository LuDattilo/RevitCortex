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

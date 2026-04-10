# RevitCortex

A next-generation **MCP (Model Context Protocol) server** for Autodesk Revit with 124 tools, typed errors, session state, dynamic tool discovery, and a built-in AI chat panel.

RevitCortex lets Claude (or any MCP-compatible LLM) read, create, modify, and analyze Revit models in real time -- from querying elements and parameters to creating views, sheets, schedules, and running full audit workflows.

## Features

- **124 MCP tools** across 14 categories: Elements, Views, Sheets, Schedules, Parameters, Materials, Creation, Export, Audit, Workflows, Database, Journal, Code, and Meta
- **Typed results** -- every tool returns `CortexResult<T>` with structured error codes, not raw strings
- **Session state** -- `CortexSession` persists data across tool calls within a session
- **Dynamic tools** -- tools auto-hide when the active document doesn't support them
- **Multi-locale** -- detects Revit language (EN, IT, FR, DE) and adapts parameter/category names
- **Built-in chat panel** -- WPF dockable panel with direct Anthropic API integration, thinking support, prompt chips, and chat export
- **Confirmation dialogs** -- destructive operations show a native Revit TaskDialog before executing
- **Multi-version** -- supports Revit 2023, 2024, 2025, and 2026

## Prerequisites

- **Revit** 2023, 2024, 2025, or 2026
- **.NET SDK** 8.0+ (for Revit 2025/2026) or .NET Framework 4.8 (for Revit 2023/2024)
- **Node.js** 18+ and npm
- **Claude Desktop**, **Claude Code**, or any MCP-compatible client
- (Optional) **Anthropic API key** for the built-in chat panel

## Installation

### 1. Clone the repository

```bash
git clone https://github.com/LuDattilo/RevitCortex.git
cd RevitCortex
```

### 2. Build the TypeScript MCP server

```bash
cd server
npm install
npm run build
cd ..
```

### 3. Build the C# Revit plugin

Pick your Revit version (2023/2024/2025/2026):

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

### 4. Deploy to Revit

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2025
```

This copies the plugin DLLs and `.addin` manifest to `C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex\`.

### 5. Restart Revit

After deploy, restart Revit. You'll see a **RevitCortex** tab in the ribbon with:
- **Cortex Switch** -- start/stop the MCP TCP server
- **Cortex Panel** -- open the AI chat panel
- **Settings** -- configure port, API key, model, and tool visibility

## MCP Client Configuration

### Claude Desktop

Add to your `claude_desktop_config.json` (usually at `%APPDATA%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["C:/path/to/RevitCortex/server/build/index.js"]
    }
  }
}
```

### Claude Code

Add to your project's `.mcp.json` or global MCP settings:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["C:/path/to/RevitCortex/server/build/index.js"]
    }
  }
}
```

### Important

The MCP server communicates with the Revit plugin via TCP (default port 8080). Make sure:
1. Revit is running with a document open
2. The Cortex Switch is **ON** (click it in the RevitCortex ribbon tab)
3. The MCP server is started by your client (Claude Desktop/Code handles this automatically)

## Usage

### Via MCP Client (Claude Desktop / Claude Code)

Once configured, Claude can use all 124 tools directly. Examples:

```
"Show me all walls in the model"
→ Claude calls ai_element_filter with filterCategory: "OST_Walls"

"Create a floor plan for each room"
→ Claude calls get_current_view_elements, then create_views_from_rooms

"Export all room data to Excel"
→ Claude calls export_to_excel with the room data

"Run a full model health check"
→ Claude calls workflow_model_audit
```

### Via Built-in Chat Panel

1. Click **Cortex Panel** in the Revit ribbon
2. Type your request in natural language
3. The panel calls Claude's API directly, with all Revit tools available
4. Prompt chips at the bottom offer common actions

**API Key Setup** (for chat panel only):
- Set environment variable `ANTHROPIC_API_KEY=sk-ant-...`
- Or create file `%USERPROFILE%\.claude\api_key.txt` containing your key

## Tool Categories

| Category | Tools | Examples |
|----------|-------|---------|
| **Elements** | 24 | `ai_element_filter`, `get_element_parameters`, `set_element_parameters`, `delete_element`, `color_elements` |
| **Creation** | 13 | `create_room`, `create_level`, `create_grid`, `create_floor`, `create_line_based_element` |
| **Views** | 13 | `create_view`, `duplicate_view`, `override_graphics`, `create_views_from_rooms` |
| **Sheets** | 7 | `create_sheet`, `batch_create_sheets`, `place_viewport`, `align_viewports` |
| **Schedules** | 8 | `create_schedule`, `create_preset_schedule`, `modify_schedule`, `get_schedule_data` |
| **Parameters** | 10 | `add_shared_parameter`, `bulk_modify_parameter_values`, `batch_rename`, `sync_csv_parameters` |
| **Project** | 14 | `get_project_info`, `load_family`, `manage_links`, `duplicate_system_type` |
| **Materials** | 9 | `get_materials`, `create_material`, `get_compound_structure`, `set_compound_structure` |
| **Export** | 7 | `export_to_excel`, `import_from_excel`, `batch_export`, `export_room_data` |
| **Audit** | 7 | `analyze_model_statistics`, `check_model_health`, `purge_unused`, `clash_detection` |
| **Workflows** | 5 | `workflow_model_audit`, `workflow_room_documentation`, `workflow_sheet_set` |
| **Database** | 3 | `store_project_data`, `store_room_data`, `query_stored_data` |
| **Journal** | 1 | `analyze_journal` (works without Revit connection) |
| **Code** | 1 | `send_code_to_revit` (execute arbitrary C# in Revit) |
| **Meta** | 1 | `say_hello` (connection test) |

## Architecture

```
 Claude / LLM
      |
 MCP Server (TypeScript)    Zod validation, JSON schema, stdio transport
      |
 SocketService (C#)         TCP bridge, JSON-RPC framing
      |
 CortexRouter (C#)          Deserialize request, find tool, manage session
      |
 ICortexTool (C#)           Unified interface every tool implements
      |
 CortexSession (C#)         Shared state, session store, locale, capabilities
      |
 CortexResult<T> (C#)       Typed response envelope with structured error codes
      |
 DocumentAnalyzer (C#)      Scans active document to populate DocumentCapabilities
```

### Key Design Decisions

- **Two-process architecture**: The TypeScript MCP server handles stdio transport and schema validation. The C# plugin runs inside Revit and executes operations. They communicate via TCP/JSON-RPC.
- **Server off by default**: The TCP server only starts when the user clicks "Cortex Switch". This prevents unwanted connections.
- **Confirmation for destructive ops**: Tools like `delete_element`, `purge_unused`, `batch_rename` show a native Revit TaskDialog before executing.
- **Language-independent categories**: Tools use `OST_*` BuiltInCategory codes instead of localized display names.

## Project Structure

```
RevitCortex/
  RevitCortex.sln
  deploy.ps1                  Deploy script (Revit 2023-2026)
  tool-schemas.txt            Compact tool signatures for token optimization
  src/
    RevitCortex.Core/         Core types (no Revit dependency)
      Discovery/                DocumentCapabilities, IDocumentAnalyzer
      Results/                  CortexResult<T>, CortexError, CortexErrorCode
      Session/                  CortexSession, ISessionStore, SessionStore
      Tools/                    ICortexTool interface
    RevitCortex.Plugin/       Revit add-in (ExternalApplication)
      Commands/                 Ribbon button commands
      Communication/            SocketService, JSON-RPC
      Discovery/                DocumentAnalyzer, LocaleDetector
      Tracking/                 UsageTracker (token usage logging)
      UI/                       CortexPanel, CortexChatClient, Settings
    RevitCortex.Tools/        Tool implementations (by domain)
      Elements/                 Element CRUD, filtering, selection
      Views/                    View creation, templates, overrides
      Project/                  Materials, families, links
      ...
    RevitCortex.Tests/        Unit tests (xUnit)
  server/                     TypeScript MCP server
    src/
      connection/               TCP connection to Plugin
      database/                 SQLite (project data, usage tracking)
      journal/                  Revit journal file analysis
      logging/                  Structured logging, token tracking
      schemas/                  Zod schemas (14 schema files)
      tools/                    Tool definitions (124 tools)
    index.ts                    Server entry point
    esbuild.config.mjs         Build config
```

## Configuration

Settings are stored in `~/.revitcortex/settings.json`:

```json
{
  "port": 8080,
  "logLevel": "Info",
  "model": "claude-sonnet-4-6",
  "disabledTools": [],
  "tokenPricing": {
    "claude-sonnet-4-6": { "inputPerMTok": 3.0, "outputPerMTok": 15.0 },
    "claude-opus-4-6": { "inputPerMTok": 15.0, "outputPerMTok": 75.0 }
  }
}
```

Data files:
- `~/.revitcortex/revitcortex-data.db` -- Project/room SQLite database
- `~/.revitcortex/usage-mcp.db` -- MCP tool usage tracking
- `~/.revitcortex/usage.jsonl` -- API call usage tracking
- `~/.revitcortex/logs/` -- Structured logs

## Building from Source

### C# Plugin (all Revit versions)

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2023 (net48)
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2024 (net48)
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2025 (net8.0)
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2026 (net8.0)
```

### TypeScript Server

```bash
cd server && npm install && npm run build
```

### Tests

```bash
dotnet test -c "Debug R25"
```

### Regenerate Tool Schemas

After adding or modifying tool schemas:

```bash
node server/generate-tool-schemas.mjs
```

## Supported Revit Versions

| Version | .NET Target | Status |
|---------|-------------|--------|
| 2023 | net48 | Supported |
| 2024 | net48 | Supported |
| 2025 | net8.0-windows | Primary |
| 2026 | net8.0-windows | Supported |

## License

Private -- All rights reserved.

## Author

**Luigi Dattilo**

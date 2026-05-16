# RevitCortex

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Revit](https://img.shields.io/badge/Revit-2023%E2%80%932027-blue)](#supported-revit-versions)
[![Latest Release](https://img.shields.io/github/v/release/LuDattilo/revitcortex-releases?label=release)](https://github.com/LuDattilo/revitcortex-releases/releases/latest)

**MCP (Model Context Protocol) server** for Autodesk Revit with 173 tools, typed errors, session state, and dynamic tool discovery. Pure C# — no Node.js required.

RevitCortex lets Claude, GPT, Gemini, or **any MCP-compatible LLM** (Claude Desktop, Claude Code, Codex CLI, Opencode, Cursor, Continue, Cline, Gemini CLI, …) read, create, modify, and analyze Revit models in real time — from querying elements and parameters to creating views, sheets, schedules, IFC pipelines, Power BI live integration, and full audit workflows.

## Features

- **173 MCP tools** across 15+ categories: Elements, Views, Sheets, Schedules, Parameters, Materials, Creation, Export, Audit, Workflows, IFC, Links, Power BI, Journal, Code, and Meta
- **Pure C# architecture** — MCP server and Revit plugin both in C#, no Node.js dependency
- **Typed results** — every tool returns `CortexResult<T>` with structured error codes, not raw strings
- **Session state** — `CortexSession` persists data across tool calls within a session
- **Dynamic tools** — tools auto-hide when the active document doesn't support them
- **Multi-locale** — detects Revit language (EN, IT, FR, DE) and adapts parameter/category names
- **Confirmation dialogs** — destructive operations show a native Revit TaskDialog before executing
- **Multi-version** — supports Revit **2023, 2024, 2025, 2026, and 2027**
- **Power BI live integration** — push selection/elements/schedules to Power BI datasets, custom visual with cross-filter sync
- **IFC native reconstruction** — analyze IFC DirectShapes and rebuild them as native Revit elements
- **Sandboxed code execution** — `send_code_to_revit` validates user code against a namespace block-list
- **Audit log** — every tool invocation appended to `~/.revitcortex/audit.jsonl`
- **Read-only mode** — global switch in settings blocks all write tools

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Dependencies](#dependencies)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [Tool Reference](#tool-reference)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Settings](#settings)
- [Building from Source](#building-from-source)
- [Troubleshooting](#troubleshooting)
- [Supported Revit Versions](#supported-revit-versions)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)

---

## Prerequisites

### Runtime (end users — install ZIP only)

| Requirement | Version | Notes |
|-------------|---------|-------|
| **Autodesk Revit** | 2023, 2024, 2025, 2026, or 2027 | Any edition (LT not supported) |
| **Windows** | 10 or 11 | Revit is Windows-only |
| **.NET Desktop Runtime** | 8.0+ (R25/R26), 10.0+ (R27) | R23/R24 use the .NET Framework 4.8 bundled with Windows |
| **PowerShell** | 5.1+ | Bundled with Windows, used by the installer |

### Build prerequisites (developers — build from source)

| Requirement | Version | Notes |
|-------------|---------|-------|
| **.NET SDK** | 8.0+ | Builds the MCP server and Revit 2025/2026 plugin |
| **.NET SDK** | 10.0+ | Required only for Revit 2027 builds |
| **.NET Framework Targeting Pack** | 4.8 | Builds Revit 2023/2024 plugin |
| **Git** | 2.30+ | Cloning the repository |
| **Visual Studio 2022** (optional) | 17.8+ | For C# debugging |

### MCP Client (one of)

RevitCortex speaks **standard MCP stdio**. Any MCP-compatible client works:

| Client | Status | Notes |
|--------|--------|-------|
| **Claude Desktop** | ✅ Recommended | Anthropic desktop app, edit `claude_desktop_config.json` |
| **Claude Code** | ✅ Tested | Anthropic CLI for developers |
| **Codex CLI** | ✅ Tested | OpenAI's local coding CLI (GPT-5) |
| **Opencode** | ✅ Tested | Open-source TUI ([sst/opencode](https://github.com/sst/opencode)) |
| **Cursor** | ✅ Supported | IDE — MCP via `.cursor/mcp.json` |
| **Continue** | ✅ Supported | VS Code / JetBrains extension |
| **Cline** | ✅ Supported | VS Code extension (formerly Claude Dev) |
| **Gemini CLI** | ✅ Supported | Google's official CLI ([google-gemini/gemini-cli](https://github.com/google-gemini/gemini-cli)) |
| **LM Studio / Ollama** | 🟡 Partial | Only if their MCP bridge is enabled |
| Any other MCP-compatible client | ✅ | Must support **stdio transport** |

---

## Dependencies

### C# MCP Server (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 1.2.0 | MCP protocol implementation, stdio transport |
| `Newtonsoft.Json` | 13.0.4 | JSON serialization (JSON-RPC bridge to plugin) |
| `Microsoft.Extensions.Hosting` | 10.0.6 | .NET hosting, DI, logging |

### C# Plugin (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| `Newtonsoft.Json` | 13.0.3 | JSON serialization (JSON-RPC, API calls) |
| `Nice3point.Revit.Api.RevitAPI` | auto (per version) | Revit API bindings |
| `Nice3point.Revit.Api.RevitAPIUI` | auto (per version) | Revit UI API bindings |
| `Nice3point.Revit.Toolkit` | auto (per version) | Revit development helpers |
| `ClosedXML` | 0.104.2 | Excel import/export (RevitCortex.Tools only) |

Test dependencies:

| Package | Version | Purpose |
|---------|---------|---------|
| `xunit` | 2.9.3 | Unit testing framework |
| `xunit.runner.visualstudio` | 2.8.2 | Test runner for VS |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | Test SDK |

All NuGet packages are restored automatically from nuget.org during build.

---

## Installation

There are two ways to install RevitCortex:

- **🟢 End-user install (recommended)** — download the latest ZIP from [revitcortex-releases](https://github.com/LuDattilo/revitcortex-releases/releases/latest) and run the bundled installer. No build tools required.
- **🟠 From source** — clone the repo and build with `dotnet`. Required only for development, debugging, or custom forks.

---

### Option A — End-user install (pre-built ZIP)

1. Go to the public releases page: **[github.com/LuDattilo/revitcortex-releases/releases/latest](https://github.com/LuDattilo/revitcortex-releases/releases/latest)**
2. Download **`RevitCortex-vX.Y.Z.zip`** and extract it anywhere (e.g. `Desktop\RevitCortex`).
3. Make sure **Revit is closed** (the installer cannot replace locked DLLs).
4. Right-click `install.ps1` → **Run with PowerShell** (or from an admin PowerShell):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

5. The installer:
   - auto-detects every installed Revit version (2023 → 2027)
   - deploys the matching plugin DLLs to `C:\ProgramData\Autodesk\Revit\Addins\{version}\RevitCortex\`
   - installs the MCP server to `%USERPROFILE%\.revitcortex\server\`
   - (optional) writes the MCP entry into `claude_desktop_config.json` automatically

6. Restart Revit. A **RevitCortex** ribbon tab appears with two buttons:

   | Button | Description |
   |--------|-------------|
   | **Cortex Switch** | Start/stop the MCP TCP server (off by default) |
   | **Settings** | Configure port, log level, and tool visibility |

To uninstall: run `uninstall.ps1` from the same folder.

---

### Option B — Build from source

#### 1. Clone the repository

```bash
git clone https://github.com/LuDattilo/RevitCortex.git
cd RevitCortex
```

#### 2. Build the C# MCP server

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

NuGet packages are restored automatically on first build.

#### 3. Build the C# Revit plugin

Pick your Revit version. The build configuration format is `{Debug|Release} R{version_short}`:

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2023
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2024
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2025
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2026
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # Revit 2027 (requires .NET SDK 10+)
```

#### 4. Deploy to Revit

```powershell
# Default: Revit 2025, Debug
powershell -ExecutionPolicy Bypass -File deploy.ps1

# Specify version and config
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2027 -Config Release
```

The script publishes the plugin, copies DLLs to the Revit add-ins folder, and installs the `.addin` manifest. **Restart Revit** afterwards.

---

## Configuration

### Where is the MCP server executable?

After the **ZIP installer** runs, the standalone MCP server lives at:

```
%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe
```

After a **from-source** build, use either:

```
C:\path\to\RevitCortex\src\RevitCortex.Server\bin\Debug\net8.0\RevitCortex.Server.exe
```

or `dotnet run --project <path-to-RevitCortex.Server>`.

All examples below assume the installer path. Substitute your own if you built from source.

---

### MCP Client Setup — choose your client

> ⚠️ **Common gotcha**: all paths in MCP configs must be **absolute**. Tilde (`~`) and `%USERPROFILE%` are **not expanded** by every client — write the literal path (`C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe`).

#### Claude Desktop

Edit `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
    }
  }
}
```

Restart Claude Desktop. The installer can write this entry for you automatically.

#### Claude Code (CLI)

One-liner:

```bash
claude mcp add revitcortex "C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe"
```

Or add to a project's `.mcp.json`:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
    }
  }
}
```

#### Codex CLI (OpenAI)

Edit `%USERPROFILE%\.codex\config.toml`:

```toml
[mcp_servers.revitcortex]
command = "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
```

Then `codex` will discover RevitCortex automatically. Verify with `/mcp` inside Codex.

#### Opencode (sst/opencode)

Edit `%USERPROFILE%\.config\opencode\opencode.json` (or `~/.opencode/opencode.json`):

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "revitcortex": {
      "type": "local",
      "command": ["C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"],
      "enabled": true
    }
  }
}
```

Restart Opencode. Run `/mcp` to verify the 173 tools are registered.

#### Cursor

Create `.cursor/mcp.json` in your project (or `%USERPROFILE%\.cursor\mcp.json` for global):

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
    }
  }
}
```

Reload Cursor → Settings → MCP, you should see RevitCortex listed.

#### Continue (VS Code / JetBrains)

Edit `~\.continue\config.yaml`:

```yaml
mcpServers:
  - name: revitcortex
    command: C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe
```

#### Cline (VS Code)

In the Cline sidebar → **MCP Servers** → **Configure MCP Servers** and add:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe",
      "disabled": false
    }
  }
}
```

#### Gemini CLI (Google)

Edit `%USERPROFILE%\.gemini\settings.json`:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<you>\\.revitcortex\\server\\RevitCortex.Server.exe"
    }
  }
}
```

Then in `gemini` run `/mcp list` to confirm registration.

#### Generic stdio MCP client

Any client that supports MCP stdio works:

```
command: C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe
args:    []   (none required)
env:     (optional) REVITCORTEX_PORT=8080
```

---

### Connection Checklist

Before invoking any tool, ensure:

1. **Revit is running** with a document open
2. **Cortex Switch is ON** — click it in the RevitCortex ribbon tab (server is **off by default**)
3. The MCP server is reachable (your MCP client launches it automatically via stdio)
4. Default TCP port between MCP server ↔ Plugin is **8080** (configurable in Settings)
5. If you have a firewall prompt the first time, allow **localhost** access — RevitCortex never exposes anything off-machine

---

## Usage

### Via MCP Client (Claude Desktop / Claude Code)

Once configured, your LLM has access to all 173 tools. Examples:

| Request | Tools Used |
|---------|-----------|
| "Show me all walls in the model" | `ai_element_filter` |
| "What parameters does element 606873 have?" | `get_element_parameters` |
| "Create a floor plan for each room" | `create_views_from_rooms` |
| "Export all room data to Excel" | `export_to_excel` |
| "Run a full model health check" | `workflow_model_audit` |
| "Link this IFC file into the project" | `ifc_validate_request` → `ifc_link` |
| "Analyze which IFC elements can become native Revit" | `ifc_analyze_rebuildability` |
| "Rebuild all walls from the IFC import" | `ifc_rebuild_walls` |
| "Delete all unused families" | `purge_unused` (shows confirmation dialog) |
| "Color all doors red" | `color_elements` |
| "How much did I use today?" | `report_token_usage` |

### Locale Detection

Revit localizes category and parameter names by language. RevitCortex detects the locale automatically:

| Language | Example Parameters |
|----------|--------------------|
| English | Level, Comments, Type Name |
| Italiano | Livello, Commenti, Nome del tipo |
| Francais | Niveau, Commentaires, Nom du type |
| Deutsch | Ebene, Kommentare, Typname |

Use `OST_*` codes (e.g., `OST_Walls`, `OST_Doors`) for categories -- these are language-independent and always work.

### Destructive Operations

Tools that modify or delete data show a **native Revit confirmation dialog** before executing:

`delete_element`, `delete_selection`, `delete_material`, `purge_unused`, `wipe_empty_tags`, `set_element_parameters`, `set_compound_structure`, `batch_rename`, `override_graphics`, `set_element_phase`, `set_element_workset`, `change_element_type`, `load_family`

Use the `dryRun: true` parameter (where available) to preview changes without applying them.

---

## Tool Reference

### Elements (24 tools)

| Tool | Description |
|------|-------------|
| `ai_element_filter` | Query elements by category, parameter filters, and conditions |
| `get_element_parameters` | Get all parameters of specific elements by ID |
| `set_element_parameters` | Modify parameter values on elements |
| `get_selected_elements` | Get currently selected elements |
| `get_current_view_elements` | List elements visible in the active view |
| `get_linked_elements` | Query elements from linked Revit models |
| `get_elements_in_spatial_volume` | Find elements within a 3D bounding box |
| `delete_element` | Delete elements by ID |
| `delete_selection` | Delete all currently selected elements |
| `operate_element` | Select, highlight, or zoom to elements |
| `change_element_type` | Change the family type of elements |
| `modify_element` | Move, rotate, or mirror elements |
| `copy_elements` | Copy elements with offset |
| `measure_between_elements` | Measure distance between two elements |
| `renumber_elements` | Sequentially renumber elements (mark parameter) |
| `find_untagged_elements` | Find elements missing tags in the active view |
| `find_undimensioned_elements` | Find elements missing dimensions |
| `export_elements_data` | Export element data to JSON |
| `match_element_properties` | Copy parameters from source to target elements |
| `color_elements` | Apply color overrides to elements by category |
| `save_selection` | Save current selection with a name |
| `load_selection` | Restore a previously saved selection |
| `set_element_phase` | Change the creation phase of elements |
| `set_element_workset` | Move elements to a different workset |

### Creation (13 tools)

| Tool | Description |
|------|-------------|
| `create_line_based_element` | Create walls or other line-based elements |
| `create_point_based_element` | Create columns, furniture, and point-based families |
| `create_surface_based_element` | Create floors, ceilings from boundary points |
| `create_floor` | Create a floor from boundary points |
| `create_grid` | Create grid lines |
| `create_level` | Create levels at specified elevations |
| `create_room` | Create rooms at specified positions |
| `create_array` | Create linear or radial arrays |
| `create_filled_region` | Create filled regions in views |
| `create_dimensions` | Create dimension annotations |
| `create_text_note` | Create text notes in views |
| `create_color_legend` | Create a color fill legend |
| `create_structural_framing_system` | Create structural beam systems |

### Views (13 tools)

| Tool | Description |
|------|-------------|
| `create_view` | Create floor plans, sections, 3D views, elevations |
| `duplicate_view` | Duplicate an existing view |
| `create_view_filter` | Create parameter-based view filters |
| `override_graphics` | Override element graphics in a view |
| `apply_view_template` | Apply a view template |
| `batch_modify_view_range` | Modify view range for multiple views |
| `section_box_from_selection` | Create a 3D section box from selected elements |
| `manage_unplaced_views` | List or delete views not placed on sheets |
| `manage_view_templates` | List, create, or apply view templates |
| `create_views_from_rooms` | Create a plan view for each room |
| `get_current_view_info` | Get info about the active view |
| `rename_views` | Batch rename views with patterns |
| `lines_per_view_count` | Count detail lines per view (performance audit) |

### Sheets (7 tools)

| Tool | Description |
|------|-------------|
| `create_sheet` | Create a new sheet with a title block |
| `place_viewport` | Place a view on a sheet |
| `align_viewports` | Align viewport positions across sheets |
| `batch_create_sheets` | Create multiple sheets at once |
| `create_placeholder_sheets` | Create placeholder sheets for future content |
| `duplicate_sheet_with_content` | Duplicate a sheet with all annotations |
| `duplicate_sheet_with_views` | Duplicate a sheet and its placed views |

### Schedules (8 tools)

| Tool | Description |
|------|-------------|
| `create_schedule` | Create a new schedule with custom fields |
| `create_preset_schedule` | Create a schedule from a predefined template |
| `get_schedule_data` | Export schedule data as JSON |
| `delete_schedule` | Delete a schedule |
| `duplicate_schedule` | Duplicate an existing schedule |
| `modify_schedule` | Add/remove fields, change filters and sorting |
| `list_schedulable_fields` | List all available fields for a category |
| `import_table` | Import a CSV/data table into a schedule |

### Parameters (11 tools)

| Tool | Description |
|------|-------------|
| `add_shared_parameter` | Add a shared parameter to a category |
| `manage_project_parameters` | List, create, delete, modify project parameters; `set_group` action bulk-reassigns "Group Parameter Under" |
| `manage_global_parameters` | List, create, read, update, or delete global parameters |
| `add_prefix_suffix` | Add prefix/suffix to parameter values |
| `get_shared_parameters` | List all shared parameters in the project |
| `bulk_modify_parameter_values` | Modify parameter values across multiple elements |
| `clear_parameter_values` | Clear parameter values for a category |
| `transfer_parameters` | Copy parameter values between elements |
| `filter_by_parameter_value` | Filter elements by parameter value |
| `batch_rename` | Rename elements using pattern rules |
| `sync_csv_parameters` | Sync parameter values from/to a CSV file |

### Project (16 tools)

| Tool | Description |
|------|-------------|
| `get_project_info` | Get project name, address, author, levels, phases |
| `get_phases` | List all project phases |
| `get_worksets` | List all worksets |
| `get_warnings` | Get all model warnings and errors |
| `create_revision` | Create a new revision |
| `manage_links` | List, reload, or remove linked models |
| `load_family` | Load a family (.rfa) into the project |
| `rename_families` | Batch rename families |
| `get_available_family_types` | List all loaded family types |
| `list_family_sizes` | Show file sizes of loaded families |
| `get_room_openings` | Get doors/windows adjacent to rooms |
| `tag_rooms` | Auto-tag all rooms in a view |
| `tag_walls` | Auto-tag all walls in a view |
| `duplicate_system_type` | Duplicate a wall/floor/roof/ceiling type |
| `manage_project_units` | Get or set project units (length, area, volume, angle, etc.) |
| `manage_additional_settings` | Manage line styles, line weights, line patterns, fill patterns, halftone/underlay |

### Materials (9 tools)

| Tool | Description |
|------|-------------|
| `get_materials` | List all project materials |
| `get_material_properties` | Get detailed material properties |
| `get_material_quantities` | Get material quantities by element |
| `set_material_properties` | Modify material properties |
| `create_material` | Create a new material |
| `duplicate_material` | Duplicate an existing material |
| `delete_material` | Delete a material |
| `get_compound_structure` | Get wall/floor/roof layer structure |
| `set_compound_structure` | Modify compound structure layers |

### Export (7 tools)

| Tool | Description |
|------|-------------|
| `export_room_data` | Export all room data (area, volume, level) |
| `export_schedule` | Export a schedule to CSV |
| `export_families` | Export loaded families to .rfa files |
| `export_shared_parameter_file` | Export shared parameter definitions |
| `batch_export` | Export views to PDF, DWG, DWF, NWC, IFC |
| `export_to_excel` | Export element data to Excel (.xlsx) |
| `import_from_excel` | Import parameter values from Excel |

### Audit (7 tools)

| Tool | Description |
|------|-------------|
| `analyze_model_statistics` | Element counts by category, type, family, level |
| `check_model_health` | Comprehensive model health score |
| `audit_families` | Audit loaded families (size, usage, naming) |
| `purge_unused` | Identify/remove unused families, types, materials |
| `cad_link_cleanup` | Find and manage CAD imports/links |
| `clash_detection` | Detect geometric clashes between categories |
| `wipe_empty_tags` | Remove tags with no value |

### Workflows (5 tools)

| Tool | Description |
|------|-------------|
| `workflow_model_audit` | Complete model audit (health + families + purge) |
| `workflow_room_documentation` | Full room documentation (views + tags + schedules) |
| `workflow_sheet_set` | Create a complete sheet set from views |
| `workflow_clash_review` | Run clash detection with categorized report |
| `workflow_data_roundtrip` | Export data, modify externally, re-import |

### Database (3 tools)

| Tool | Description |
|------|-------------|
| `store_project_data` | Save project metadata to local SQLite |
| `store_room_data` | Save room data to local SQLite |
| `query_stored_data` | Query stored projects and rooms |

### IFC (20 tools)

| Tool | Description |
|------|-------------|
| `ifc_get_capabilities` | Detect IFC version support and revit-ifc add-in presence |
| `ifc_validate_request` | Validate IFC file path, extension, and schema version |
| `ifc_link` | Link an IFC file into the active document (RevitLinkType) |
| `ifc_reload_link` | Reload an existing IFC link, optionally from a new file |
| `ifc_open_or_import` | Open or import an IFC file using IFCImportOptions |
| `ifc_export_basic` | Export to IFC with basic options (version, quantities, splitting) |
| `ifc_export_with_configuration` | Export using named configurations with overrides |
| `ifc_list_export_configurations` | List available built-in export configurations |
| `ifc_get_export_configuration` | Get full details of a specific export configuration |
| `ifc_set_family_mapping_file` | Set family mapping file for IFC exports (session) |
| `ifc_analyze_rebuildability` | Analyze IFC DirectShapes for native reconstruction feasibility |
| `ifc_list_rebuild_candidates` | List elements above a rebuild confidence threshold |
| `ifc_rebuild_walls` | Rebuild native walls from IFC DirectShapes |
| `ifc_rebuild_floors` | Rebuild native floors from IFC DirectShapes |
| `ifc_rebuild_roofs` | Rebuild native roofs from IFC DirectShapes |
| `ifc_rebuild_structural_members` | Rebuild columns and beams from IFC DirectShapes |
| `ifc_rebuild_openings` | Cut openings in rebuilt walls/floors |
| `ifc_rebuild_family_instances` | Place doors, windows from IFC DirectShapes |
| `ifc_compare_original_vs_rebuilt` | Compare volume/geometry between original and rebuilt |
| `ifc_tag_unreconstructable_elements` | Tag elements that cannot be rebuilt |

### Journal, Code, Meta (3 tools)

| Tool | Description |
|------|-------------|
| `analyze_journal` | Analyze Revit journal files (works without Revit) |
| `send_code_to_revit` | Execute arbitrary C# code in Revit |
| `say_hello` | Connection test -- shows a dialog in Revit |

---

## Architecture

```
 Claude / LLM
      |
 MCP Server (C#)            ModelContextProtocol SDK, stdio transport
      |  (TCP localhost:8080)
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

### Design Decisions

- **Two-process architecture**: The C# MCP server handles stdio transport and schema validation via the ModelContextProtocol SDK. The C# plugin runs inside Revit's process and executes operations via the Revit API. They communicate via TCP/JSON-RPC on localhost.
- **Server off by default**: The TCP server starts only when the user clicks "Cortex Switch". This prevents unwanted connections and resource usage.
- **Confirmation for destructive ops**: Tools like `delete_element`, `purge_unused` show a native Revit `TaskDialog` before executing. Cancelled operations return a structured `Cancelled` error code.
- **Language-independent categories**: All tools use `OST_*` BuiltInCategory codes instead of localized display names.
- **Dynamic tool visibility**: Tools with `IsDynamic = true` are only exposed when `DocumentCapabilities` match (e.g., workset tools hidden in non-workshared files).

---

## Project Structure

```
RevitCortex/
  RevitCortex.sln              Solution file
  nuget.config                 NuGet package source (nuget.org)
  deploy.ps1                   Deploy script (Revit 2023-2026)
  tool-schemas.txt             Compact tool signatures for token optimization
  CLAUDE.md                    AI assistant guide (tool corrections, locale maps)

  src/
    RevitCortex.Core/          Core types (no Revit dependency)
      Discovery/                 DocumentCapabilities, IDocumentAnalyzer
      Results/                   CortexResult<T>, CortexError, CortexErrorCode
      Session/                   CortexSession, ISessionStore, SessionStore
      Tools/                     ICortexTool interface

    RevitCortex.Plugin/        Revit add-in (ExternalApplication)
      Commands/                  Ribbon button commands (ToggleConnection, etc.)
      Communication/             SocketService, JSON-RPC models
      Discovery/                 DocumentAnalyzer, LocaleDetector
      Tracking/                  UsageTracker (API token usage logging)
      UI/                        CortexPanel, CortexChatClient, Settings
      RevitCortexApp.cs          Entry point (IExternalApplication)
      RevitCortex.addin          Revit add-in manifest

    RevitCortex.Tools/         Tool implementations (grouped by domain)
      Elements/                  Element CRUD, filtering, selection
      Views/                     View creation, templates, overrides
      Sheets/                    Sheet creation, viewports
      Project/                   Materials, families, links, system types
      Meta/                      SayHelloTool

    RevitCortex.Tests/         Unit tests (xUnit)
      Discovery/                 DocumentCapabilitiesTests
      Results/                   CortexResultTests
      Router/                    CortexRouterTests
      Session/                   SessionStoreTests

  src/RevitCortex.Server/      C# MCP server (stdio transport)
    Program.cs                   Server entry point (MCP hosting)
    Connection/
      RevitBridge.cs             TCP bridge to Plugin (JSON-RPC)
    Tools/                       Tool definitions (157 tools across 9 files)
      MetaTools.cs               say_hello, get_project_info
      ElementTools.cs            Element CRUD, filtering, selection
      ViewTools.cs               Views, sheets, schedules
      ProjectTools.cs            Project info, audit, workflows
      CreationTools.cs           Element creation, export
      MaterialTools.cs           Materials, compound structures
      ParameterTools.cs          Parameter operations
      LinkTools.cs               Linked file management
      IfcTools.cs                IFC import/export/reconstruction

  docs/
    superpowers/
      specs/                     Design specifications
      plans/                     Implementation plans
```

---

## Settings

Settings are stored in `~/.revitcortex/settings.json` (created on first run):

```json
{
  "port": 8080,
  "logLevel": "Info",
  "disabledTools": []
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `port` | 8080 | TCP port for MCP server <-> Plugin communication |
| `logLevel` | Info | Log verbosity: Info, Warning, Error |
| `disabledTools` | [] | Array of tool names to hide from the MCP client |

### Data Files

| File | Purpose |
|------|---------|
| `~/.revitcortex/settings.json` | User settings |
| `~/.revitcortex/logs/` | Structured log files |
| `~/.revitcortex/audit.jsonl` | Tool execution audit log |

---

## Building from Source

### Full Build (all components)

```bash
# C# MCP server
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj

# C# plugin (pick your Revit version)
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj

# Unit tests
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj
```

### Build All Revit Versions

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj   # needs .NET SDK 10+
```

---

## Troubleshooting

### "Connection refused" when calling tools

1. Ensure Revit is running with a document open
2. Click **Cortex Switch** in the ribbon to start the TCP server
3. Check the port in Settings matches the default (8080)

### Tools not appearing in Claude Desktop

1. Verify `claude_desktop_config.json` has the correct absolute path to `src/RevitCortex.Server`
2. Restart Claude Desktop after config changes
3. Check that `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj` completed successfully

### Build errors for specific Revit version

1. Ensure you have the correct .NET SDK installed:
   - **R23/R24** — .NET Framework 4.8 targeting pack
   - **R25/R26** — .NET SDK 8.0+
   - **R27** — .NET SDK 10.0+ (otherwise `NETSDK1045` "framework not supported")
2. Run `dotnet restore` before building.
3. Check that `nuget.config` points to `https://api.nuget.org/v3/index.json`.

### MCP client doesn't see RevitCortex tools

1. Run the executable manually to confirm it starts: `& "C:\Users\<you>\.revitcortex\server\RevitCortex.Server.exe"` — it should print MCP handshake lines.
2. Use **absolute paths** in your MCP config — `~` / `%USERPROFILE%` are not always expanded.
3. Check the client's MCP log:
   - Claude Desktop → `%APPDATA%\Claude\logs\mcp*.log`
   - Codex CLI → `~/.codex/log/`
   - Opencode → `~/.local/share/opencode/log/` (or `%LOCALAPPDATA%\opencode\log\` on Windows)
4. Server log: `~/.revitcortex/logs/mcp-server-revitcortex.log` — if you see "No frameworks were found", the publish mode is inconsistent. Re-run the ZIP installer.

### Revit freezes when executing a tool

- Heavy operations (3D views, model statistics) can take time
- Read operations are safe to run in parallel (5+)
- Limit write operations to 3-4 concurrent
- Run `analyze_model_statistics` and `purge_unused` individually

---

## Supported Revit Versions

| Version | .NET Target | Conditional Symbols |
|---------|-------------|---------------------|
| 2023 | net48 | `REVIT2023_OR_GREATER` |
| 2024 | net48 | `REVIT2023_OR_GREATER`, `REVIT2024_OR_GREATER` |
| 2025 | net8.0-windows | `REVIT2023_OR_GREATER` … `REVIT2025_OR_GREATER` |
| 2026 | net8.0-windows | `REVIT2023_OR_GREATER` … `REVIT2026_OR_GREATER` |
| 2027 | net10.0-windows | `REVIT2023_OR_GREATER` … `REVIT2027_OR_GREATER` |

API differences (e.g., `ElementId.Value` vs `.IntegerValue`) are handled via `#if` directives.

> Building **R27** locally requires a .NET SDK **≥ 10**. `global.json` pins to SDK 8 with `rollForward: latestMajor`, so SDK 11 preview (or any future SDK) is accepted automatically when SDK 10 is absent.

---

## Contributing

Contributions are welcome — issues, PRs, and ideas.

1. Fork the repo and create a feature branch.
2. Build **all** target Revit versions before opening a PR (`Debug R23` → `Debug R27`). See [CLAUDE.md](CLAUDE.md) for cross-target gotchas (`record`, `init`, `Range/Index`, etc.).
3. Run the test suite: `dotnet test -c "Debug R25"`.
4. Update [tool-schemas.txt](tool-schemas.txt) (`node server/generate-tool-schemas-csharp.mjs`) and any affected docs (`README.md`, `docs/USER_GUIDE.md`, `WORKFLOWS.md`).

For deep AI-assistant context (locale mappings, tool-specific corrections, performance notes) see [CLAUDE.md](CLAUDE.md) and [WORKFLOWS.md](WORKFLOWS.md).

---

## Security

RevitCortex runs **fully on-machine**. The MCP server ↔ Plugin bridge listens only on **localhost** and is **off by default**. See [docs/SECURITY.md](docs/SECURITY.md) for the full threat model, including:

- Namespace block-list enforced by `CodeSandbox` before any `send_code_to_revit` execution.
- Append-only audit log at `~/.revitcortex/audit.jsonl`.
- Global `readOnlyMode` switch in `~/.revitcortex/settings.json`.

Found a vulnerability? Email **luigi.dattilo@gpapartners.com** — please do not open a public issue.

---

## License

[MIT](LICENSE) © 2026 Luigi Dattilo

## Author

**Luigi Dattilo** — [@LuDattilo](https://github.com/LuDattilo)

Built at [GPA S.r.l.](https://www.gpapartners.com) — engineering & BIM.

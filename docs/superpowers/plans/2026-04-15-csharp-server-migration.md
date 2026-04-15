# RevitCortex C# Server Migration & Chat Panel Removal

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the migration from TypeScript to C# MCP server, remove the unused chat panel, achieve full tool parity (149/149), update docs, and push.

**Architecture:** The TypeScript server (`server/`) is replaced by `src/RevitCortex.Server/` (C# console app using ModelContextProtocol SDK v1.2). The chat panel (WPF dockable + Anthropic API client) is removed from the plugin — all AI interaction happens via MCP clients (Claude Desktop/Code). Everything stays in Git history.

**Tech Stack:** C# / .NET 8, ModelContextProtocol NuGet 1.2.0, Revit API 2023-2027

---

## Task 1: Commit current state & create branch

**Files:** None modified — Git operations only.

- [ ] **Step 1: Commit all pending changes**

```bash
cd C:\Users\luigi.dattilo\Documents\RevitCortex
git add -A
git commit -m "feat: add RevitCortex.Server (C# MCP server) + batch_rename system types + DocumentClosing handler

- New project: src/RevitCortex.Server with 119 MCP tools (C# replacement for TS server)
- BatchRenameTool: extended targetCategory with WallTypes/FloorTypes/CeilingTypes/RoofTypes
- bulk-operations.ts: matching Zod schema update
- RevitCortexApp: DocumentClosing handler stops server, resets session, clears chat
- CortexPanel: OnDocumentClosing method for clean state reset

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 2: Create feature branch**

```bash
git checkout -b feat/csharp-server-migration
```

---

## Task 2: Remove chat panel from plugin

**Files:**
- Delete: `src/RevitCortex.Plugin/UI/CortexPanel.xaml`
- Delete: `src/RevitCortex.Plugin/UI/CortexPanel.xaml.cs`
- Delete: `src/RevitCortex.Plugin/UI/CortexChatClient.cs`
- Delete: `src/RevitCortex.Plugin/UI/CortexDockablePaneProvider.cs`
- Delete: `src/RevitCortex.Plugin/UI/ApiKeySettingsPage.xaml`
- Delete: `src/RevitCortex.Plugin/UI/ApiKeySettingsPage.xaml.cs`
- Delete: `src/RevitCortex.Plugin/UI/PricingSettingsPage.xaml`
- Delete: `src/RevitCortex.Plugin/UI/PricingSettingsPage.xaml.cs`
- Delete: `src/RevitCortex.Plugin/UI/UsageReportPage.xaml`
- Delete: `src/RevitCortex.Plugin/UI/UsageReportPage.xaml.cs`
- Delete: `src/RevitCortex.Plugin/Commands/ToggleCortexPanel.cs`
- Delete: `src/RevitCortex.Plugin/Tracking/` (UsageTracker — only used by chat panel)
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs`
- Modify: `src/RevitCortex.Plugin/UI/SettingsWindow.xaml`
- Modify: `src/RevitCortex.Plugin/UI/SettingsWindow.xaml.cs`

- [ ] **Step 1: Delete chat panel files**

Delete these files:
- `src/RevitCortex.Plugin/UI/CortexPanel.xaml`
- `src/RevitCortex.Plugin/UI/CortexPanel.xaml.cs`
- `src/RevitCortex.Plugin/UI/CortexChatClient.cs`
- `src/RevitCortex.Plugin/UI/CortexDockablePaneProvider.cs`
- `src/RevitCortex.Plugin/UI/ApiKeySettingsPage.xaml`
- `src/RevitCortex.Plugin/UI/ApiKeySettingsPage.xaml.cs`
- `src/RevitCortex.Plugin/UI/PricingSettingsPage.xaml`
- `src/RevitCortex.Plugin/UI/PricingSettingsPage.xaml.cs`
- `src/RevitCortex.Plugin/UI/UsageReportPage.xaml`
- `src/RevitCortex.Plugin/UI/UsageReportPage.xaml.cs`
- `src/RevitCortex.Plugin/Commands/ToggleCortexPanel.cs`

- [ ] **Step 2: Clean RevitCortexApp.cs**

Remove from `OnStartup`:
- The `RegisterDockablePane` block (lines 36-48)
- The "Chat Panel" ribbon button creation (lines 154-161)

Remove from `OnIdling`:
- The panel hide logic (`_panelHideAttempts`, pane.Hide() block)

Remove from `OnDocumentClosing`:
- The `CortexPanel.Instance?.Dispatcher.BeginInvoke(...)` block — keep the server stop and session reset.

Remove field: `private int _panelHideAttempts = 5;`

- [ ] **Step 3: Clean SettingsWindow**

Remove the "API Key" and "Pricing" and "Usage Report" tabs from `SettingsWindow.xaml` and their code-behind references in `SettingsWindow.xaml.cs`. Keep: General settings, Tools settings.

- [ ] **Step 4: Check for Tracking/ folder**

If `src/RevitCortex.Plugin/Tracking/UsageTracker.cs` is only used by CortexChatClient, delete the whole `Tracking/` folder. If also used by CortexRouter or other tools, keep it.

- [ ] **Step 5: Build all targets**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

All must pass with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: remove chat panel, API key settings, pricing, usage report

Chat panel (WPF dockable + Anthropic API client) removed.
All AI interaction now happens via MCP clients (Claude Desktop/Code).
Removed: CortexPanel, CortexChatClient, CortexDockablePaneProvider,
ApiKeySettingsPage, PricingSettingsPage, UsageReportPage, ToggleCortexPanel.
Kept: ToggleConnection, OpenSettings, General/Tools settings."
```

---

## Task 3: Add 34 missing tools to C# server

**Files:**
- Create: `src/RevitCortex.Server/Tools/IfcTools.cs`
- Modify: `src/RevitCortex.Server/Tools/ElementTools.cs`
- Modify: `src/RevitCortex.Server/Tools/MaterialTools.cs`
- Modify: `src/RevitCortex.Server/Tools/ProjectTools.cs`

Missing tools (34 total):

**IFC tools (20):** ifc_get_capabilities, ifc_validate_request, ifc_link, ifc_reload_link, ifc_open_or_import, ifc_export_basic, ifc_export_with_configuration, ifc_list_export_configurations, ifc_get_export_configuration, ifc_set_family_mapping_file, ifc_analyze_rebuildability, ifc_list_rebuild_candidates, ifc_rebuild_walls, ifc_rebuild_floors, ifc_rebuild_roofs, ifc_rebuild_structural_members, ifc_rebuild_openings, ifc_rebuild_family_instances, ifc_compare_original_vs_rebuilt, ifc_tag_unreconstructable_elements

**Element tools (4):** get_elements_in_spatial_volume, get_linked_elements, get_room_openings, modify_element

**Material tools (3):** get_material_properties, get_material_quantities, delete_material, duplicate_material, duplicate_family_type

**Project tools (5):** get_shared_parameters, lines_per_view_count, list_family_sizes, list_schedulable_fields, get_schedule_data (check if already exists)

- [ ] **Step 1: Create IfcTools.cs** with 20 IFC tools. All use generic forwarding (`data` string → `JObject.Parse`).

- [ ] **Step 2: Add missing element tools** to ElementTools.cs: get_elements_in_spatial_volume, get_linked_elements, get_room_openings, modify_element

- [ ] **Step 3: Add missing material tools** to MaterialTools.cs: get_material_properties, get_material_quantities, delete_material, duplicate_material, duplicate_family_type

- [ ] **Step 4: Add missing project tools** to ProjectTools.cs: get_shared_parameters, lines_per_view_count, list_family_sizes, list_schedulable_fields

- [ ] **Step 5: Verify tool count**

```bash
grep -c 'McpServerTool(Name' src/RevitCortex.Server/Tools/*.cs
```

Total should be ~149 (matching plugin count).

- [ ] **Step 6: Build and verify**

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

0 errors required.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: complete C# MCP server tool parity (149 tools)

Added 34 missing tools: 20 IFC, 4 Element, 5 Material, 5 Project.
C# server now exposes all 149 tools matching the Revit plugin."
```

---

## Task 4: Code review

- [ ] **Step 1: Review RevitBridge.cs** — TCP connection handling, error recovery, timeouts
- [ ] **Step 2: Review all Tool files** — parameter correctness, JSON serialization, edge cases
- [ ] **Step 3: Review Program.cs** — hosting configuration, logging, graceful shutdown
- [ ] **Step 4: Review RevitCortexApp.cs** — post chat-panel removal, clean lifecycle
- [ ] **Step 5: Cross-target build verification**

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test -c "Debug R25"
```

- [ ] **Step 6: Fix any issues found**

---

## Task 5: Update documentation

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `distribution/config-templates/` (Claude Desktop config)

- [ ] **Step 1: Update README.md**
- Remove Node.js from prerequisites
- Update installation: no more `cd server && npm install && npm run build`
- Update architecture diagram: C# server replaces TS
- Remove "Built-in chat panel" from features
- Remove API key setup section
- Update Claude Desktop config to use `dotnet run`
- Add self-contained exe publish instructions

- [ ] **Step 2: Update CLAUDE.md**
- Update project structure (remove `server/` references, add `src/RevitCortex.Server/`)
- Update architecture diagram
- Remove "UI Components" section (CortexPanel, CortexChatClient, etc.)
- Update build commands
- Update deploy instructions

- [ ] **Step 3: Update config templates**
- `distribution/config-templates/` — change from `node` to `dotnet run`

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: update README, CLAUDE.md for C# server migration

- Removed Node.js dependency from prerequisites
- Updated architecture diagrams
- Removed chat panel documentation
- Updated Claude Desktop config templates"
```

---

## Task 6: Deploy, test, and push

- [ ] **Step 1: Deploy plugin**

```bash
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

- [ ] **Step 2: Restart Revit, verify Cortex Switch works**
- [ ] **Step 3: Test C# server from Claude Desktop** — say_hello, get_project_info, create_material
- [ ] **Step 4: Push**

```bash
git push origin feat/csharp-server-migration
```

- [ ] **Step 5: Merge to main (after validation)**

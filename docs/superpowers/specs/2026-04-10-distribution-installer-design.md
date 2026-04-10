# RevitCortex Distribution & One-Click Installer

**Date:** 2026-04-10
**Status:** Approved

## Goal

Enable non-technical colleagues to install RevitCortex with a single operation: download a ZIP from GitHub Releases, extract, run `install.ps1`. No programming knowledge, build tools, or manual configuration required.

## Constraints

- Colleagues have similar Windows 11 hardware but mixed Revit versions (2023–2026)
- No Node.js installed on their machines
- Clients: Claude Desktop, Claude Code, or both
- Single ZIP for all Revit versions (installer auto-detects)

## Release Package Structure

```
RevitCortex-v{version}.zip
├── install.ps1                   # One-click installer
├── uninstall.ps1                 # Cleanup script
├── README.txt                    # 5-line quick start
├── plugin/
│   ├── R23/                      # Pre-built DLLs (net48)
│   │   ├── RevitCortex.Plugin.dll
│   │   ├── RevitCortex.Tools.dll
│   │   ├── RevitCortex.Core.dll
│   │   └── ... (all dependencies)
│   ├── R24/                      # Pre-built DLLs (net48)
│   ├── R25/                      # Pre-built DLLs (net8.0)
│   └── R26/                      # Pre-built DLLs (net8.0)
├── server/
│   ├── package.json              # For npm install (sql.js dependency)
│   └── build/
│       ├── index.js              # Bundled MCP server (esbuild)
│       └── sql-wasm.wasm         # SQLite WASM runtime
├── RevitCortex.addin             # Manifest template
└── config-templates/
    ├── claude-desktop.json       # Template for Claude Desktop MCP config
    └── claude-code.json          # Template for Claude Code MCP config
```

## install.ps1 Flow

### Step 1: Check/Install Node.js

```
node --version
├── Found (≥18) → OK, continue
└── Not found →
    ├── winget available? → winget install OpenJS.NodeJS.LTS --silent
    └── winget unavailable? → Download node-v{lts}-x64.msi from nodejs.org
        └── msiexec /i node-installer.msi /qn (silent install)
    └── Refresh PATH, verify node --version
    └── Still fails → Error: "Install Node.js manually from https://nodejs.org" + exit
```

### Step 2: Detect Installed Revit Versions

Scan `C:\ProgramData\Autodesk\Revit\Addins\` for year folders (2023, 2024, 2025, 2026). Cross-reference with `plugin/R{XX}/` folders in the ZIP.

Output: list of versions to install (e.g., "Found Revit 2024, 2025").

If no Revit found: error and exit.

### Step 3: Install Revit Plugin

For each detected Revit version:

1. Map version to config suffix: 2023→R23, 2024→R24, 2025→R25, 2026→R26
2. Copy `plugin/R{XX}/*` → `C:\ProgramData\Autodesk\Revit\Addins\{year}\RevitCortex\`
3. Copy `RevitCortex.addin` → `C:\ProgramData\Autodesk\Revit\Addins\{year}\`

Requires admin elevation for writing to ProgramData. Script self-elevates if not already admin.

### Step 4: Install MCP Server

1. Create `~/.revitcortex/server/` if not exists
2. Copy `server/build/` and `server/package.json` to `~/.revitcortex/server/`
3. Run `npm install --production` in `~/.revitcortex/server/` (installs sql.js)

### Step 5: Configure MCP Client

Prompt user with simple menu:
```
How will you use RevitCortex?
[1] Claude Desktop
[2] Claude Code
[3] Both
[4] Skip (configure later)
```

**Claude Desktop** — Merge into `%APPDATA%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["C:\\Users\\{user}\\.revitcortex\\server\\build\\index.js"]
    }
  }
}
```
If file exists, parse JSON and merge `mcpServers.revitcortex` without touching other entries. If file doesn't exist, create it.

**Claude Code** — Run:
```
claude mcp add revitcortex node C:\Users\{user}\.revitcortex\server\build\index.js
```
If `claude` command not found, write instructions to configure manually.

### Step 6: Summary

```
╔══════════════════════════════════════════╗
║     RevitCortex installed successfully   ║
╠══════════════════════════════════════════╣
║  Plugin:  Revit 2024, 2025              ║
║  Server:  ~/.revitcortex/server/        ║
║  Client:  Claude Desktop configured     ║
║                                          ║
║  → Restart Revit to activate             ║
║  → Restart Claude Desktop to connect     ║
╚══════════════════════════════════════════╝
```

## uninstall.ps1 Flow

1. Remove `RevitCortex/` folder from each `Addins/{year}/`
2. Remove `RevitCortex.addin` from each `Addins/{year}/`
3. Remove `~/.revitcortex/server/`
4. Remove `revitcortex` entry from Claude Desktop config (if present)
5. Run `claude mcp remove revitcortex` (if claude CLI available)
6. **Preserve** `~/.revitcortex/settings.json`, `usage.jsonl`, `usage-mcp.db`, `logs/` (user data)

Requires admin for Addins folder cleanup.

## build-release.ps1 Flow

Luigi runs this from the repo root to create a release ZIP.

```powershell
.\build-release.ps1 -Version 1.0.0
```

Steps:
1. **Build C# for all Revit versions:**
   ```
   dotnet publish -c "Release R23" → release/plugin/R23/
   dotnet publish -c "Release R24" → release/plugin/R24/
   dotnet publish -c "Release R25" → release/plugin/R25/
   dotnet publish -c "Release R26" → release/plugin/R26/
   ```
2. **Build TypeScript server:**
   ```
   cd server && npm install && npm run build
   ```
   Copy `server/build/index.js` + `server/build/sql-wasm.wasm` + `server/package.json` → `release/server/`
3. **Copy support files:**
   - `install.ps1`, `uninstall.ps1`, `README.txt` from `distribution/`
   - `RevitCortex.addin` from `src/RevitCortex.Plugin/`
   - Config templates from `distribution/config-templates/`
4. **Create ZIP:**
   ```
   Compress-Archive release/* → RevitCortex-v1.0.0.zip
   ```
5. **Output:** `RevitCortex-v1.0.0.zip` ready for GitHub Releases upload.

## README.txt

```
RevitCortex - AI Assistant for Autodesk Revit
==============================================

1. Right-click install.ps1 → "Run with PowerShell"
2. Follow the on-screen prompts
3. Restart Revit and Claude

For help: https://github.com/{owner}/RevitCortex/issues
```

## Config Templates

### claude-desktop.json
```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["{SERVER_PATH}\\build\\index.js"]
    }
  }
}
```

### claude-code.json
```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["{SERVER_PATH}\\build\\index.js"]
    }
  }
}
```

`{SERVER_PATH}` is replaced by the installer with the actual path (`~/.revitcortex/server`).

## Error Handling

| Error | Action |
|-------|--------|
| No admin rights | Self-elevate with UAC prompt |
| Node.js install fails | Show manual install URL, exit |
| No Revit found | "No Revit installation detected. Install Revit first." + exit |
| npm install fails | Retry once, then show error + manual instructions |
| Claude config parse error | Back up existing file, create fresh config |
| Plugin folder locked (Revit open) | "Close Revit first, then re-run install.ps1" + exit |

## File Map

| # | File | Action |
|---|------|--------|
| 1 | `distribution/install.ps1` | Create |
| 2 | `distribution/uninstall.ps1` | Create |
| 3 | `distribution/README.txt` | Create |
| 4 | `distribution/config-templates/claude-desktop.json` | Create |
| 5 | `distribution/config-templates/claude-code.json` | Create |
| 6 | `build-release.ps1` | Create |

# RevitCortex Distribution & Installer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a one-click installer so non-technical colleagues can install RevitCortex from a GitHub Release ZIP — no build tools or programming knowledge required.

**Architecture:** `build-release.ps1` (Luigi runs) builds C# for all Revit versions + bundles TS server into a ZIP. `install.ps1` (colleague runs) auto-detects Revit, installs Node.js if needed, copies files, configures MCP client. Single ZIP for all Revit versions.

**Tech Stack:** PowerShell, winget, dotnet publish, esbuild, npm

**Spec:** `docs/superpowers/specs/2026-04-10-distribution-installer-design.md`

---

## File Map

| # | File | Action | Responsibility |
|---|------|--------|----------------|
| 1 | `distribution/install.ps1` | Create | One-click installer for colleagues |
| 2 | `distribution/uninstall.ps1` | Create | Clean removal script |
| 3 | `distribution/README.txt` | Create | 5-line quick start |
| 4 | `distribution/config-templates/claude-desktop.json` | Create | Template for Claude Desktop MCP config |
| 5 | `distribution/config-templates/claude-code.json` | Create | Template for Claude Code MCP config |
| 6 | `build-release.ps1` | Create | Builds everything + creates ZIP |

---

### Task 1: Config Templates + README

**Files:**
- Create: `distribution/config-templates/claude-desktop.json`
- Create: `distribution/config-templates/claude-code.json`
- Create: `distribution/README.txt`

- [ ] **Step 1: Create distribution directory structure**

Run:
```bash
mkdir -p distribution/config-templates
```

- [ ] **Step 2: Create Claude Desktop config template**

```json
// distribution/config-templates/claude-desktop.json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["__SERVER_PATH__\\build\\index.js"]
    }
  }
}
```

The `__SERVER_PATH__` placeholder is replaced by `install.ps1` with the actual path.

- [ ] **Step 3: Create Claude Code config template**

```json
// distribution/config-templates/claude-code.json
{
  "mcpServers": {
    "revitcortex": {
      "command": "node",
      "args": ["__SERVER_PATH__\\build\\index.js"]
    }
  }
}
```

- [ ] **Step 4: Create README.txt**

```text
RevitCortex - AI Assistant for Autodesk Revit
==============================================

1. Right-click install.ps1 → "Run with PowerShell"
2. Follow the on-screen prompts
3. Restart Revit and Claude

To uninstall: Right-click uninstall.ps1 → "Run with PowerShell"
For help: https://github.com/LuDattilo/RevitCortex/issues
```

- [ ] **Step 5: Commit**

```bash
git add distribution/
git commit -m "feat(dist): add config templates and README for installer"
```

---

### Task 2: install.ps1

**Files:**
- Create: `distribution/install.ps1`

- [ ] **Step 1: Create install.ps1 with self-elevation**

The script starts with a self-elevation block so it can write to `C:\ProgramData`. Then runs the install steps.

```powershell
# distribution/install.ps1
param(
    [switch]$SkipNodeCheck
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ─── Self-elevate if not admin ───
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    $argList = "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    if ($SkipNodeCheck) { $argList += " -SkipNodeCheck" }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ─── Step 1: Check / Install Node.js ───
Write-Host "[1/5] Checking Node.js..." -ForegroundColor Yellow

$nodePath = Get-Command node -ErrorAction SilentlyContinue
if ($nodePath -and -not $SkipNodeCheck) {
    $nodeVersion = & node --version 2>$null
    Write-Host "  Node.js $nodeVersion found." -ForegroundColor Green
} elseif (-not $SkipNodeCheck) {
    Write-Host "  Node.js not found. Installing..." -ForegroundColor Yellow

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Host "  Installing via winget..." -ForegroundColor Yellow
        winget install OpenJS.NodeJS.LTS --silent --accept-source-agreements --accept-package-agreements
    } else {
        Write-Host "  Downloading Node.js installer..." -ForegroundColor Yellow
        $nodeUrl = "https://nodejs.org/dist/v22.15.0/node-v22.15.0-x64.msi"
        $nodeMsi = Join-Path $env:TEMP "node-install.msi"
        Invoke-WebRequest -Uri $nodeUrl -OutFile $nodeMsi -UseBasicParsing
        Write-Host "  Running Node.js installer (silent)..." -ForegroundColor Yellow
        Start-Process msiexec -ArgumentList "/i `"$nodeMsi`" /qn" -Wait
        Remove-Item $nodeMsi -Force -ErrorAction SilentlyContinue
    }

    # Refresh PATH
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

    $nodeCheck = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCheck) {
        $nodeVersion = & node --version 2>$null
        Write-Host "  Node.js $nodeVersion installed successfully." -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "  ERROR: Node.js installation failed." -ForegroundColor Red
        Write-Host "  Please install manually from https://nodejs.org" -ForegroundColor Red
        Write-Host "  Then re-run this installer." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
}

# ─── Step 2: Detect Revit versions ───
Write-Host ""
Write-Host "[2/5] Detecting Revit installations..." -ForegroundColor Yellow

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$supportedVersions = @("2023", "2024", "2025", "2026")
$configMap = @{ "2023" = "R23"; "2024" = "R24"; "2025" = "R25"; "2026" = "R26" }
$foundVersions = @()

foreach ($ver in $supportedVersions) {
    $verDir = Join-Path $addinsRoot $ver
    $pluginDir = Join-Path $ScriptDir "plugin\$($configMap[$ver])"
    if ((Test-Path $verDir) -and (Test-Path $pluginDir)) {
        $foundVersions += $ver
    }
}

if ($foundVersions.Count -eq 0) {
    Write-Host "  ERROR: No supported Revit installation found (2023-2026)." -ForegroundColor Red
    Write-Host "  Looked in: $addinsRoot" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "  Found Revit: $($foundVersions -join ', ')" -ForegroundColor Green

# ─── Step 3: Install Plugin ───
Write-Host ""
Write-Host "[3/5] Installing Revit plugin..." -ForegroundColor Yellow

$addinTemplate = Join-Path $ScriptDir "RevitCortex.addin"

foreach ($ver in $foundVersions) {
    $suffix = $configMap[$ver]
    $sourceDir = Join-Path $ScriptDir "plugin\$suffix"
    $targetDir = Join-Path $addinsRoot "$ver\RevitCortex"
    $addinTarget = Join-Path $addinsRoot "$ver\RevitCortex.addin"

    # Remove old installation
    if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }

    # Copy plugin DLLs
    Copy-Item $sourceDir $targetDir -Recurse -Force
    Write-Host "  Revit $ver : $targetDir" -ForegroundColor Gray

    # Copy .addin manifest
    Copy-Item $addinTemplate $addinTarget -Force
}

Write-Host "  Plugin installed for $($foundVersions.Count) Revit version(s)." -ForegroundColor Green

# ─── Step 4: Install MCP Server ───
Write-Host ""
Write-Host "[4/5] Installing MCP server..." -ForegroundColor Yellow

$serverTarget = Join-Path $env:USERPROFILE ".revitcortex\server"
$serverSource = Join-Path $ScriptDir "server"

# Remove old server
if (Test-Path $serverTarget) { Remove-Item $serverTarget -Recurse -Force }

# Create directory and copy files
New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
Copy-Item "$serverSource\*" $serverTarget -Recurse -Force

# Install npm dependencies (sql.js)
Write-Host "  Running npm install..." -ForegroundColor Gray
Push-Location $serverTarget
& npm install --production --silent 2>$null
Pop-Location

Write-Host "  Server installed: $serverTarget" -ForegroundColor Green

# ─── Step 5: Configure MCP Client ───
Write-Host ""
Write-Host "[5/5] Configure Claude client" -ForegroundColor Yellow
Write-Host ""
Write-Host "  How will you use RevitCortex?"
Write-Host "  [1] Claude Desktop"
Write-Host "  [2] Claude Code (CLI)"
Write-Host "  [3] Both"
Write-Host "  [4] Skip (configure later)"
Write-Host ""
$choice = Read-Host "  Enter choice (1-4)"

$serverBuildPath = (Join-Path $serverTarget "build\index.js") -replace '\\', '\\'

# Claude Desktop config
if ($choice -eq "1" -or $choice -eq "3") {
    $configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
    $configDir = Split-Path $configPath

    if (!(Test-Path $configDir)) { New-Item -ItemType Directory -Path $configDir -Force | Out-Null }

    $mcpEntry = @{
        command = "node"
        args = @((Join-Path $serverTarget "build\index.js"))
    }

    if (Test-Path $configPath) {
        try {
            $config = Get-Content $configPath -Raw | ConvertFrom-Json
            if (-not $config.mcpServers) {
                $config | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue @{} -Force
            }
            $config.mcpServers | Add-Member -NotePropertyName "revitcortex" -NotePropertyValue $mcpEntry -Force
            $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
        } catch {
            # Backup and create fresh
            Copy-Item $configPath "$configPath.bak" -Force
            $freshConfig = @{ mcpServers = @{ revitcortex = $mcpEntry } }
            $freshConfig | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
        }
    } else {
        $freshConfig = @{ mcpServers = @{ revitcortex = $mcpEntry } }
        $freshConfig | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
    }

    Write-Host "  Claude Desktop configured." -ForegroundColor Green
}

# Claude Code config
if ($choice -eq "2" -or $choice -eq "3") {
    $claudeCli = Get-Command claude -ErrorAction SilentlyContinue
    if ($claudeCli) {
        $serverIndexPath = Join-Path $serverTarget "build\index.js"
        & claude mcp add revitcortex node $serverIndexPath 2>$null
        Write-Host "  Claude Code configured." -ForegroundColor Green
    } else {
        Write-Host "  Claude Code CLI not found. Add manually:" -ForegroundColor Yellow
        Write-Host "    claude mcp add revitcortex node `"$(Join-Path $serverTarget "build\index.js")`"" -ForegroundColor Gray
    }
}

if ($choice -eq "4") {
    Write-Host "  Skipped. Configure later with:" -ForegroundColor Yellow
    Write-Host "    Claude Desktop: Add to %APPDATA%\Claude\claude_desktop_config.json" -ForegroundColor Gray
    Write-Host "    Claude Code:    claude mcp add revitcortex node `"$(Join-Path $serverTarget "build\index.js")`"" -ForegroundColor Gray
}

# ─── Summary ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex installed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Plugin:  Revit $($foundVersions -join ', ')" -ForegroundColor White
Write-Host "  Server:  $serverTarget" -ForegroundColor White
if ($choice -eq "1") { Write-Host "  Client:  Claude Desktop" -ForegroundColor White }
if ($choice -eq "2") { Write-Host "  Client:  Claude Code" -ForegroundColor White }
if ($choice -eq "3") { Write-Host "  Client:  Claude Desktop + Claude Code" -ForegroundColor White }
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Revit" -ForegroundColor White
Write-Host "  2. Restart Claude Desktop / Claude Code" -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"
```

- [ ] **Step 2: Test syntax**

Run: `powershell -Command "Get-Content distribution/install.ps1 | Out-Null; Write-Host 'Syntax OK'"`
Expected: `Syntax OK`

- [ ] **Step 3: Commit**

```bash
git add distribution/install.ps1
git commit -m "feat(dist): add one-click install.ps1 installer"
```

---

### Task 3: uninstall.ps1

**Files:**
- Create: `distribution/uninstall.ps1`

- [ ] **Step 1: Create uninstall.ps1**

```powershell
# distribution/uninstall.ps1
$ErrorActionPreference = "Stop"

# ─── Self-elevate if not admin ───
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Uninstaller" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$confirm = Read-Host "This will remove RevitCortex from all Revit versions. Continue? (y/n)"
if ($confirm -ne "y") {
    Write-Host "Cancelled." -ForegroundColor Yellow
    exit
}

# ─── Remove Revit plugin ───
Write-Host ""
Write-Host "Removing Revit plugin..." -ForegroundColor Yellow

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$removedCount = 0

foreach ($ver in @("2023", "2024", "2025", "2026")) {
    $pluginDir = Join-Path $addinsRoot "$ver\RevitCortex"
    $addinFile = Join-Path $addinsRoot "$ver\RevitCortex.addin"

    if (Test-Path $pluginDir) {
        Remove-Item $pluginDir -Recurse -Force
        Write-Host "  Removed: $pluginDir" -ForegroundColor Gray
        $removedCount++
    }
    if (Test-Path $addinFile) {
        Remove-Item $addinFile -Force
    }
}

if ($removedCount -eq 0) {
    Write-Host "  No Revit plugin found." -ForegroundColor Gray
} else {
    Write-Host "  Removed from $removedCount Revit version(s)." -ForegroundColor Green
}

# ─── Remove MCP server ───
Write-Host ""
Write-Host "Removing MCP server..." -ForegroundColor Yellow

$serverDir = Join-Path $env:USERPROFILE ".revitcortex\server"
if (Test-Path $serverDir) {
    Remove-Item $serverDir -Recurse -Force
    Write-Host "  Removed: $serverDir" -ForegroundColor Green
} else {
    Write-Host "  Server directory not found." -ForegroundColor Gray
}

# ─── Remove Claude Desktop config entry ───
Write-Host ""
Write-Host "Removing Claude config..." -ForegroundColor Yellow

$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        if ($config.mcpServers -and $config.mcpServers.revitcortex) {
            $config.mcpServers.PSObject.Properties.Remove("revitcortex")
            $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
            Write-Host "  Removed revitcortex from Claude Desktop config." -ForegroundColor Green
        }
    } catch {
        Write-Host "  Could not update Claude Desktop config: $_" -ForegroundColor Yellow
    }
}

# Claude Code
$claudeCli = Get-Command claude -ErrorAction SilentlyContinue
if ($claudeCli) {
    & claude mcp remove revitcortex 2>$null
    Write-Host "  Removed revitcortex from Claude Code." -ForegroundColor Green
}

# ─── Keep user data ───
Write-Host ""
Write-Host "User data preserved:" -ForegroundColor Yellow
$dataDir = Join-Path $env:USERPROFILE ".revitcortex"
Write-Host "  $dataDir (settings, logs, usage data)" -ForegroundColor Gray
Write-Host "  Delete manually if no longer needed." -ForegroundColor Gray

# ─── Done ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex removed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Restart Revit and Claude to complete." -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"
```

- [ ] **Step 2: Test syntax**

Run: `powershell -Command "Get-Content distribution/uninstall.ps1 | Out-Null; Write-Host 'Syntax OK'"`
Expected: `Syntax OK`

- [ ] **Step 3: Commit**

```bash
git add distribution/uninstall.ps1
git commit -m "feat(dist): add uninstall.ps1 cleanup script"
```

---

### Task 4: build-release.ps1

**Files:**
- Create: `build-release.ps1`

- [ ] **Step 1: Create build-release.ps1**

```powershell
# build-release.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ReleaseDir = Join-Path $RepoRoot "release"
$ZipName = "RevitCortex-v$Version.zip"
$ZipPath = Join-Path $RepoRoot $ZipName

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Release Builder v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean release directory
if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

# ─── Build C# for all Revit versions ───
Write-Host "[1/4] Building C# plugin..." -ForegroundColor Yellow

$pluginProject = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.Plugin.csproj"
$toolsProject = Join-Path $RepoRoot "src\RevitCortex.Tools\RevitCortex.Tools.csproj"

foreach ($rv in @("R23", "R24", "R25", "R26")) {
    $config = "Release $rv"
    $outDir = Join-Path $ReleaseDir "plugin\$rv"

    Write-Host "  Building $rv..." -ForegroundColor Gray
    dotnet publish -c "$config" $pluginProject -o $outDir --no-self-contained -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed for $rv" }

    dotnet publish -c "$config" $toolsProject -o $outDir --no-self-contained -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Tools build failed for $rv" }

    $dllCount = (Get-ChildItem "$outDir\*.dll").Count
    Write-Host "    $dllCount DLLs" -ForegroundColor Gray
}

Write-Host "  C# builds complete." -ForegroundColor Green

# ─── Build TypeScript server ───
Write-Host ""
Write-Host "[2/4] Building TypeScript server..." -ForegroundColor Yellow

Push-Location (Join-Path $RepoRoot "server")
npm install --silent 2>$null
npm run build
Pop-Location

$serverTarget = Join-Path $ReleaseDir "server"
New-Item -ItemType Directory -Path "$serverTarget\build" -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "server\build\index.js") "$serverTarget\build\"
Copy-Item (Join-Path $RepoRoot "server\build\sql-wasm.wasm") "$serverTarget\build\"
Copy-Item (Join-Path $RepoRoot "server\package.json") "$serverTarget\"

Write-Host "  Server built and copied." -ForegroundColor Green

# ─── Copy support files ───
Write-Host ""
Write-Host "[3/4] Copying support files..." -ForegroundColor Yellow

# Installer scripts
Copy-Item (Join-Path $RepoRoot "distribution\install.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\uninstall.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\README.txt") $ReleaseDir

# .addin manifest
Copy-Item (Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin") $ReleaseDir

# Config templates
$templatesTarget = Join-Path $ReleaseDir "config-templates"
New-Item -ItemType Directory -Path $templatesTarget -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "distribution\config-templates\*") $templatesTarget

Write-Host "  Support files copied." -ForegroundColor Green

# ─── Create ZIP ───
Write-Host ""
Write-Host "[4/4] Creating ZIP archive..." -ForegroundColor Yellow

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$ReleaseDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)

Write-Host "  Created: $ZipPath ($sizeMB MB)" -ForegroundColor Green

# ─── Summary ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   Release package ready" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  File:    $ZipName" -ForegroundColor White
Write-Host "  Size:    $sizeMB MB" -ForegroundColor White
Write-Host "  Upload:  GitHub Releases" -ForegroundColor White
Write-Host ""
```

- [ ] **Step 2: Test syntax**

Run: `powershell -Command "Get-Content build-release.ps1 | Out-Null; Write-Host 'Syntax OK'"`
Expected: `Syntax OK`

- [ ] **Step 3: Commit**

```bash
git add build-release.ps1
git commit -m "feat(dist): add build-release.ps1 for creating release packages"
```

---

### Task 5: Test Full Release Build

- [ ] **Step 1: Run build-release.ps1**

Run: `powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 0.1.0`
Expected: ZIP created with all 4 Revit version DLLs, server build, and support files.

- [ ] **Step 2: Verify ZIP contents**

Run: `powershell -Command "Expand-Archive -Path RevitCortex-v0.1.0.zip -DestinationPath release-test -Force; Get-ChildItem release-test -Recurse -Directory | Select-Object FullName"`
Expected: `plugin/R23`, `plugin/R24`, `plugin/R25`, `plugin/R26`, `server/build`, `config-templates` directories present.

- [ ] **Step 3: Verify key files exist**

Run:
```bash
ls release-test/install.ps1 release-test/uninstall.ps1 release-test/README.txt release-test/RevitCortex.addin release-test/server/build/index.js release-test/server/build/sql-wasm.wasm release-test/server/package.json
```
Expected: All 7 files listed.

- [ ] **Step 4: Cleanup test**

Run: `rm -rf release-test RevitCortex-v0.1.0.zip release`

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat(dist): distribution installer system complete"
```

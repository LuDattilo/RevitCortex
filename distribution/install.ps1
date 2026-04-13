param(
    [switch]$SkipNodeCheck
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- Self-elevate if not admin ---
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

# --- Step 1: Check / Install Node.js ---
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

# --- Step 2: Detect Revit versions ---
Write-Host ""
Write-Host "[2/5] Detecting Revit installations..." -ForegroundColor Yellow

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$supportedVersions = @("2023", "2024", "2025", "2026", "2027")
$configMap = @{ "2023" = "R23"; "2024" = "R24"; "2025" = "R25"; "2026" = "R26"; "2027" = "R27" }
$foundVersions = @()

foreach ($ver in $supportedVersions) {
    $verDir = Join-Path $addinsRoot $ver
    $pluginDir = Join-Path $ScriptDir "plugin\$($configMap[$ver])"
    if ((Test-Path $verDir) -and (Test-Path $pluginDir)) {
        $foundVersions += $ver
    }
}

if ($foundVersions.Count -eq 0) {
    Write-Host "  ERROR: No supported Revit installation found (2023-2027)." -ForegroundColor Red
    Write-Host "  Looked in: $addinsRoot" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "  Found Revit: $($foundVersions -join ', ')" -ForegroundColor Green

# --- Step 3: Install Plugin ---
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

# --- Step 4: Install MCP Server ---
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

# --- Step 5: Configure MCP Client ---
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

# --- Summary ---
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

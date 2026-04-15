$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- Self-elevate if not admin ---
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Detect Revit versions ---
Write-Host "[1/4] Detecting Revit installations..." -ForegroundColor Yellow

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

# --- Step 2: Install Plugin ---
Write-Host ""
Write-Host "[2/4] Installing Revit plugin..." -ForegroundColor Yellow

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

# --- Step 3: Install MCP Server ---
Write-Host ""
Write-Host "[3/4] Installing MCP server..." -ForegroundColor Yellow

$serverSource = Join-Path $ScriptDir "server"
$serverTarget = Join-Path $env:USERPROFILE ".revitcortex\server"

if (!(Test-Path $serverSource)) {
    Write-Host "  ERROR: Server files not found at $serverSource" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Remove old server
if (Test-Path $serverTarget) { Remove-Item $serverTarget -Recurse -Force }

# Create directory and copy files
New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
Copy-Item "$serverSource\*" $serverTarget -Recurse -Force

$serverExe = Join-Path $serverTarget "RevitCortex.Server.exe"
Write-Host "  Server installed: $serverExe" -ForegroundColor Green

# --- Step 4: Configure MCP Client ---
Write-Host ""
Write-Host "[4/4] Configure Claude client" -ForegroundColor Yellow
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
        command = $serverExe
        args    = @()
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
        & claude mcp add revitcortex $serverExe 2>$null
        Write-Host "  Claude Code configured." -ForegroundColor Green
    } else {
        Write-Host "  Claude Code CLI not found. Add manually:" -ForegroundColor Yellow
        Write-Host "    claude mcp add revitcortex `"$serverExe`"" -ForegroundColor Gray
    }
}

if ($choice -eq "4") {
    Write-Host "  Skipped. Configure later:" -ForegroundColor Yellow
    Write-Host "    Claude Desktop: add to %APPDATA%\Claude\claude_desktop_config.json" -ForegroundColor Gray
    Write-Host "    Claude Code:    claude mcp add revitcortex `"$serverExe`"" -ForegroundColor Gray
}

# --- Summary ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex installed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Plugin:  Revit $($foundVersions -join ', ')" -ForegroundColor White
Write-Host "  Server:  $serverExe" -ForegroundColor White
if ($choice -eq "1") { Write-Host "  Client:  Claude Desktop" -ForegroundColor White }
if ($choice -eq "2") { Write-Host "  Client:  Claude Code" -ForegroundColor White }
if ($choice -eq "3") { Write-Host "  Client:  Claude Desktop + Claude Code" -ForegroundColor White }
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Revit" -ForegroundColor White
Write-Host "  2. Restart Claude Desktop / Claude Code" -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"

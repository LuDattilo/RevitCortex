$ErrorActionPreference = "Stop"

# --- Self-elevate if not admin ---
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

# --- Remove Revit plugin ---
Write-Host ""
Write-Host "Removing Revit plugin..." -ForegroundColor Yellow

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$removedCount = 0

foreach ($ver in @("2023", "2024", "2025", "2026", "2027")) {
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

# --- Remove MCP server ---
Write-Host ""
Write-Host "Removing MCP server..." -ForegroundColor Yellow

$serverDir = Join-Path $env:USERPROFILE ".revitcortex\server"
if (Test-Path $serverDir) {
    Remove-Item $serverDir -Recurse -Force
    Write-Host "  Removed: $serverDir" -ForegroundColor Green
} else {
    Write-Host "  Server directory not found." -ForegroundColor Gray
}

# --- Remove Claude Desktop config entry ---
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

# --- Keep user data ---
Write-Host ""
Write-Host "User data preserved:" -ForegroundColor Yellow
$dataDir = Join-Path $env:USERPROFILE ".revitcortex"
Write-Host "  $dataDir (settings, logs, usage data)" -ForegroundColor Gray
Write-Host "  Delete manually if no longer needed." -ForegroundColor Gray

# --- Done ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex removed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Restart Revit and Claude to complete." -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"

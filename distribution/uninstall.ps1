#requires -Version 5.1
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $ScriptDir 'lib\ClaudeConfig.ps1')
. (Join-Path $ScriptDir 'lib\RevitDeploy.ps1')

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
if ($confirm -ne "y") { Write-Host "Cancelled." -ForegroundColor Yellow; exit }

# --- Revit plugin (both scopes) ---
Write-Host ""
Write-Host "Removing Revit plugin..." -ForegroundColor Yellow
$totalRemoved = 0
foreach ($ver in @("2023","2024","2025","2026","2027")) {
    $removed = Remove-RevitAddin -Version $ver
    foreach ($path in $removed) {
        Write-Host "  Removed: $path" -ForegroundColor Gray
        $totalRemoved++
    }
}
if ($totalRemoved -eq 0) { Write-Host "  No Revit plugin found." -ForegroundColor Gray }
else { Write-Host "  Removed $totalRemoved plugin folder(s)." -ForegroundColor Green }

# --- MCP server ---
Write-Host ""
Write-Host "Removing MCP server..." -ForegroundColor Yellow
$serverDir = Join-Path $env:USERPROFILE ".revitcortex\server"
if (Test-Path $serverDir) {
    Remove-Item $serverDir -Recurse -Force
    Write-Host "  Removed: $serverDir" -ForegroundColor Green
} else {
    Write-Host "  Server directory not found." -ForegroundColor Gray
}

# --- Claude Desktop config entry (safe, preserves other MCP servers) ---
Write-Host ""
Write-Host "Removing Claude Desktop config entry..." -ForegroundColor Yellow
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
try {
    $result = Remove-ClaudeMcpServer -ConfigPath $configPath -ServerName 'revitcortex'
    if ($result.Action -eq 'removed') {
        Write-Host "  revitcortex entry removed" -ForegroundColor Green
        if ($result.BackupPath) { Write-Host ("  Backup: {0}" -f $result.BackupPath) -ForegroundColor Gray }
    } else {
        Write-Host "  revitcortex entry not found (nothing to remove)" -ForegroundColor Gray
    }
} catch {
    Write-Host "  Claude Desktop config update FAILED: $_" -ForegroundColor Yellow
}

# --- Claude Code ---
$claudeCli = Get-Command claude -ErrorAction SilentlyContinue
if ($claudeCli) {
    try { & claude mcp remove revitcortex 2>$null | Out-Null; Write-Host "  Claude Code: revitcortex removed." -ForegroundColor Green } catch {}
}

# --- User data preserved ---
Write-Host ""
Write-Host "User data preserved:" -ForegroundColor Yellow
$dataDir = Join-Path $env:USERPROFILE ".revitcortex"
Write-Host "  $dataDir (settings, logs, usage data)" -ForegroundColor Gray
Write-Host "  Delete manually if no longer needed." -ForegroundColor Gray

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex removed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Restart Revit and Claude to complete." -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"

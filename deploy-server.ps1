#Requires -Version 5.1
<#
.SYNOPSIS
  Rebuild and deploy the RevitCortex MCP server (~/.revitcortex/server).
  Run this AFTER closing Claude Desktop, otherwise the running .exe is locked.

.NOTES
  - Self-contained publish (mandatory: framework-dependent breaks the exe — see memory).
  - Reopen Claude Desktop after this script finishes.
#>

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ServerProject = Join-Path $RepoRoot "src\RevitCortex.Server\RevitCortex.Server.csproj"
$ServerTarget  = Join-Path $env:USERPROFILE ".revitcortex\server"

Write-Host "=== RevitCortex MCP Server Deploy ===" -ForegroundColor Cyan
Write-Host "Target: $ServerTarget"
Write-Host ""

# Sanity: warn if Claude Desktop is running (file lock will fail)
$claude = Get-Process "Claude" -ErrorAction SilentlyContinue
if ($claude) {
    Write-Host "WARNING: Claude Desktop is still running — close it before continuing." -ForegroundColor Red
    Write-Host "         The deploy will fail with 'file in use' otherwise."
    $r = Read-Host "Continue anyway? (y/N)"
    if ($r -ne "y") { exit 1 }
}

# Sanity: warn if any orphan RevitCortex.Server.exe
$orphans = Get-Process "RevitCortex.Server" -ErrorAction SilentlyContinue
if ($orphans) {
    Write-Host "Killing $($orphans.Count) orphan RevitCortex.Server processes..." -ForegroundColor Yellow
    $orphans | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# Wipe old install (avoids the publish-mode-mix trap)
if (Test-Path $ServerTarget) {
    Write-Host "Wiping old server install..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $ServerTarget
}

# Self-contained publish
Write-Host "Publishing self-contained server (Release, win-x64)..." -ForegroundColor Yellow
dotnet publish $ServerProject -c Release -o $ServerTarget --self-contained true -r win-x64 -v quiet
if ($LASTEXITCODE -ne 0) { throw "MCP server publish failed" }

$exe = Join-Path $ServerTarget "RevitCortex.Server.exe"
if (!(Test-Path $exe)) { throw "Server executable not found after publish: $exe" }

$ts = (Get-Item $exe).LastWriteTime
$dllCount = (Get-ChildItem "$ServerTarget\*.dll").Count

Write-Host ""
Write-Host "=== Deploy complete ===" -ForegroundColor Green
Write-Host "Exe: $exe"
Write-Host "Build time: $ts"
Write-Host "$dllCount DLLs deployed."
Write-Host ""
Write-Host "Now reopen Claude Desktop to load the new server." -ForegroundColor Cyan

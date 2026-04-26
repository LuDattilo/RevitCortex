#Requires -Version 5.1
<#
.SYNOPSIS
  Diagnose shadow / duplicate RevitCortex installations across machine and user
  Revit addin folders. Detects the silent failure mode where a stale user-scope
  copy in %AppData%\Autodesk\Revit\Addins shadows a newer machine-scope deploy,
  causing "I deployed but Revit shows the old version".

.NOTES
  Read-only — does not modify anything. To fix duplicates, run deploy.ps1 (dev)
  or distribution\install.ps1 (release), both of which now wipe the opposite scope.
#>

$ErrorActionPreference = "Stop"

$versions = @("2023","2024","2025","2026","2027")
$machineRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$userRoot    = Join-Path $env:APPDATA "Autodesk\Revit\Addins"

Write-Host "=== RevitCortex install diagnostic ===" -ForegroundColor Cyan
Write-Host ""

$anyDup = $false

foreach ($ver in $versions) {
    $machineDll = Join-Path $machineRoot "$ver\RevitCortex\RevitCortex.Plugin.dll"
    $userDll    = Join-Path $userRoot    "$ver\RevitCortex\RevitCortex.Plugin.dll"

    $hasMachine = Test-Path $machineDll
    $hasUser    = Test-Path $userDll

    if (-not $hasMachine -and -not $hasUser) { continue }

    Write-Host ("Revit {0}:" -f $ver) -ForegroundColor White

    if ($hasMachine) {
        $info = Get-Item $machineDll
        Write-Host ("  machine : {0,7} bytes  {1:yyyy-MM-dd HH:mm}  {2}" -f $info.Length, $info.LastWriteTime, $machineDll) -ForegroundColor Gray
    }
    if ($hasUser) {
        $info = Get-Item $userDll
        Write-Host ("  user    : {0,7} bytes  {1:yyyy-MM-dd HH:mm}  {2}" -f $info.Length, $info.LastWriteTime, $userDll) -ForegroundColor Gray
    }

    if ($hasMachine -and $hasUser) {
        $anyDup = $true
        Write-Host "  WARNING: BOTH scopes contain a RevitCortex install. The user-scope copy may shadow machine-scope at runtime." -ForegroundColor Red
        Write-Host "  Fix: run deploy.ps1 (dev) or distribution\install.ps1 (release) to consolidate." -ForegroundColor Yellow
    }
    Write-Host ""
}

if (-not $anyDup) {
    Write-Host "No shadow installs detected." -ForegroundColor Green
}

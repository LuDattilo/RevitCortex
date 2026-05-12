#requires -Version 5.0
<#
.SYNOPSIS
  Cleans up the machine-scope (C:\ProgramData) RevitCortex deploy for Revit
  2026 so only user-scope (%APPDATA%) is active.

.DESCRIPTION
  RevitCortex can be deployed to two locations:
    - User scope:    %APPDATA%\Autodesk\Revit\Addins\2026\RevitCortex\
    - Machine scope: C:\ProgramData\Autodesk\Revit\Addins\2026\RevitCortex\

  Revit scans both. With matching .addin manifests in both scopes the
  machine-scope copy can shadow user-scope updates. This script self-elevates
  and either removes the machine-scope install entirely (-RemoveMachineScopeOnly)
  or updates its DLLs to match the latest build output.

.NOTES
  Default action: update machine-scope with the freshest DLLs from
  src\RevitCortex.Plugin\bin\Release R26 (no user-scope changes).
  With -RemoveMachineScopeOnly: deletes the machine-scope folder + manifest,
  leaving user-scope as the only install.
#>

param(
  [string]$RepoRoot,
  [switch]$RemoveMachineScopeOnly,
  [string]$RevitYear = "2026"
)

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".")).Path
}

# Self-elevate
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Re-launching with admin elevation..." -ForegroundColor Yellow
    $argsList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-RepoRoot", "`"$RepoRoot`"",
        "-RevitYear", "`"$RevitYear`""
    )
    if ($RemoveMachineScopeOnly) { $argsList += "-RemoveMachineScopeOnly" }
    Start-Process powershell -ArgumentList $argsList -Verb RunAs -Wait
    exit
}

$ErrorActionPreference = "Stop"

$RShort = "R" + $RevitYear.Substring(2)  # "2025" -> "R25"
$SrcPlugin = Join-Path $RepoRoot "src\RevitCortex.Plugin\bin\Release $RShort\net8.0-windows10.0.19041.0"
$SrcTools  = Join-Path $RepoRoot "src\RevitCortex.Tools\bin\Release $RShort\net8.0-windows10.0.19041.0"
$DstDir    = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear\RevitCortex"
$DstManifest = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear\RevitCortex.addin"

Write-Host "=== RevitCortex machine-scope sync ($RShort) ===" -ForegroundColor Cyan

if ($RemoveMachineScopeOnly) {
    Write-Host "Mode: REMOVE machine-scope. User-scope (APPDATA) will become the only install." -ForegroundColor Yellow

    $revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    if ($revit) {
        Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close it first." -ForegroundColor Red
        Start-Sleep -Seconds 3
        exit 1
    }

    if (Test-Path $DstManifest) {
        Write-Host "Removing $DstManifest" -ForegroundColor Yellow
        Remove-Item $DstManifest -Force
    } else {
        Write-Host "Manifest already absent: $DstManifest"
    }
    if (Test-Path $DstDir) {
        Write-Host "Removing $DstDir" -ForegroundColor Yellow
        Remove-Item $DstDir -Recurse -Force
    } else {
        Write-Host "Folder already absent: $DstDir"
    }
    Write-Host ""
    Write-Host "DONE. Restart Revit 2026 to reload from user-scope only." -ForegroundColor Green
    Start-Sleep -Seconds 2
    exit 0
}

# Update mode
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close it first." -ForegroundColor Red
    Start-Sleep -Seconds 3
    exit 1
}

if (-not (Test-Path $DstDir)) {
    Write-Host "Machine-scope folder doesn't exist. Nothing to sync." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    exit 0
}

$files = @(
    "$SrcPlugin\RevitCortex.Plugin.dll",
    "$SrcPlugin\RevitCortex.Plugin.pdb",
    "$SrcPlugin\RevitCortex.Core.dll",
    "$SrcPlugin\RevitCortex.Core.pdb",
    "$SrcTools\RevitCortex.Tools.dll",
    "$SrcTools\RevitCortex.Tools.pdb"
)

foreach ($f in $files) {
    if (Test-Path $f) {
        Copy-Item $f $DstDir -Force
        Write-Host "  Updated: $(Split-Path $f -Leaf)" -ForegroundColor Green
    } else {
        Write-Host "  Skipped (missing in build output): $(Split-Path $f -Leaf)" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "DONE. Restart Revit 2026 to load the updated DLLs." -ForegroundColor Green
Start-Sleep -Seconds 2

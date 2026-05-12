#requires -Version 5.0
<#
.SYNOPSIS
  One-shot build + deploy to user-scope addin folders for every Revit year.

.DESCRIPTION
  Per the team's deploy policy, every code change must reach all installed
  Revit versions, not just the one under test. This script:

    1. Builds RevitCortex.Plugin and RevitCortex.Tools for R23, R24, R25, R26
       (each in Release configuration). Skips any year whose .NET target
       isn't installed (warns and continues).
    2. Copies Plugin.dll + Plugin.pdb + Core.dll + Core.pdb + Tools.dll +
       Tools.pdb to %APPDATA%\Autodesk\Revit\Addins\<year>\RevitCortex\
       for each year, creating the folder if missing.
    3. Does NOT touch C:\ProgramData (machine-scope). If a stale machine-
       scope install is shadowing user-scope, run sync-machine-scope-r26.ps1
       -RemoveMachineScopeOnly -RevitYear <year> separately (requires admin).

.PARAMETER Years
  List of Revit years to deploy to. Defaults to all four (2023..2026).

.PARAMETER Config
  Build configuration prefix. Defaults to "Release". Use "Debug" for local
  iterations.

.EXAMPLE
  .\deploy-all-years.ps1
  # Builds Release R23/R24/R25/R26 and deploys to all four user-scope folders.

.EXAMPLE
  .\deploy-all-years.ps1 -Years 2025,2026 -Config Debug
  # Only R25 + R26 in Debug.
#>
param(
    [int[]]$Years = @(2023, 2024, 2025, 2026),
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

# Refuse to run while Revit holds DLL locks
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close it first." -ForegroundColor Red
    exit 1
}

function Get-Target([int]$year) {
    if ($year -lt 2025) { return "net48" }
    if ($year -le 2026) { return "net8.0-windows10.0.19041.0" }
    return "net10.0-windows7.0"  # R27 (untested here)
}

$summary = @()

foreach ($year in $Years) {
    $rShort = "R" + ($year.ToString().Substring(2))   # 2025 -> R25
    $configName = "$Config $rShort"                    # "Release R25"
    $target = Get-Target $year
    $dst = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year\RevitCortex"

    Write-Host ""
    Write-Host "=== $year ($rShort, $target) ===" -ForegroundColor Cyan

    # ── Build Plugin + Tools ───────────────────────────────────────────
    foreach ($proj in @("RevitCortex.Plugin", "RevitCortex.Tools")) {
        $csproj = Join-Path $RepoRoot "src\$proj\$proj.csproj"
        Write-Host "  Building $proj..." -ForegroundColor DarkGray
        $buildOut = & dotnet build -c $configName $csproj --nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("  BUILD FAILED for {0} {1}:" -f $proj, $rShort) -ForegroundColor Red
            $buildOut | Where-Object { $_ -match 'error' } | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $summary += [PSCustomObject]@{ Year = $year; Status = "BUILD-FAIL" }
            continue 2  # next year
        }
    }

    # ── Deploy ─────────────────────────────────────────────────────────
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force -Path $dst | Out-Null }

    $srcPlugin = Join-Path $RepoRoot "src\RevitCortex.Plugin\bin\$configName\$target"
    $srcTools  = Join-Path $RepoRoot "src\RevitCortex.Tools\bin\$configName\$target"

    $files = @(
        "$srcPlugin\RevitCortex.Plugin.dll",
        "$srcPlugin\RevitCortex.Plugin.pdb",
        "$srcPlugin\RevitCortex.Core.dll",
        "$srcPlugin\RevitCortex.Core.pdb",
        "$srcTools\RevitCortex.Tools.dll",
        "$srcTools\RevitCortex.Tools.pdb"
    )

    $copied = 0
    foreach ($f in $files) {
        if (Test-Path $f) {
            Copy-Item $f $dst -Force
            $copied++
        }
    }

    $pluginDll = Join-Path $dst "RevitCortex.Plugin.dll"
    $info = Get-Item $pluginDll -ErrorAction SilentlyContinue
    if ($info) {
        Write-Host ("  Deployed: {0} files, Plugin.dll {1:N0} B @ {2:HH:mm:ss}" -f $copied, $info.Length, $info.LastWriteTime) -ForegroundColor Green
        $summary += [PSCustomObject]@{ Year = $year; Status = "OK"; Files = $copied; Size = $info.Length; Time = $info.LastWriteTime }
    } else {
        Write-Host "  Deploy verification failed - no Plugin.dll at target" -ForegroundColor Red
        $summary += [PSCustomObject]@{ Year = $year; Status = "DEPLOY-FAIL" }
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$summary | Format-Table -AutoSize

# Reminder about machine-scope
$staleScopes = @()
foreach ($year in $Years) {
    if (Test-Path "C:\ProgramData\Autodesk\Revit\Addins\$year\RevitCortex.addin") {
        $staleScopes += $year
    }
}
if ($staleScopes.Count -gt 0) {
    Write-Host ""
    Write-Host "WARNING: Machine-scope install still present for Revit $($staleScopes -join ', ')." -ForegroundColor Yellow
    Write-Host "These can shadow user-scope. Clean with (per year):" -ForegroundColor Yellow
    Write-Host '  .\sync-machine-scope-r26.ps1 -RemoveMachineScopeOnly -RevitYear YYYY' -ForegroundColor Yellow
}

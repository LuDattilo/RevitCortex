# User-scope deploy workaround.
# ProgramData\...\RevitCortex is owned by BUILTIN\Administrators and the machine
# manifest is already neutralized (RevitCortex.addin.disabled). An unelevated
# deploy.ps1 cannot wipe ProgramData, and it doesn't need to: Revit loads the
# user-scope add-in from %APPDATA%. This deploys there for one Revit version.
#
# See memory: reference_userscope_deploy_workaround.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("2023", "2024", "2025", "2026", "2027")]
    [string]$RevitVersion,
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$short = "R$($RevitVersion.Substring(2))"
$Configuration = "$Config $short"
$PublishDir = Join-Path $RepoRoot "publish\$short"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$UserTargetDir = Join-Path $UserAddinsDir "RevitCortex"

Write-Host "=== RevitCortex User-Scope Deploy ($short) ===" -ForegroundColor Cyan

# Pre-flight: refuse while Revit runs (DLLs would be locked)
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) { Write-Host "ERROR: Revit running (PID $($revit.Id -join ', ')). Close it." -ForegroundColor Red; exit 1 }

$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) { $orphans | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500 }

# Clean publish dir then publish Plugin + Tools
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
Write-Host "Publishing Plugin..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }
Write-Host "Publishing Tools..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

# Wipe + recreate user-scope target so stale satellite assemblies don't survive
if (Test-Path $UserTargetDir) { Remove-Item $UserTargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $UserTargetDir -Force | Out-Null
Copy-Item "$PublishDir\*" $UserTargetDir -Recurse -Force

# Copy .addin manifest into user-scope addins root
$AddinSource = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin"
Copy-Item $AddinSource $UserAddinsDir -Force

$dllCount = (Get-ChildItem "$UserTargetDir\*.dll").Count
Write-Host "OK $short -> $UserTargetDir ($dllCount DLLs)" -ForegroundColor Green

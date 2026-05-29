# Deploy RevitCortex to USER-scope (%APPDATA%) for all Revit targets.
# Use when machine-scope (C:\ProgramData\...) is Administrators-owned and an
# unelevated deploy.ps1 cannot wipe it. The machine-scope .addin manifest must be
# disabled (renamed .disabled) so only the user-scope manifest loads — this script
# verifies that and refuses to run if a machine manifest is still active (double-load).
#
# Per memory reference_userscope_deploy_workaround. Dev-only.

param(
    [string[]]$Versions = @("2023","2024","2025","2026","2027"),
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$AddInId = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
$FullClassName = "RevitCortex.Plugin.RevitCortexApp"

# Pre-flight: Revit must be closed (DLLs locked otherwise).
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close it and re-run." -ForegroundColor Red
    exit 1
}
$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) { $orphans | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500 }

foreach ($v in $Versions) {
    $short = "R$($v.Substring(2))"
    $Configuration = "$Config $short"
    $PublishDir = Join-Path $RepoRoot "publish\$short"
    $UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
    $UserTargetDir = Join-Path $UserAddinsDir "RevitCortex"
    $machineManifest = "C:\ProgramData\Autodesk\Revit\Addins\$v\RevitCortex.addin"

    Write-Host "`n=== $v ($Configuration) ===" -ForegroundColor Cyan

    if (Test-Path $machineManifest) {
        Write-Host "  SKIP: machine-scope manifest is ACTIVE ($machineManifest). Disable it first (rename .disabled) to avoid double-load." -ForegroundColor Yellow
        continue
    }

    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  Plugin publish FAILED for $v" -ForegroundColor Red; continue }
    dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  Tools publish FAILED for $v" -ForegroundColor Red; continue }

    if (Test-Path $UserTargetDir) { Remove-Item $UserTargetDir -Recurse -Force }
    New-Item -ItemType Directory -Path $UserTargetDir -Force | Out-Null
    Copy-Item "$PublishDir\*" $UserTargetDir -Recurse -Force

    # Write user-scope manifest with a relative assembly path.
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitCortex</Name>
    <Assembly>RevitCortex\RevitCortex.Plugin.dll</Assembly>
    <AddInId>$AddInId</AddInId>
    <FullClassName>$FullClassName</FullClassName>
    <VendorId>GPA</VendorId>
    <VendorDescription>GPA - RevitCortex</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    Set-Content -Path (Join-Path $UserAddinsDir "RevitCortex.addin") -Value $manifest -Encoding UTF8

    $dllCount = (Get-ChildItem "$UserTargetDir\*.dll").Count
    Write-Host "  OK: $dllCount DLLs -> $UserTargetDir (Plugin.dll $((Get-Item (Join-Path $UserTargetDir 'RevitCortex.Plugin.dll')).LastWriteTime))" -ForegroundColor Green
}

Write-Host "`n=== Done. Restart Revit to load the updated plugin. ===" -ForegroundColor Green

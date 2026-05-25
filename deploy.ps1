param(
    [ValidateSet("2023","2024","2025","2026","2027")]
    [string]$RevitVersion = "2025",
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Configuration = "$Config R$($RevitVersion.Substring(2))"
$PublishDir = Join-Path $RepoRoot "publish\R$($RevitVersion.Substring(2))"
$AddInsDir = "C:\ProgramData\Autodesk\Revit\Addins\$RevitVersion"
$TargetDir = Join-Path $AddInsDir "RevitCortex"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$UserTargetDir = Join-Path $UserAddinsDir "RevitCortex"

Write-Host "=== RevitCortex Deploy ===" -ForegroundColor Cyan
Write-Host "Revit: $RevitVersion | Config: $Configuration"
Write-Host "Target: $TargetDir"

# --- Pre-flight: refuse to deploy while Revit is running (DLLs would be locked) ---
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host ""
    Write-Host "ERROR: Revit is currently running (PID $($revit.Id -join ', ')). Close Revit and re-run." -ForegroundColor Red
    exit 1
}

# Kill orphan RevitCortex.Server processes that may hold satellite assemblies in lock
$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) {
    Write-Host "Killing $($orphans.Count) orphan RevitCortex.Server process(es)..." -ForegroundColor Yellow
    $orphans | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Clean publish dir
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

# Build & publish Plugin
Write-Host "`nPublishing Plugin..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }

# Build & publish Tools (to same output)
Write-Host "Publishing Tools..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

# --- Remove competing user-scope install ---
# Revit scans both ProgramData (machine) and AppData\Roaming (user). If both exist,
# the user-scope copy can shadow this deploy and you'll silently run the wrong DLLs.
# Always wipe user-scope before writing to machine-scope (this script is dev-only).
if (Test-Path $UserTargetDir) {
    Write-Host "Removing competing user-scope install: $UserTargetDir" -ForegroundColor Yellow
    Remove-Item $UserTargetDir -Recurse -Force
}
$userAddinManifest = Join-Path $UserAddinsDir "RevitCortex.addin"
if (Test-Path $userAddinManifest) { Remove-Item $userAddinManifest -Force }

# Wipe + recreate machine-scope target so stale satellite assemblies don't survive
if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

# Copy DLLs
Write-Host "Copying files..." -ForegroundColor Yellow
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force

# Copy .addin manifest
$AddinSource = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin"
Copy-Item $AddinSource $AddInsDir -Force

$dllCount = (Get-ChildItem "$TargetDir\*.dll").Count

# --- AI Skill: keep dev-installed skill in sync with the repo ---
# Same guard as distribution/install.ps1: only install if client root exists.
$skillSrc = Join-Path $RepoRoot "ai-skills\revitcortex"
if (Test-Path $skillSrc) {
    $skillTargets = @(
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".claude");  Target = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex");  Name = "Claude Code" },
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".codex");   Target = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex");   Name = "Codex CLI" }
    )
    foreach ($entry in $skillTargets) {
        if (Test-Path $entry.ClientRoot) {
            if (-not (Test-Path $entry.Target)) { New-Item -ItemType Directory -Path $entry.Target -Force | Out-Null }
            Copy-Item "$skillSrc\*" $entry.Target -Recurse -Force
            Write-Host "Skill synced -> $($entry.Target)" -ForegroundColor Green
        }
    }
}

Write-Host "`n=== Deploy complete ===" -ForegroundColor Green
Write-Host "$dllCount DLLs deployed to $TargetDir"
Write-Host ".addin manifest copied to $AddInsDir\RevitCortex.addin"
Write-Host "`nRestart Revit $RevitVersion to load the plugin."

param(
    [ValidateSet("2023","2024","2025","2026")]
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

Write-Host "=== RevitCortex Deploy ===" -ForegroundColor Cyan
Write-Host "Revit: $RevitVersion | Config: $Configuration"
Write-Host "Target: $TargetDir"

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

# Create target directory
if (!(Test-Path $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null }

# Copy DLLs
Write-Host "Copying files..." -ForegroundColor Yellow
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force

# Copy .addin manifest
$AddinSource = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin"
Copy-Item $AddinSource $AddInsDir -Force

$dllCount = (Get-ChildItem "$TargetDir\*.dll").Count
Write-Host "`n=== Deploy complete ===" -ForegroundColor Green
Write-Host "$dllCount DLLs deployed to $TargetDir"
Write-Host ".addin manifest copied to $AddInsDir\RevitCortex.addin"
Write-Host "`nRestart Revit 2025 to load the plugin."

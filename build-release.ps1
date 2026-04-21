param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ReleaseDir = Join-Path $RepoRoot "release"
$ZipName = "RevitCortex-v$Version.zip"
$ZipPath = Join-Path $RepoRoot $ZipName

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Release Builder v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean release directory
if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

# --- Build C# for all Revit versions ---
Write-Host "[1/4] Building C# plugin..." -ForegroundColor Yellow

$pluginProject = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.Plugin.csproj"
$toolsProject = Join-Path $RepoRoot "src\RevitCortex.Tools\RevitCortex.Tools.csproj"

$builtVersions = @()

foreach ($rv in @("R23", "R24", "R25", "R26", "R27")) {
    $config = "Release $rv"
    $outDir = Join-Path $ReleaseDir "plugin\$rv"

    Write-Host "  Building $rv..." -ForegroundColor Gray
    dotnet publish -c "$config" $pluginProject -o $outDir --no-self-contained -v quiet 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    SKIPPED (build errors)" -ForegroundColor Yellow
        if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
        continue
    }

    dotnet publish -c "$config" $toolsProject -o $outDir --no-self-contained -v quiet 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    SKIPPED (build errors)" -ForegroundColor Yellow
        if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
        continue
    }

    $dllCount = (Get-ChildItem "$outDir\*.dll").Count
    Write-Host "    $dllCount DLLs" -ForegroundColor Gray
    $builtVersions += $rv
}

if ($builtVersions.Count -eq 0) { throw "No Revit versions built successfully" }
Write-Host "  Built: $($builtVersions -join ', ')" -ForegroundColor Green

# --- Build C# MCP server (self-contained, win-x64) ---
Write-Host ""
Write-Host "[2/4] Building C# MCP server..." -ForegroundColor Yellow

$serverProject = Join-Path $RepoRoot "src\RevitCortex.Server\RevitCortex.Server.csproj"
$serverTarget = Join-Path $ReleaseDir "server"

dotnet publish $serverProject -c Release -o $serverTarget --self-contained true -r win-x64 -v quiet 2>$null
if ($LASTEXITCODE -ne 0) { throw "MCP server build failed" }

$exePath = Join-Path $serverTarget "RevitCortex.Server.exe"
if (!(Test-Path $exePath)) { throw "Server executable not found after publish: $exePath" }

Write-Host "  Server built: $exePath" -ForegroundColor Green

# --- Copy support files ---
Write-Host ""
Write-Host "[3/4] Copying support files..." -ForegroundColor Yellow

# Installer scripts
Copy-Item (Join-Path $RepoRoot "distribution\install.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\install.bat") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\uninstall.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\README.txt") $ReleaseDir

# Shared PowerShell helpers (JSON-safe Claude config, Revit ACL fallback, Git install)
$libTarget = Join-Path $ReleaseDir "lib"
New-Item -ItemType Directory -Path $libTarget -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "distribution\lib\*") $libTarget -Recurse -Force

# .addin manifest
Copy-Item (Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin") $ReleaseDir

# Config templates
$templatesTarget = Join-Path $ReleaseDir "config-templates"
New-Item -ItemType Directory -Path $templatesTarget -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "distribution\config-templates\*") $templatesTarget

Write-Host "  Support files copied." -ForegroundColor Green

# --- Create ZIP ---
Write-Host ""
Write-Host "[4/4] Creating ZIP archive..." -ForegroundColor Yellow

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$ReleaseDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)

Write-Host "  Created: $ZipPath ($sizeMB MB)" -ForegroundColor Green

# --- Summary ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   Release package ready" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  File:    $ZipName" -ForegroundColor White
Write-Host "  Size:    $sizeMB MB" -ForegroundColor White
Write-Host "  Upload:  GitHub Releases" -ForegroundColor White
Write-Host ""

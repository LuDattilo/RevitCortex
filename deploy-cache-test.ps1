#requires -Version 5.1
<#
    Deploy script for the tool-result-cache live test.

    What it does:
      1. Refuses to run if Revit.exe is alive.
      2. Kills any orphan RevitCortex.Server.exe processes.
      3. Copies the freshly-built Plugin/Tools/Core DLLs (Debug R25) into:
         - %APPDATA%\Autodesk\Revit\Addins\2025\RevitCortex   (user scope)
         - %ProgramData%\Autodesk\Revit\Addins\2025\RevitCortex (machine scope, needs admin)
      4. Reports timestamps so you can confirm the new build landed.

    The machine-scope copy is what prevents the old DLLs from shadowing the
    user-scope deploy. If you skip the admin elevation, only user-scope is
    updated and the old machine-scope DLL may still load.
#>

[CmdletBinding()]
param(
    [string]$RevitVersion = '2025',
    [switch]$SkipMachineScope
)

$ErrorActionPreference = 'Stop'

$WorktreeRoot = Split-Path -Parent $PSCommandPath
$PluginBin    = Join-Path $WorktreeRoot "src\RevitCortex.Plugin\bin\Debug R$($RevitVersion.Substring(2))\net8.0-windows10.0.19041.0"
$ToolsBin     = Join-Path $WorktreeRoot "src\RevitCortex.Tools\bin\Debug R$($RevitVersion.Substring(2))\net8.0-windows10.0.19041.0"
$UserDst      = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\RevitCortex"
$MachineDst   = "C:\ProgramData\Autodesk\Revit\Addins\$RevitVersion\RevitCortex"

# Files we deploy. Everything else (third-party deps, satellites) stays as the
# previous install left it — this is a hot-patch, not a full re-install.
$FilesFromPlugin = @(
    'RevitCortex.Plugin.dll',
    'RevitCortex.Plugin.pdb',
    'RevitCortex.Core.dll',
    'RevitCortex.Core.pdb'
)
$FilesFromTools = @(
    'RevitCortex.Tools.dll',
    'RevitCortex.Tools.pdb'
)

Write-Host "=== RevitCortex tool-result-cache deploy ===" -ForegroundColor Cyan
Write-Host "Plugin source: $PluginBin"
Write-Host "Tools  source: $ToolsBin"
Write-Host ""

# --- Pre-flight: Revit must be closed ---
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close it and re-run." -ForegroundColor Red
    exit 1
}

# --- Verify build artifacts exist ---
foreach ($f in $FilesFromPlugin) {
    $p = Join-Path $PluginBin $f
    if (-not (Test-Path $p)) {
        Write-Host "ERROR: missing build artifact: $p" -ForegroundColor Red
        Write-Host "Run: dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c 'Debug R$($RevitVersion.Substring(2))'" -ForegroundColor Yellow
        exit 1
    }
}
foreach ($f in $FilesFromTools) {
    $p = Join-Path $ToolsBin $f
    if (-not (Test-Path $p)) {
        Write-Host "ERROR: missing build artifact: $p" -ForegroundColor Red
        Write-Host "Run: dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c 'Debug R$($RevitVersion.Substring(2))'" -ForegroundColor Yellow
        exit 1
    }
}

# --- Kill orphan Server.exe so DLLs aren't locked ---
$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) {
    Write-Host "Killing $($orphans.Count) orphan RevitCortex.Server process(es)..." -ForegroundColor Yellow
    $orphans | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

function Copy-DeploySet {
    param([string]$Destination)

    if (-not (Test-Path $Destination)) {
        Write-Host "WARN: destination does not exist, skipping: $Destination" -ForegroundColor Yellow
        return
    }

    foreach ($f in $FilesFromPlugin) {
        Copy-Item -Path (Join-Path $PluginBin $f) -Destination $Destination -Force
    }
    foreach ($f in $FilesFromTools) {
        Copy-Item -Path (Join-Path $ToolsBin $f) -Destination $Destination -Force
    }
    Write-Host "  Deployed to: $Destination" -ForegroundColor Green
    Get-Item (Join-Path $Destination 'RevitCortex.Plugin.dll') | Format-Table Name, LastWriteTime -AutoSize | Out-Host
}

# --- User scope (no UAC) ---
Write-Host "User-scope deploy..." -ForegroundColor Cyan
Copy-DeploySet -Destination $UserDst

# --- Machine scope (needs admin) ---
if ($SkipMachineScope) {
    Write-Host ""
    Write-Host "SKIPPED machine-scope copy ($MachineDst)." -ForegroundColor Yellow
    Write-Host "If Revit loads the old machine-scope DLLs you may not see the new tools." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Machine-scope deploy ($MachineDst)..." -ForegroundColor Cyan
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        Copy-DeploySet -Destination $MachineDst
    } else {
        # Self-elevate: re-launch this script as admin, machine-scope only, then return.
        Write-Host "Elevating for machine-scope copy..." -ForegroundColor Yellow
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName  = (Get-Process -Id $PID).Path
        $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -RevitVersion $RevitVersion -SkipMachineScope:`$false -OnlyMachineScope"
        $psi.Verb      = 'runas'
        try {
            $proc = [System.Diagnostics.Process]::Start($psi)
            $proc.WaitForExit()
            if ($proc.ExitCode -ne 0) {
                Write-Host "WARN: elevated machine-scope copy exited with code $($proc.ExitCode)" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "WARN: UAC denied. Machine-scope NOT updated: $_" -ForegroundColor Yellow
            Write-Host "Re-run from an elevated PowerShell to update $MachineDst manually." -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "Done. Restart Revit and Claude Code, then click 'Cortex Switch'." -ForegroundColor Green
Write-Host "After reconnect, get_cache_stats and clear_cache should appear in the MCP tool list." -ForegroundColor Green

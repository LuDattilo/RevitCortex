#requires -Version 5.1
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $ScriptDir 'lib\ClaudeConfig.ps1')
. (Join-Path $ScriptDir 'lib\RevitDeploy.ps1')
. (Join-Path $ScriptDir 'lib\GitInstall.ps1')

# --- Self-elevate (machine-scope Revit install path requires admin;
#     user-scope fallback does not, but we try machine first for parity with Inno installer) ---
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RevitCortex Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 0: Verify Revit is not running (lock DLLs if it is) ---
Write-Host "[0/5] Pre-flight checks..." -ForegroundColor Yellow
try {
    Assert-RevitClosed
    Write-Host "  Revit is not running" -ForegroundColor Gray
} catch {
    Write-Host "  $_" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# --- Step 1: Detect Revit versions ---
Write-Host ""
Write-Host "[1/5] Detecting Revit installations..." -ForegroundColor Yellow

$configMap = [ordered]@{ "2023" = "R23"; "2024" = "R24"; "2025" = "R25"; "2026" = "R26"; "2027" = "R27" }
$machineAddinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$foundVersions   = @()   # installed + plugin available in this ZIP
$skippedVersions = @()   # installed but plugin not in this ZIP

foreach ($ver in $configMap.Keys) {
    # 1. Detect whether this Revit version is actually installed on the machine.
    #    Check three signals in order: Addins folder, registry, Revit.exe.
    $machineVerDir = Join-Path $machineAddinsRoot $ver
    $revitInstalled = Test-Path $machineVerDir

    if (-not $revitInstalled) {
        foreach ($rp in @(
            "HKLM:\SOFTWARE\Autodesk\Revit\$ver",
            "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\$ver",
            "HKCU:\SOFTWARE\Autodesk\Revit\$ver"
        )) { if (Test-Path $rp) { $revitInstalled = $true; break } }
    }

    if (-not $revitInstalled) {
        $revitExe = "C:\Program Files\Autodesk\Revit $ver\Revit.exe"
        if (Test-Path $revitExe) { $revitInstalled = $true }
    }

    if (-not $revitInstalled) { continue }

    # 2. Check whether this ZIP contains the plugin build for this version.
    $pluginDir = Join-Path $ScriptDir "plugin\$($configMap[$ver])"
    if (-not (Test-Path $pluginDir)) {
        $skippedVersions += $ver   # Revit installed but not bundled in this package
        continue
    }

    $foundVersions += $ver
}

if ($foundVersions.Count -eq 0 -and $skippedVersions.Count -eq 0) {
    Write-Host "  ERROR: No supported Revit installation found (2023-2027)." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

if ($foundVersions.Count -eq 0) {
    Write-Host "  ERROR: Revit $($skippedVersions -join ', ') detected but this package does not include a plugin build for those versions." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "  Found Revit: $($foundVersions -join ', ')" -ForegroundColor Green
if ($skippedVersions.Count -gt 0) {
    Write-Host "  NOTE: Revit $($skippedVersions -join ', ') detected but not bundled in this package — skipped." -ForegroundColor Yellow
}

# Port 8080 warning (non-fatal)
try {
    if (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue) {
        Write-Host "  WARNING: Port 8080 already in use. Change in %USERPROFILE%\.revitcortex\settings.json if needed." -ForegroundColor Yellow
    }
} catch {}

# --- Step 2: Install Plugin (machine → user scope fallback on ACL errors) ---
Write-Host ""
Write-Host "[2/5] Installing Revit plugin..." -ForegroundColor Yellow

$addinTemplate = Join-Path $ScriptDir "RevitCortex.addin"
if (-not (Test-Path $addinTemplate)) {
    Write-Host "  ERROR: RevitCortex.addin not found at $addinTemplate" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$deployFailures = @()
foreach ($ver in $foundVersions) {
    $suffix = $configMap[$ver]
    $sourceDir = Join-Path $ScriptDir "plugin\$suffix"
    $r = Copy-RevitAddin -Version $ver -PluginSource $sourceDir -AddinManifest $addinTemplate
    if ($r.Ok) {
        $tag = if ($r.Scope -eq 'user') { '(user scope)' } else { '' }
        Write-Host ("  Revit {0} : {1} {2}" -f $ver, $r.TargetDir, $tag) -ForegroundColor Gray
    } else {
        Write-Host ("  Revit {0} : FAILED — {1}" -f $ver, $r.Error) -ForegroundColor Red
        $deployFailures += $ver
    }
}

if ($deployFailures.Count -gt 0) {
    Write-Host "  Plugin install failed for: $($deployFailures -join ', ')" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "  Plugin installed for $($foundVersions.Count) Revit version(s)." -ForegroundColor Green

# --- Step 3: Install MCP Server (C# self-contained) ---
Write-Host ""
Write-Host "[3/5] Installing MCP server..." -ForegroundColor Yellow

$serverSource = Join-Path $ScriptDir "server"
$serverTarget = Join-Path $env:USERPROFILE ".revitcortex\server"

if (-not (Test-Path $serverSource)) {
    Write-Host "  ERROR: Server files not found at $serverSource" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

if (Test-Path $serverTarget) { Remove-Item $serverTarget -Recurse -Force }
New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
Copy-Item "$serverSource\*" $serverTarget -Recurse -Force

$serverExe = Join-Path $serverTarget "RevitCortex.Server.exe"
if (-not (Test-Path $serverExe)) {
    Write-Host "  ERROR: Expected RevitCortex.Server.exe in server/ — check release package" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Unblock Zone.Identifier so Defender/SmartScreen don't block on first run
Get-ChildItem $serverTarget -Recurse -File | ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }

# Defender exclusion (opt-in, idempotent) — remains from old installer
if (Get-Command Add-MpPreference -ErrorAction SilentlyContinue) {
    $existing = @()
    try { $mp = Get-MpPreference -ErrorAction SilentlyContinue; if ($mp -and $mp.ExclusionPath) { $existing = @($mp.ExclusionPath) } } catch {}
    if ($existing -notcontains $serverTarget) {
        $a = Read-Host "  Add Windows Defender exclusion for '$serverTarget'? (y/N)"
        if ($a -eq 'y' -or $a -eq 'Y') {
            Add-MpPreference -ExclusionPath $serverTarget -ErrorAction SilentlyContinue
            Write-Host "  Defender exclusion added" -ForegroundColor Gray
        }
    }
}

Write-Host "  Server installed: $serverExe" -ForegroundColor Green

# AI Skill — install RevitCortex skill to user-level paths
# Guard on client root (.claude / .codex) existing: if the user doesn't have
# the client at all we skip (no profile pollution). If the client root exists
# but skills/ doesn't yet, we create it — first-time skill install must work.
$skillSrc = Join-Path $PSScriptRoot "ai-skills\revitcortex"
if (Test-Path $skillSrc) {
    $skillTargets = @(
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".claude");  Target = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex");  Name = "Claude Code" },
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".codex");   Target = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex");   Name = "Codex CLI" }
    )
    foreach ($entry in $skillTargets) {
        if (Test-Path $entry.ClientRoot) {
            if (-not (Test-Path $entry.Target)) { New-Item -ItemType Directory -Path $entry.Target -Force | Out-Null }
            Copy-Item "$skillSrc\*" $entry.Target -Recurse -Force
            Write-Host "  Installed skill -> $($entry.Target)"
        } else {
            Write-Host "  Skipped skill install ($($entry.Name) not detected at $($entry.ClientRoot))"
        }
    }
} else {
    Write-Host "  ai-skills not bundled — skipping skill install"
}

# --- Step 4: Ensure Git (needed by Claude Code for many workflows) ---
Write-Host ""
Write-Host "[4/5] Checking Git..." -ForegroundColor Yellow
$gitOk = Ensure-Git

# --- Step 5: Configure Claude clients ---
Write-Host ""
Write-Host "[5/5] Configure Claude client" -ForegroundColor Yellow
Write-Host ""
Write-Host "  How will you use RevitCortex?"
Write-Host "  [1] Claude Desktop"
Write-Host "  [2] Claude Code (CLI)"
Write-Host "  [3] Both"
Write-Host "  [4] Skip (configure later)"
Write-Host ""
$choice = Read-Host "  Enter choice (1-4)"

$claudeDesktopConfigured = $false
$claudeCodeConfigured    = $false

if ($choice -eq "1" -or $choice -eq "3") {
    $configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
    try {
        $result = Merge-ClaudeMcpServer -ConfigPath $configPath -ServerName 'revitcortex' -Command $serverExe -Arguments @()
        if ($result.BackupPath) {
            Write-Host ("  Claude Desktop config backed up: {0}" -f $result.BackupPath) -ForegroundColor Gray
        }
        Write-Host ("  Claude Desktop: revitcortex {0}." -f $result.Action) -ForegroundColor Green
        $claudeDesktopConfigured = $true
    } catch {
        Write-Host "  Claude Desktop config update FAILED: $_" -ForegroundColor Red
        Write-Host "  Your existing config has been preserved. Fix the JSON and re-run this installer." -ForegroundColor Yellow
    }
}

if ($choice -eq "2" -or $choice -eq "3") {
    $claudeCli = Get-Command claude -ErrorAction SilentlyContinue
    if ($claudeCli) {
        try {
            & claude mcp add revitcortex $serverExe 2>$null | Out-Null
            Write-Host "  Claude Code: revitcortex configured." -ForegroundColor Green
            $claudeCodeConfigured = $true
        } catch {
            Write-Host "  Claude Code CLI returned: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Claude Code CLI not found. Add manually:" -ForegroundColor Yellow
        Write-Host "    claude mcp add revitcortex `"$serverExe`"" -ForegroundColor Gray
    }
}

if ($choice -eq "4") {
    Write-Host "  Skipped. Configure later:" -ForegroundColor Yellow
    Write-Host "    Claude Desktop: add to %APPDATA%\Claude\claude_desktop_config.json" -ForegroundColor Gray
    Write-Host "    Claude Code:    claude mcp add revitcortex `"$serverExe`"" -ForegroundColor Gray
}

# --- Summary ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   RevitCortex installed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host ("  Plugin:  Revit {0}" -f ($foundVersions -join ', ')) -ForegroundColor White
Write-Host ("  Server:  {0}" -f $serverExe) -ForegroundColor White
Write-Host ("  Git:     {0}" -f $(if ($gitOk) { 'OK' } else { 'NOT installed (install manually)' })) -ForegroundColor White
if ($claudeDesktopConfigured) { Write-Host "  Client:  Claude Desktop" -ForegroundColor White }
if ($claudeCodeConfigured)    { Write-Host "  Client:  Claude Code" -ForegroundColor White }
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Revit" -ForegroundColor White
Write-Host "  2. Restart Claude Desktop / Claude Code" -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to close"

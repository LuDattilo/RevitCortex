#requires -Version 5.1
<#
.SYNOPSIS
    Regression tests for distribution\lib\ClaudeConfig.ps1.

.DESCRIPTION
    Self-contained (no Pester dependency — PS 5.1 ships Pester 3.x which lacks Should -BeOfType).
    Run under BOTH engines, because the Inno installer invokes powershell.exe (5.1):

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File distribution\tests\ClaudeConfig.Tests.ps1
        pwsh -NoProfile -File distribution\tests\ClaudeConfig.Tests.ps1

    Exit code 0 = all pass, 1 = at least one failure.

    Covers the 2026-06-10 incident: Merge-ClaudeMcpServer round-trips the whole config
    through ConvertTo-HashtableDeep, and PowerShell's output enumeration mangled arrays —
    [] became {} / null and single-element arrays collapsed to scalars. Claude Desktop's
    schema validation then rejected the file and RESET it, wiping every MCP server.
#>

$ErrorActionPreference = 'Stop'
$libPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\ClaudeConfig.ps1'
. $libPath

$script:Failures = 0
$script:Passes = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if ($Condition) {
        $script:Passes++
        Write-Host "  PASS  $Message" -ForegroundColor Green
    } else {
        $script:Failures++
        Write-Host "  FAIL  $Message" -ForegroundColor Red
    }
}

function New-TestDir {
    $dir = Join-Path $env:TEMP ("ClaudeConfigTests_" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

# JSON value classifier on the RAW parsed output (PSCustomObject world, no helpers
# from the lib under test — an independent oracle).
function Get-JsonKind {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return 'string' }
    if ($Value -is [System.Array]) { return 'array' }
    if ($Value -is [System.Management.Automation.PSCustomObject]) { return 'object' }
    return $Value.GetType().Name
}

Write-Host ""
Write-Host "ClaudeConfig.ps1 tests ($($PSVersionTable.PSVersion), $libPath)" -ForegroundColor Cyan
Write-Host ""

# --- Scenario 1: Merge must not corrupt OTHER servers' arrays (the 2026-06-10 incident) ---
Write-Host "Scenario 1: Merge-ClaudeMcpServer preserves foreign entries byte-exact in shape" -ForegroundColor Yellow
$dir = New-TestDir
$cfgPath = Join-Path $dir 'claude_desktop_config.json'
@'
{
  "mcpServers": {
    "naviscortex": {
      "command": "C:\\navis\\NavisCortex.Server.exe",
      "args": []
    },
    "formacortex": {
      "command": "C:\\nodejs\\node.exe",
      "args": ["C:\\forma\\dist\\server.js"],
      "env": { "APS_CLIENT_ID": "abc", "FORMACORTEX_MOCK": "0" }
    },
    "multi": {
      "command": "C:\\x.exe",
      "args": ["-a", "-b", "-c"]
    }
  },
  "preferences": {
    "chromeExtension": {
      "pairedFromDeviceIds": ["45ae689d-d000-4be4-a210-0b22917358b1"]
    },
    "starredSpaces": [],
    "nested": { "deep": [[1, 2], []] }
  }
}
'@ | Set-Content -Path $cfgPath -Encoding UTF8

$null = Merge-ClaudeMcpServer -ConfigPath $cfgPath -ServerName 'revitcortex' -Command 'C:\rc\RevitCortex.Server.exe'
$out = [System.IO.File]::ReadAllText($cfgPath) | ConvertFrom-Json

Assert-True ((Get-JsonKind $out.mcpServers.naviscortex.args) -eq 'array') "naviscortex.args [] stays an array (got: $(Get-JsonKind $out.mcpServers.naviscortex.args))"
Assert-True (@($out.mcpServers.naviscortex.args).Count -eq 0) "naviscortex.args stays empty"
Assert-True ((Get-JsonKind $out.mcpServers.formacortex.args) -eq 'array') "formacortex.args single-element stays an array (got: $(Get-JsonKind $out.mcpServers.formacortex.args))"
Assert-True (@($out.mcpServers.formacortex.args).Count -eq 1 -and @($out.mcpServers.formacortex.args)[0] -eq 'C:\forma\dist\server.js') "formacortex.args content preserved"
Assert-True ((Get-JsonKind $out.mcpServers.formacortex.env) -eq 'object') "formacortex.env stays an object"
Assert-True ($out.mcpServers.formacortex.env.APS_CLIENT_ID -eq 'abc') "formacortex.env values preserved"
Assert-True (@($out.mcpServers.multi.args).Count -eq 3) "multi-element args length preserved"
Assert-True ((Get-JsonKind $out.preferences.chromeExtension.pairedFromDeviceIds) -eq 'array') "deep single-element array stays an array (got: $(Get-JsonKind $out.preferences.chromeExtension.pairedFromDeviceIds))"
Assert-True ((Get-JsonKind $out.preferences.starredSpaces) -eq 'array') "deep empty array stays an array (got: $(Get-JsonKind $out.preferences.starredSpaces))"
Assert-True ((Get-JsonKind $out.preferences.nested.deep) -eq 'array' -and @($out.preferences.nested.deep).Count -eq 2) "array-of-arrays keeps outer length 2"
Assert-True ((Get-JsonKind $out.preferences.nested.deep[0]) -eq 'array' -and @($out.preferences.nested.deep[0]).Count -eq 2) "inner array [1,2] preserved"
Assert-True ((Get-JsonKind $out.mcpServers.revitcortex.args) -eq 'array' -and @($out.mcpServers.revitcortex.args).Count -eq 0) "merged revitcortex entry gets args []"
Assert-True ($out.mcpServers.revitcortex.command -eq 'C:\rc\RevitCortex.Server.exe') "merged revitcortex command set"
Remove-Item $dir -Recurse -Force

# --- Scenario 2: Remove must round-trip arrays unharmed too (uninstall path) ---
Write-Host "Scenario 2: Remove-ClaudeMcpServer preserves remaining entries" -ForegroundColor Yellow
$dir = New-TestDir
$cfgPath = Join-Path $dir 'claude_desktop_config.json'
@'
{
  "mcpServers": {
    "revitcortex": { "command": "C:\\rc\\RevitCortex.Server.exe", "args": [] },
    "formacortex": { "command": "C:\\nodejs\\node.exe", "args": ["C:\\forma\\dist\\server.js"] }
  }
}
'@ | Set-Content -Path $cfgPath -Encoding UTF8

$r = Remove-ClaudeMcpServer -ConfigPath $cfgPath -ServerName 'revitcortex'
$out = [System.IO.File]::ReadAllText($cfgPath) | ConvertFrom-Json
Assert-True ($r.Action -eq 'removed') "revitcortex entry removed"
Assert-True ($null -eq $out.mcpServers.revitcortex) "revitcortex gone from file"
Assert-True ((Get-JsonKind $out.mcpServers.formacortex.args) -eq 'array' -and @($out.mcpServers.formacortex.args).Count -eq 1) "surviving formacortex.args stays a 1-element array (got: $(Get-JsonKind $out.mcpServers.formacortex.args))"
Remove-Item $dir -Recurse -Force

# --- Scenario 3: Fresh install (no config file) ---
Write-Host "Scenario 3: Merge into missing config creates a valid file" -ForegroundColor Yellow
$dir = New-TestDir
$cfgPath = Join-Path $dir 'claude_desktop_config.json'
$r = Merge-ClaudeMcpServer -ConfigPath $cfgPath -ServerName 'revitcortex' -Command 'C:\rc\RevitCortex.Server.exe' -Arguments @('--port', '8080')
$out = [System.IO.File]::ReadAllText($cfgPath) | ConvertFrom-Json
Assert-True ($r.Action -eq 'added') "entry reported as added"
Assert-True ((Get-JsonKind $out.mcpServers.revitcortex.args) -eq 'array' -and @($out.mcpServers.revitcortex.args).Count -eq 2) "explicit 2-element Arguments preserved"
Remove-Item $dir -Recurse -Force

# --- Scenario 4: Malformed JSON must throw, not nuke the file ---
Write-Host "Scenario 4: Malformed config throws and is left untouched" -ForegroundColor Yellow
$dir = New-TestDir
$cfgPath = Join-Path $dir 'claude_desktop_config.json'
'{ not json' | Set-Content -Path $cfgPath -Encoding UTF8
$before = [System.IO.File]::ReadAllText($cfgPath)
$threw = $false
try { $null = Merge-ClaudeMcpServer -ConfigPath $cfgPath -ServerName 'revitcortex' -Command 'C:\rc.exe' } catch { $threw = $true }
$after = [System.IO.File]::ReadAllText($cfgPath)
Assert-True $threw "merge throws on malformed JSON"
Assert-True ($before -eq $after) "malformed file left untouched"
Remove-Item $dir -Recurse -Force

# --- Summary ---
Write-Host ""
if ($script:Failures -gt 0) {
    Write-Host "RESULT: $script:Failures FAILED, $script:Passes passed" -ForegroundColor Red
    exit 1
} else {
    Write-Host "RESULT: all $script:Passes passed" -ForegroundColor Green
    exit 0
}

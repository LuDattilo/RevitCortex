#requires -Version 5.1
<#
.SYNOPSIS
    JSON-safe merge helper for claude_desktop_config.json.

.DESCRIPTION
    Fixes the "Claude Desktop stops seeing MCP servers after install" class of bugs
    by avoiding the two footguns of Set-Content -Encoding UTF8:
      1. PowerShell 5.1 writes UTF-8 WITH BOM by default when using Set-Content -Encoding UTF8.
         Electron (Claude Desktop) sometimes chokes on the BOM.
      2. ConvertFrom-Json returns PSCustomObject; merging with Add-Member on a hashtable
         payload produces inconsistent results between PS 5.1 and PS 7+.

    This module normalizes every object through hashtables and writes raw UTF-8 without BOM
    via [System.IO.File]::WriteAllText with a UTF8Encoding($false).

    Exposes:
      - Merge-ClaudeMcpServer  (add or update one MCP server entry, preserving the rest)
      - Remove-ClaudeMcpServer (remove one MCP server entry by name)
#>

function ConvertTo-HashtableDeep {
    param([Parameter(ValueFromPipeline = $true)] $obj)
    process {
        if ($null -eq $obj) { return $null }
        if ($obj -is [hashtable] -or $obj -is [System.Collections.Specialized.OrderedDictionary]) {
            $result = [ordered]@{}
            foreach ($key in $obj.Keys) { $result[$key] = ConvertTo-HashtableDeep $obj[$key] }
            return $result
        }
        if ($obj -is [System.Management.Automation.PSCustomObject]) {
            $result = [ordered]@{}
            foreach ($p in $obj.PSObject.Properties) { $result[$p.Name] = ConvertTo-HashtableDeep $p.Value }
            return $result
        }
        if ($obj -is [System.Collections.IList] -and $obj -isnot [string]) {
            # PowerShell enumerates collections written to the output stream:
            # a plain `return @(...)` collapses 0 elements to nothing (ConvertTo-Json
            # then emits {}) and 1 element to a bare scalar — Claude Desktop rejects
            # the resulting config and resets it. Build the array in expression
            # context and prepend the unary comma so it leaves the function as ONE object.
            $items = New-Object System.Collections.ArrayList
            foreach ($element in $obj) {
                [void]$items.Add((ConvertTo-HashtableDeep $element))
            }
            return , $items.ToArray()
        }
        return $obj
    }
}

function Read-JsonFileAsHashtable {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path $Path)) { return [ordered]@{} }

    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { return [ordered]@{} }

    # Strip BOM if present (PS 5.1 Get-Content returns it, [IO.File]::ReadAllText does not — belt and braces)
    if ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) { $raw = $raw.Substring(1) }

    try {
        $obj = $raw | ConvertFrom-Json -ErrorAction Stop
        return (ConvertTo-HashtableDeep $obj)
    } catch {
        throw "Claude config at '$Path' is not valid JSON: $_"
    }
}

function Write-JsonFileUtf8NoBom {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Data
    )

    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # ConvertTo-Json on an ordered hashtable preserves key order and emits standard JSON
    $json = $Data | ConvertTo-Json -Depth 20

    # UTF-8 without BOM — what Electron apps expect
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function New-TimestampedBackup {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path $Path)) { return $null }
    $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
    $bak = "$Path.$ts.bak"
    Copy-Item $Path $bak -Force
    return $bak
}

function Merge-ClaudeMcpServer {
    <#
    .SYNOPSIS
        Merge (or update) one MCP server entry into claude_desktop_config.json without
        overwriting existing servers or corrupting the file.

    .PARAMETER ConfigPath
        Full path to claude_desktop_config.json. Created if missing.

    .PARAMETER ServerName
        Name of the MCP server (the key under mcpServers). Typically "revitcortex".

    .PARAMETER Command
        Executable path that launches the server.

    .PARAMETER Arguments
        Optional array of CLI arguments. Defaults to empty.

    .OUTPUTS
        Hashtable with { Path, BackupPath, Action = 'added'|'updated', Ok }.
    #>
    param(
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [string] $ServerName,
        [Parameter(Mandatory)] [string] $Command,
        [string[]] $Arguments = @()
    )

    # Read and parse — throws if the JSON is malformed rather than silently nuking it
    $config = Read-JsonFileAsHashtable -Path $ConfigPath

    # Backup before we touch anything (only if file exists)
    $backup = $null
    if (Test-Path $ConfigPath) { $backup = New-TimestampedBackup -Path $ConfigPath }

    if (-not $config.Contains('mcpServers')) {
        $config['mcpServers'] = [ordered]@{}
    } elseif ($config['mcpServers'] -isnot [System.Collections.IDictionary]) {
        # Malformed existing mcpServers node — back up and start fresh for this key
        $config['mcpServers'] = [ordered]@{}
    }

    $action = if ($config['mcpServers'].Contains($ServerName)) { 'updated' } else { 'added' }

    $entry = [ordered]@{
        command = $Command
        args    = @($Arguments)
    }
    $config['mcpServers'][$ServerName] = $entry

    Write-JsonFileUtf8NoBom -Path $ConfigPath -Data $config

    return @{ Path = $ConfigPath; BackupPath = $backup; Action = $action; Ok = $true }
}

function Remove-ClaudeMcpServer {
    <#
    .SYNOPSIS
        Remove one MCP server entry from claude_desktop_config.json, leaving the rest intact.

    .OUTPUTS
        Hashtable with { Path, BackupPath, Action = 'removed'|'not-found', Ok }.
    #>
    param(
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [string] $ServerName
    )

    if (-not (Test-Path $ConfigPath)) {
        return @{ Path = $ConfigPath; BackupPath = $null; Action = 'not-found'; Ok = $true }
    }

    $config = Read-JsonFileAsHashtable -Path $ConfigPath
    $backup = New-TimestampedBackup -Path $ConfigPath

    $action = 'not-found'
    if ($config.Contains('mcpServers') -and $config['mcpServers'] -is [System.Collections.IDictionary] `
        -and $config['mcpServers'].Contains($ServerName)) {
        $config['mcpServers'].Remove($ServerName)
        $action = 'removed'
    }

    Write-JsonFileUtf8NoBom -Path $ConfigPath -Data $config
    return @{ Path = $ConfigPath; BackupPath = $backup; Action = $action; Ok = $true }
}

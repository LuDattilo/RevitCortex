#requires -Version 5.1
<#
.SYNOPSIS
    TOML-safe merge helper for ~/.codex/config.toml.

.DESCRIPTION
    Adds or updates the [mcp_servers.<name>] section in Codex CLI's config file
    without overwriting existing sections or corrupting the file.

    PowerShell has no built-in TOML parser, so this module uses regex-based
    text manipulation scoped strictly to the target section.

    Exposes:
      - Merge-CodexMcpServer  (add or update one MCP server entry, preserving the rest)
      - Remove-CodexMcpServer (remove one MCP server entry by name)
#>

function Merge-CodexMcpServer {
    <#
    .SYNOPSIS
        Merge (or update) one MCP server entry into ~/.codex/config.toml without
        overwriting existing servers or corrupting the file.

    .PARAMETER ConfigPath
        Full path to config.toml. Created if missing.

    .PARAMETER ServerName
        Name of the MCP server (key under mcp_servers). Typically "revitcortex".

    .PARAMETER Command
        Executable path that launches the server.

    .OUTPUTS
        Hashtable with { Path, Action = 'created'|'added'|'updated', Ok }.
    #>
    param(
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [string] $ServerName,
        [Parameter(Mandatory)] [string] $Command
    )

    # Backslashes must be doubled in TOML strings
    $escapedCmd = $Command.Replace('\', '\\')
    $sectionHeader = "[mcp_servers.$ServerName]"
    $newSection = "$sectionHeader`ncommand = `"$escapedCmd`"`nargs = []"

    if (-not (Test-Path $ConfigPath)) {
        $dir = Split-Path $ConfigPath -Parent
        if ($dir -and -not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($ConfigPath, $newSection + "`n", [System.Text.UTF8Encoding]::new($false))
        return @{ Path = $ConfigPath; Action = 'created'; Ok = $true }
    }

    $content = [System.IO.File]::ReadAllText($ConfigPath)

    if ($content -match [regex]::Escape($sectionHeader)) {
        # Section already exists — update the command line in place
        $pattern = "(?m)(\[mcp_servers\.$([regex]::Escape($ServerName))\]\s*\r?\n(?:.*\r?\n)*?)command\s*=\s*`"[^`"]*`""
        $replacement = "`${1}command = `"$escapedCmd`""
        $updated = [regex]::Replace($content, $pattern, $replacement)
        [System.IO.File]::WriteAllText($ConfigPath, $updated, [System.Text.UTF8Encoding]::new($false))
        return @{ Path = $ConfigPath; Action = 'updated'; Ok = $true }
    }

    # Section not present — append at end of file
    $content = $content.TrimEnd() + "`n`n" + $newSection + "`n"
    [System.IO.File]::WriteAllText($ConfigPath, $content, [System.Text.UTF8Encoding]::new($false))
    return @{ Path = $ConfigPath; Action = 'added'; Ok = $true }
}

function Remove-CodexMcpServer {
    <#
    .SYNOPSIS
        Remove one MCP server section from config.toml, leaving the rest intact.

    .OUTPUTS
        Hashtable with { Path, Action = 'removed'|'not-found', Ok }.
    #>
    param(
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [string] $ServerName
    )

    if (-not (Test-Path $ConfigPath)) {
        return @{ Path = $ConfigPath; Action = 'not-found'; Ok = $true }
    }

    $content = [System.IO.File]::ReadAllText($ConfigPath)
    $sectionHeader = [regex]::Escape("[mcp_servers.$ServerName]")

    # Match from section header up to (but not including) the next [section] or end of file
    $pattern = "(?m)\[mcp_servers\.$([regex]::Escape($ServerName))\][^\[]*"
    if ($content -notmatch $pattern) {
        return @{ Path = $ConfigPath; Action = 'not-found'; Ok = $true }
    }

    $updated = [regex]::Replace($content, $pattern, '').TrimEnd() + "`n"
    [System.IO.File]::WriteAllText($ConfigPath, $updated, [System.Text.UTF8Encoding]::new($false))
    return @{ Path = $ConfigPath; Action = 'removed'; Ok = $true }
}

#requires -Version 5.1
<#
.SYNOPSIS
    Revit deploy helpers: detect running instances, copy addin DLLs with ACL-aware
    fallback from machine scope (ProgramData) to user scope (AppData).
#>

function Test-RevitRunning {
    [OutputType([bool])]
    param()
    return [bool](Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
}

function Assert-RevitClosed {
    <#
    .SYNOPSIS
        Verify Revit is not running. Prompt user to close if it is, loop until closed or aborted.
    #>
    param([switch] $NonInteractive)

    while (Test-RevitRunning) {
        if ($NonInteractive) {
            throw "Revit is running. Close Revit and re-run the installer."
        }
        Write-Host ""
        Write-Host "  Revit is currently running. The installer cannot write plugin DLLs while Revit has them locked." -ForegroundColor Yellow
        $choice = Read-Host "  Close Revit manually, then press ENTER to continue (or type 'q' to abort)"
        if ($choice -eq 'q' -or $choice -eq 'Q') {
            throw "Installation aborted by user (Revit was running)."
        }
    }
}

function Copy-RevitAddin {
    <#
    .SYNOPSIS
        Copy plugin DLLs + .addin manifest into the correct Revit addin folder for
        a specific Revit version, with ACL-aware machine→user fallback.

    .DESCRIPTION
        Primary destination: C:\ProgramData\Autodesk\Revit\Addins\<version>\
        Fallback (when write is denied by ACLs or Copy-Item raises UnauthorizedAccess):
                  %AppData%\Autodesk\Revit\Addins\<version>\

        Revit scans both locations on startup, so user-scope is a fully functional
        fallback that works without elevation and is isolated per user.

    .PARAMETER Version
        Revit major version as a string, e.g. "2025".

    .PARAMETER PluginSource
        Folder containing the built plugin DLLs for this version.

    .PARAMETER AddinManifest
        Full path to the RevitCortex.addin XML manifest file.

    .OUTPUTS
        Hashtable { Version, Scope = 'machine'|'user', TargetDir, Ok, Error }.
    #>
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $PluginSource,
        [Parameter(Mandatory)] [string] $AddinManifest
    )

    $machineRoot = "C:\ProgramData\Autodesk\Revit\Addins"
    $userRoot    = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
    $scopes = @(
        @{ Name = 'machine'; Root = $machineRoot; Other = $userRoot },
        @{ Name = 'user';    Root = $userRoot;    Other = $machineRoot }
    )

    $lastError = $null
    foreach ($scope in $scopes) {
        $verDir     = Join-Path $scope.Root $Version
        $pluginDir  = Join-Path $verDir 'RevitCortex'
        $addinFile  = Join-Path $verDir 'RevitCortex.addin'

        try {
            if (-not (Test-Path $verDir)) { New-Item -ItemType Directory -Path $verDir -Force | Out-Null }

            # Clean target if present; tolerate partial previous installs
            if (Test-Path $pluginDir) { Remove-Item $pluginDir -Recurse -Force -ErrorAction Stop }

            Copy-Item $PluginSource $pluginDir -Recurse -Force -ErrorAction Stop
            Copy-Item $AddinManifest $addinFile -Force -ErrorAction Stop

            # Remove Zone.Identifier ADS so .NET can load the DLLs (HRESULT 0x80131515)
            Get-ChildItem $pluginDir -Recurse -File | ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }
            Unblock-File -Path $addinFile -ErrorAction SilentlyContinue

            # Wipe the OTHER scope so Revit doesn't load a stale shadow copy.
            # Revit scans both ProgramData and AppData\Roaming on startup; leaving
            # an old copy in the opposite location silently shadows this install.
            $otherVerDir    = Join-Path $scope.Other $Version
            $otherPluginDir = Join-Path $otherVerDir 'RevitCortex'
            $otherAddinFile = Join-Path $otherVerDir 'RevitCortex.addin'
            if (Test-Path $otherPluginDir) {
                try { Remove-Item $otherPluginDir -Recurse -Force -ErrorAction Stop } catch {}
            }
            if (Test-Path $otherAddinFile) {
                try { Remove-Item $otherAddinFile -Force -ErrorAction Stop } catch {}
            }

            return @{ Version = $Version; Scope = $scope.Name; TargetDir = $pluginDir; Ok = $true; Error = $null }
        } catch [System.UnauthorizedAccessException] {
            $lastError = $_
            continue  # try the next scope
        } catch {
            # Any other I/O failure (file locked, path too long, disk full...) - surface it
            $lastError = $_
            continue
        }
    }

    return @{ Version = $Version; Scope = $null; TargetDir = $null; Ok = $false; Error = "$lastError" }
}

function Remove-RevitAddin {
    <#
    .SYNOPSIS
        Remove RevitCortex from BOTH machine and user scope for the given version.
    #>
    param([Parameter(Mandatory)] [string] $Version)

    $removed = @()
    foreach ($root in @("C:\ProgramData\Autodesk\Revit\Addins", (Join-Path $env:APPDATA 'Autodesk\Revit\Addins'))) {
        $pluginDir = Join-Path $root "$Version\RevitCortex"
        $addinFile = Join-Path $root "$Version\RevitCortex.addin"
        if (Test-Path $pluginDir) {
            try { Remove-Item $pluginDir -Recurse -Force -ErrorAction Stop; $removed += $pluginDir } catch {}
        }
        if (Test-Path $addinFile) {
            try { Remove-Item $addinFile -Force -ErrorAction Stop } catch {}
        }
    }
    return $removed
}

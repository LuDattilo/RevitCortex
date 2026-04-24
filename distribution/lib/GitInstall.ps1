#requires -Version 5.1
<#
.SYNOPSIS
    Git auto-install: winget first, direct-download fallback with architecture detection.

.DESCRIPTION
    Claude Code users need Git in PATH. Many deploy PCs don't have it.
    Strategy, in order:
      1. If git.exe is already on PATH → skip (idempotent).
      2. If winget is available (Windows 10 22H2+ and Windows 11) → `winget install Git.Git`.
      3. Otherwise → download the matching Git-for-Windows release and run it silently.

    Architecture detection picks the correct x64 / arm64 / 32-bit installer at runtime
    so the same installer works on every machine.
#>

$GitForWindowsVersion = 'v2.53.0.windows.3'
$GitForWindowsBuild   = '2.53.0.3'

function Test-GitInstalled {
    [OutputType([bool])]
    param()
    return [bool](Get-Command git -ErrorAction SilentlyContinue)
}

function Get-GitInstallerUrl {
    [OutputType([string])]
    param()

    $arch = 'x64'
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') {
        $arch = 'arm64'
    } elseif (-not [Environment]::Is64BitOperatingSystem) {
        $arch = 'x86'
    }

    $file = switch ($arch) {
        'arm64' { "Git-$GitForWindowsBuild-arm64.exe" }
        'x64'   { "Git-$GitForWindowsBuild-64-bit.exe" }
        'x86'   { "Git-$GitForWindowsBuild-32-bit.exe" }
    }
    return "https://github.com/git-for-windows/git/releases/download/$GitForWindowsVersion/$file"
}

function Install-GitViaWinget {
    [OutputType([bool])]
    param()
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { return $false }
    try {
        $args = @('install','--id','Git.Git','--source','winget',
                  '--silent','--accept-source-agreements','--accept-package-agreements',
                  '--disable-interactivity')
        $p = Start-Process winget -ArgumentList $args -Wait -PassThru -NoNewWindow
        # winget exit code 0 = success; -1978335189 = already installed; others = fail
        return ($p.ExitCode -eq 0 -or $p.ExitCode -eq -1978335189)
    } catch {
        return $false
    }
}

function Install-GitViaDownload {
    [OutputType([bool])]
    param()

    $url = Get-GitInstallerUrl
    $exe = Join-Path $env:TEMP ("RevitCortex_Git_" + [guid]::NewGuid().ToString('N') + ".exe")

    try {
        Write-Host "    Downloading Git installer from $url" -ForegroundColor Gray
        $old = [Net.ServicePointManager]::SecurityProtocol
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
        try {
            Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing -ErrorAction Stop
        } finally {
            [Net.ServicePointManager]::SecurityProtocol = $old
        }

        # Git-for-Windows installer silent args (see https://gitforwindows.org/requirements.html)
        $silentArgs = '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /NOCANCEL /SP- /CLOSEAPPLICATIONS'
        $p = Start-Process $exe -ArgumentList $silentArgs -Wait -PassThru
        return ($p.ExitCode -eq 0)
    } catch {
        Write-Host "    Git download/install failed: $_" -ForegroundColor Yellow
        return $false
    } finally {
        if (Test-Path $exe) { Remove-Item $exe -Force -ErrorAction SilentlyContinue }
    }
}

function Ensure-Git {
    <#
    .SYNOPSIS
        Best-effort Git install. Returns $true if git is (or becomes) available.
    .PARAMETER Quiet
        Suppress host output on the happy path.
    #>
    param([switch] $Quiet)

    if (Test-GitInstalled) {
        if (-not $Quiet) { Write-Host "  Git already installed — skipping" -ForegroundColor Gray }
        return $true
    }

    Write-Host "  Git not found in PATH. Installing..." -ForegroundColor Yellow

    if (Install-GitViaWinget) {
        Write-Host "  Git installed via winget" -ForegroundColor Green
    } elseif (Install-GitViaDownload) {
        Write-Host "  Git installed from Git-for-Windows release" -ForegroundColor Green
    } else {
        Write-Host "  Git install failed (no winget, download failed)." -ForegroundColor Red
        Write-Host "  Install manually from https://git-scm.com/download/win and re-run this installer." -ForegroundColor Yellow
        return $false
    }

    # PATH refresh — new process inherits updated PATH from registry
    $machinePath = [Environment]::GetEnvironmentVariable('Path','Machine')
    $userPath    = [Environment]::GetEnvironmentVariable('Path','User')
    $env:Path    = "$machinePath;$userPath"

    return (Test-GitInstalled)
}

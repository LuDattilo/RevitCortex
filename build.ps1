#requires -Version 5.1
<#
.SYNOPSIS
    Builds RiveTT for every supported Revit target and packages the per-user installer.

.DESCRIPTION
    Output layout, all of it generated and all of it gitignored:

        dist\
          2026\plugin\        add-in + tools built against Revit 2026.5
          2027\plugin\        add-in + tools built against Revit 2027
          server\             RiveTT.Server.exe, self-contained, shared by both
          RiveTT.addin        manifest, identical for both targets
          RiveTT-Setup-<version>.exe

    The server carries no Revit API reference, so it is built ONCE and shared. It used
    to be published into each version folder; self-contained that would be 38 MB
    duplicated for nothing.

    The installer is compiled by Inno Setup (ISCC.exe). When Inno is not installed the
    binaries are still produced and the packaging step is skipped with a warning -- a
    developer building locally does not need it to run the tests.

    ENCODING: this file is UTF-8 WITH BOM, and must stay that way. Windows PowerShell
    5.1 reads a BOM-less script as Windows-1252, so every multi-byte character becomes
    three garbage ones -- and some of those are curly quotes, which PowerShell honours
    as string delimiters. A box-drawing dash in a comment silently swallowed the rest
    of its line and stripped $LASTEXITCODE out of a guard. Comments and code here stay
    ASCII; only the user-facing French strings use accents.

.PARAMETER RevitVersion
    Build a single target instead of all of them. Accepts 2026 or 2027.

.PARAMETER SkipTests
    Skip the xUnit run. The 13 tests that load RevitAPI.dll at runtime need Revit
    installed; everything else runs anywhere.

.PARAMETER SkipInstaller
    Build the binaries but do not invoke Inno Setup.
#>
[CmdletBinding()]
param(
    # Omit to build every supported target. Both run on .NET 10 (Revit 2026.5 update);
    # the plugin and tools projects pick the matching Nice3point.Revit.Api.RevitAPI
    # version and the REVIT2027_OR_GREATER-gated code through this.
    [ValidateSet('2026', '2027')]
    [string[]] $RevitVersion = @('2026', '2027'),
    [switch] $SkipTests,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configuration = 'Release'
$distRoot = Join-Path $root 'dist'
$serverOut = Join-Path $distRoot 'server'
$issPath = Join-Path $root 'installer\RiveTT.iss'

# Single source of truth for the version, the same file the assemblies read.
$propsPath = Join-Path $root 'Directory.Build.props'
$version = ([xml](Get-Content $propsPath)).Project.PropertyGroup.Version
if (-not $version) { throw "Version introuvable dans $propsPath." }

function Invoke-Dotnet {
    <#
        Windows PowerShell 5.1 wraps a native command's stderr in an ErrorRecord, and
        with $ErrorActionPreference = 'Stop' that TERMINATES the script even when the
        command itself succeeded. `dotnet test` prints its failure list to stderr, so
        the 13 environmental failures (RevitAPI.dll absent off a Revit workstation)
        killed the whole build instead of warning and moving on.

        The exit code is the only trustworthy signal, so the preference is relaxed
        around the call and the result judged on $LASTEXITCODE alone.
    #>
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $FailureMessage,
        [switch] $WarnOnly
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & dotnet @Arguments } finally { $ErrorActionPreference = $previous }

    if ($LASTEXITCODE -ne 0) {
        if ($WarnOnly) { Write-Warning $FailureMessage } else { throw $FailureMessage }
    }
}

Push-Location $root
try {
    Write-Host "RiveTT $version - cibles : $($RevitVersion -join ', ')" -ForegroundColor Cyan

    # A stale dist\ is worse than none: a removed file would survive into the installer.
    if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $distRoot, $serverOut -Force | Out-Null

    # --- The server: no Revit reference, built once, shared by every target ---
    Write-Host 'Serveur MCP (autonome, sans dependance runtime)...' -ForegroundColor Cyan
    Invoke-Dotnet -Arguments @(
        'publish', '.\src\RiveTT.Server\RiveTT.Server.csproj',
        '-c', $configuration, '-o', $serverOut, '--nologo'
    ) -FailureMessage 'Echec de la publication du serveur.'
    # PublishSingleFile still emits the .pdb beside the exe; it has no business shipping.
    Get-ChildItem $serverOut -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

    # --- One plugin payload per Revit target ---
    foreach ($target in $RevitVersion) {
        Write-Host "Plugin Revit $target..." -ForegroundColor Cyan
        $versionArg = "-p:RevitVersion=$target"
        $pluginOut = Join-Path $distRoot "$target\plugin"
        New-Item -ItemType Directory -Path $pluginOut -Force | Out-Null

        Invoke-Dotnet -Arguments @(
            'build', '.\src\RiveTT.Tools\RiveTT.Tools.csproj',
            '-c', $configuration, $versionArg, '--nologo'
        ) -FailureMessage "Echec du build Tools pour Revit $target."

        Invoke-Dotnet -Arguments @(
            'build', '.\src\RiveTT.Plugin\RiveTT.Plugin.csproj',
            '-c', $configuration, $versionArg, '--nologo'
        ) -FailureMessage "Echec du build Plugin pour Revit $target."

        if (-not $SkipTests) {
            # WarnOnly: 13 tests load RevitAPI.dll at runtime and cannot pass without
            # Revit installed. A packaging run on a build agent must not stop there.
            Invoke-Dotnet -Arguments @(
                'test', '.\src\RiveTT.Tests\RiveTT.Tests.csproj',
                '-c', $configuration, $versionArg, '--nologo'
            ) -FailureMessage ("Tests en echec pour Revit $target. 13 echecs sont attendus " +
                               "hors poste Revit (chargement de RevitAPI.dll).") -WarnOnly
        }

        $pluginBuild = Join-Path $root 'src\RiveTT.Plugin\bin\Release\net10.0-windows'
        $toolsBuild = Join-Path $root 'src\RiveTT.Tools\bin\Release\net10.0-windows'

        # Tools are loaded dynamically by the add-in, so their whole dependency set must
        # travel with the plugin, not just RiveTT.Tools.dll. Symbols must not: they are
        # a third of a megabyte per target and Revit never reads them.
        #
        # Filtered with Where-Object, NOT -Exclude: Get-ChildItem silently ignores
        # -Exclude when the path comes from -LiteralPath, so the .pdb files shipped
        # anyway and the exclusion looked like it worked.
        foreach ($source in @($pluginBuild, $toolsBuild)) {
            Get-ChildItem -LiteralPath $source -File |
                Where-Object { $_.Extension -ne '.pdb' } |
                Copy-Item -Destination $pluginOut -Force
        }
    }

    # The manifest is identical for both targets -- its Assembly path is relative -- so
    # it sits once at the root of dist\ rather than being copied per version.
    Copy-Item .\src\RiveTT.Plugin\RiveTT.addin (Join-Path $distRoot 'RiveTT.addin') -Force

    $sizeMb = [math]::Round((Get-ChildItem $distRoot -Recurse -File |
        Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "Binaires prets dans dist\ ($sizeMb Mo)." -ForegroundColor Green

    # --- Installer ---
    if ($SkipInstaller) {
        Write-Host 'Installateur ignore (-SkipInstaller).' -ForegroundColor Yellow
        return
    }

    # The per-user location comes FIRST and is not an afterthought: a developer without
    # local admin installs Inno with --scope user, which lands in LOCALAPPDATA\Programs.
    # Checking only Program Files would have made the tool invisible to exactly the
    # people this whole admin-free packaging exists for.
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    # Last resort: on PATH, or installed somewhere else entirely.
    if (-not $iscc) {
        $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($onPath) { $iscc = $onPath.Source }
    }

    if (-not $iscc) {
        Write-Warning ("Inno Setup 6 introuvable : les binaires sont prets dans dist\ mais " +
                       "l'installateur n'a pas ete produit. Installez-le " +
                       "(winget install JRSoftware.InnoSetup) puis relancez, ou passez " +
                       "-SkipInstaller pour taire cet avertissement.")
        return
    }

    if ($RevitVersion.Count -lt 2) {
        Write-Warning ("Installateur construit avec une seule cible ($($RevitVersion -join ', ')) : " +
                       "il n'installera pas l'autre version de Revit.")
    }

    Write-Host "Compilation de l'installateur..." -ForegroundColor Cyan
    & $iscc "/DAppVersion=$version" "/O$distRoot" $issPath
    if ($LASTEXITCODE -ne 0) { throw "ISCC a echoue (code $LASTEXITCODE)." }

    $setup = Join-Path $distRoot "RiveTT-Setup-$version.exe"
    if (Test-Path $setup) {
        $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
        Write-Host "Installateur pret : $setup ($setupMb Mo)" -ForegroundColor Green
        Write-Host 'Aucune elevation administrateur requise a son execution.'
    }
}
finally { Pop-Location }

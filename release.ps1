#requires -Version 5.1
<#
.SYNOPSIS
    One-click release for RevitCortex.

.DESCRIPTION
    Orchestrates the full release flow:
      1. Bump version in installer/.iss and Plugin.csproj
      2. Commit + tag + push (main and --tags)
      3. Build the ZIP via build-release.ps1
      4. Copy the ZIP into the OneDrive distribution folder
      5. Update the public update manifest (revitcortex-releases repo) so
         installed plugins detect the new version on next Revit start.

    The only thing this script does NOT do is replace the file behind the
    SharePoint download link (that needs a browser). If the existing
    downloadUrl in latest.json points at a file you intend to swap in
    SharePoint "replace file" UI, do it right after this script finishes.

.PARAMETER Version
    Full semver like "1.0.5". No leading v.

.PARAMETER Changelog
    Optional user-facing changelog text that goes into latest.json.
    If omitted, the script uses the commit subject line.

.PARAMETER SkipManifest
    Skip the public manifest update step. Use when you want to build and
    copy to OneDrive first, then publish the manifest later after verifying.

.PARAMETER DownloadUrl
    Optional override for the downloadUrl field in latest.json. If omitted,
    the URL is auto-derived from -Version using the GitHub Releases asset
    pattern of the revitcortex-releases repo (the asset is uploaded by
    this script via `gh release create`). Pass this only when you want to
    host the zip somewhere else (SharePoint, OneDrive public link, ...).

.EXAMPLE
    .\release.ps1 -Version 1.0.5 -Changelog "Fix X, improve Y"

.EXAMPLE
    .\release.ps1 -Version 1.0.5 -SkipManifest
    # Build and push the release, publish the manifest later manually.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $Changelog,

    [switch] $SkipManifest,

    [string] $DownloadUrl
)

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  OK $msg"   -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  ! $msg"    -ForegroundColor Yellow }
function Write-Fail($msg) { Write-Host "  X $msg"    -ForegroundColor Red }

function Invoke-OrDie([string]$cmd, [string]$desc) {
    $out = & cmd.exe /c "$cmd 2>&1"
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "$desc failed"
        $out | ForEach-Object { Write-Host "    $_" }
        throw "$desc failed (exit $LASTEXITCODE)"
    }
    return $out
}

# ── Pre-flight ──────────────────────────────────────────────────────────────
Write-Step "Pre-flight checks"

Set-Location $RepoRoot

$gitStatus = & git status --porcelain 2>&1
if ($LASTEXITCODE -ne 0) { throw "Not a git repo or git not in PATH" }

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) not found in PATH. Install it from https://cli.github.com/ — needed to create the release and upload the asset."
}
& gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "gh CLI is installed but not authenticated. Run: gh auth login" }
Write-Ok "gh CLI authenticated"
$dirty = $gitStatus | Where-Object { $_ -and ($_ -notmatch '^\?\?') -and ($_ -notmatch 'settings\.local\.json$') }
if ($dirty) {
    Write-Fail "Working tree has uncommitted changes other than allowed files:"
    $dirty | ForEach-Object { Write-Host "    $_" }
    throw "Commit or stash before releasing"
}
Write-Ok "Working tree clean"

$existingTag = & git tag --list "v$Version"
if ($existingTag) { throw "Tag v$Version already exists" }
Write-Ok "Tag v$Version is available"

$oneDriveDir = Join-Path $env:USERPROFILE 'OneDrive - GPA Ingegneria Srl\Documenti\RevitCortex\distribution'
if (-not (Test-Path $oneDriveDir)) { throw "OneDrive distribution folder not found: $oneDriveDir" }
Write-Ok "OneDrive folder reachable"

# ── 1. Bump version ─────────────────────────────────────────────────────────
Write-Step "Bumping version to $Version"

$issPath = Join-Path $RepoRoot 'installer\RevitCortex.iss'
$csproj  = Join-Path $RepoRoot 'src\RevitCortex.Plugin\RevitCortex.Plugin.csproj'

$issText = Get-Content $issPath -Raw
$issNew  = $issText -replace '#define MyAppVersion "[\d\.]+"', "#define MyAppVersion `"$Version`""
if ($issNew -eq $issText) { throw "Could not bump MyAppVersion in $issPath" }
[System.IO.File]::WriteAllText($issPath, $issNew, [System.Text.UTF8Encoding]::new($false))
Write-Ok "installer\\RevitCortex.iss"

$cspText = Get-Content $csproj -Raw
$cspNew  = $cspText `
    -replace '<Version>[\d\.]+</Version>',                 "<Version>$Version</Version>" `
    -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>" `
    -replace '<FileVersion>[\d\.]+</FileVersion>',         "<FileVersion>$Version.0</FileVersion>"
if ($cspNew -eq $cspText) { throw "Could not bump version in $csproj" }
[System.IO.File]::WriteAllText($csproj, $cspNew, [System.Text.UTF8Encoding]::new($false))
Write-Ok "src\\RevitCortex.Plugin\\RevitCortex.Plugin.csproj"

# ── 2. Commit + tag + push ──────────────────────────────────────────────────
Write-Step "Commit + tag v$Version + push"

& git add installer/RevitCortex.iss src/RevitCortex.Plugin/RevitCortex.Plugin.csproj | Out-Null
$commitMsg = if ($Changelog) { "chore(release): bump to v$Version`n`n$Changelog" } else { "chore(release): bump to v$Version" }
& git commit -m $commitMsg | Out-Null
if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
Write-Ok "committed"

& git tag "v$Version" | Out-Null
Write-Ok "tagged v$Version"

& git push origin main --tags 2>&1 | ForEach-Object { Write-Host "    $_" }
if ($LASTEXITCODE -ne 0) { throw "git push failed" }
Write-Ok "pushed"

# ── 3. Build ZIP ────────────────────────────────────────────────────────────
Write-Step "Building release ZIP"

$buildScript = Join-Path $RepoRoot 'build-release.ps1'
& $buildScript -Version $Version
if ($LASTEXITCODE -ne 0) { throw "build-release.ps1 failed" }

$zipPath = Join-Path $RepoRoot "RevitCortex-v$Version.zip"
if (-not (Test-Path $zipPath)) { throw "Expected ZIP not found at $zipPath" }
$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Ok "built RevitCortex-v$Version.zip ($zipSizeMB MB)"

# ── 4. Copy to OneDrive ─────────────────────────────────────────────────────
Write-Step "Copying ZIP to OneDrive"

Copy-Item $zipPath (Join-Path $oneDriveDir "RevitCortex-v$Version.zip") -Force
Write-Ok "OneDrive copy complete"

# ── 5. Create/update GitHub Release (idempotent) ────────────────────────────
# Uploading the asset BEFORE writing the manifest is what makes the manifest
# safe to publish: a manifest claiming v$Version is only valid if the
# corresponding zip is actually downloadable. Doing this in a single script
# eliminates the historical "manifest says v1.0.19, asset is v1.0.18" loop.
Write-Step "Publishing GitHub Release v$Version"

$ghRepo = "LuDattilo/revitcortex-releases"
$assetName = "RevitCortex-v$Version.zip"
$releaseNotes = if ($Changelog) { $Changelog } else { "v$Version release." }

& gh release view "v$Version" --repo $ghRepo 1>$null 2>&1
$releaseExists = ($LASTEXITCODE -eq 0)

if ($releaseExists) {
    Write-Warn "Release v$Version already exists — re-uploading asset (--clobber)"
    & gh release upload "v$Version" $zipPath --repo $ghRepo --clobber 2>&1 | ForEach-Object { Write-Host "    $_" }
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
} else {
    & gh release create "v$Version" $zipPath --repo $ghRepo --title "RevitCortex v$Version" --notes "$releaseNotes" 2>&1 | ForEach-Object { Write-Host "    $_" }
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}
Write-Ok "Asset $assetName attached to v$Version on $ghRepo"

# ── 6. Publish public manifest ──────────────────────────────────────────────
if ($SkipManifest) {
    Write-Warn "Manifest update skipped (SkipManifest flag). Installed plugins will not detect v$Version until you update latest.json manually."
} else {
    Write-Step "Publishing public update manifest"

    # Auto-derive URL from version unless caller explicitly overrides.
    # Default matches what step 5 just uploaded.
    $resolvedUrl = if ($DownloadUrl) { $DownloadUrl } `
                   else { "https://github.com/$ghRepo/releases/download/v$Version/$assetName" }

    # Hard sanity check: refuse to ship a manifest where (version, url) disagree.
    # This is the exact check that, if it had been here, would have prevented
    # the v1.0.19/v1.0.18 mismatch loop.
    if ($resolvedUrl -notmatch [regex]::Escape("v$Version")) {
        throw "downloadUrl must reference 'v$Version' but got: $resolvedUrl. Refusing to publish a manifest with mismatched (version, url) — this causes infinite update-available loops on every installed plugin."
    }

    # Verify the URL actually serves the asset before broadcasting it.
    try {
        $head = Invoke-WebRequest -Uri $resolvedUrl -Method Head -MaximumRedirection 5 -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
        if ($head.StatusCode -lt 200 -or $head.StatusCode -ge 300) { throw "HEAD returned $($head.StatusCode)" }
        Write-Ok "downloadUrl reachable (HTTP $($head.StatusCode))"
    } catch {
        throw "downloadUrl not reachable: $resolvedUrl`nReason: $_`nThe GitHub Release upload may not have propagated yet, or -DownloadUrl points to a missing file. Retry in a few seconds, or pass -DownloadUrl with a working URL."
    }

    $cloneDir = Join-Path $env:TEMP "rc-releases-$([guid]::NewGuid().ToString('N'))"
    try {
        & git clone --depth 1 "https://github.com/$ghRepo.git" $cloneDir 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { throw "git clone failed" }

        $manifestPath = Join-Path $cloneDir 'latest.json'
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

        $manifest.version = $Version
        $manifest.downloadUrl = $resolvedUrl
        $manifest.changelog = $releaseNotes

        $json = $manifest | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))

        Push-Location $cloneDir
        try {
            & git add latest.json | Out-Null
            & git -c user.name="LuDattilo" -c user.email="luigi.dattilo@gpapartners.com" commit -m "Update manifest to v$Version" 2>&1 | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -ne 0) { throw "manifest commit failed" }
            & git push origin main 2>&1 | ForEach-Object { Write-Host "    $_" }
            if ($LASTEXITCODE -ne 0) { throw "manifest push failed" }
        } finally { Pop-Location }

        Write-Ok "manifest pushed (version=$Version, url=$resolvedUrl)"
    } finally {
        if (Test-Path $cloneDir) { Remove-Item $cloneDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# ── Summary ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================" -ForegroundColor Green
Write-Host "  RevitCortex v$Version released"                 -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Tag:       v$Version (pushed to origin)"            -ForegroundColor White
Write-Host "  ZIP:       $zipPath ($zipSizeMB MB)"                  -ForegroundColor White
Write-Host "  OneDrive:  $oneDriveDir"                              -ForegroundColor White
Write-Host ("  GH Asset:  https://github.com/{0}/releases/tag/v{1}" -f $ghRepo, $Version) -ForegroundColor White
if (-not $SkipManifest) {
    Write-Host "  Manifest:  https://raw.githubusercontent.com/LuDattilo/revitcortex-releases/main/latest.json" -ForegroundColor White
}
Write-Host ""
Write-Host "  Installed plugins will detect v$Version on next Revit start." -ForegroundColor Green
Write-Host ""

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

    [switch] $SkipManifest
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

# ── 5. Publish GitHub release with ZIP asset ────────────────────────────────
$ghDownloadUrl = "https://github.com/LuDattilo/revitcortex-releases/releases/download/v$Version/RevitCortex-v$Version.zip"

if ($SkipManifest) {
    Write-Warn "GitHub release + manifest update skipped (SkipManifest flag). Installed plugins will not detect v$Version until you publish them manually."
} else {
    Write-Step "Publishing GitHub release v$Version"

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) { throw "gh CLI not found in PATH — install GitHub CLI or run with -SkipManifest" }

    # Idempotent: if the tag is already a release, just upload the asset (clobber)
    & gh release view "v$Version" --repo LuDattilo/revitcortex-releases 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Warn "release v$Version already exists — uploading asset with --clobber"
        & gh release upload "v$Version" $zipPath --repo LuDattilo/revitcortex-releases --clobber 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    } else {
        $notes = if ($Changelog) { $Changelog } else { "v$Version release." }
        & gh release create "v$Version" $zipPath --repo LuDattilo/revitcortex-releases --title "RevitCortex v$Version" --notes $notes 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
    }
    Write-Ok "GitHub release published with asset"

    # ── 6. Update public manifest (downloadUrl ALWAYS derived from tag) ─────
    Write-Step "Updating public update manifest"

    $cloneDir = Join-Path $env:TEMP "rc-releases-$([guid]::NewGuid().ToString('N'))"
    try {
        & git clone --depth 1 https://github.com/LuDattilo/revitcortex-releases.git $cloneDir 2>&1 | ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) { throw "git clone failed" }

        $manifestPath = Join-Path $cloneDir 'latest.json'
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

        $newChangelog = if ($Changelog) { $Changelog } else { "v$Version release." }
        $manifest.version     = $Version
        $manifest.downloadUrl = $ghDownloadUrl
        $manifest.changelog   = $newChangelog

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

        Write-Ok "manifest pushed (version=$Version, downloadUrl=$ghDownloadUrl)"
    } finally {
        if (Test-Path $cloneDir) { Remove-Item $cloneDir -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ── 7. End-to-end verification (the stop-loss) ──────────────────────────
    Write-Step "Verifying release is consumable end-to-end"

    # 7a. Asset reachable via downloadUrl (HEAD must follow to 200)
    try {
        $head = Invoke-WebRequest -Uri $ghDownloadUrl -Method Head -MaximumRedirection 5 -UseBasicParsing -ErrorAction Stop
        if ($head.StatusCode -ne 200) { throw "HTTP $($head.StatusCode)" }
        Write-Ok "downloadUrl returns 200 ($($head.Headers.'Content-Length') bytes)"
    } catch {
        Write-Fail "downloadUrl unreachable: $_"
        throw "Verification failed: clients will fail to download v$Version"
    }

    # 7b. Manifest authoritative source (api.github.com bypasses raw CDN cache)
    #     and has the matching version + downloadUrl.
    $apiUrl = 'https://api.github.com/repos/LuDattilo/revitcortex-releases/contents/latest.json?ref=main'
    try {
        $apiResp = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = 'RevitCortex-release-verify' } -ErrorAction Stop
        $b64 = (($apiResp.content -join '') -replace '\s','')
        $manifestText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
        $live = $manifestText | ConvertFrom-Json
        if ($live.version -ne $Version) {
            throw "manifest version is '$($live.version)', expected '$Version'"
        }
        if ($live.downloadUrl -ne $ghDownloadUrl) {
            throw "manifest downloadUrl mismatch.`n      expected: $ghDownloadUrl`n      got:      $($live.downloadUrl)"
        }
        Write-Ok "manifest matches release (version + downloadUrl consistent)"
    } catch {
        Write-Fail $_.Exception.Message
        throw "Verification failed: manifest is inconsistent — UpdateChecker will misbehave"
    }

    Write-Warn "raw.githubusercontent.com CDN may take up to ~5 min to reflect the new manifest. Clients will see the update once that propagates."
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
if (-not $SkipManifest) {
    Write-Host "  Release:   https://github.com/LuDattilo/revitcortex-releases/releases/tag/v$Version" -ForegroundColor White
    Write-Host "  Manifest:  https://raw.githubusercontent.com/LuDattilo/revitcortex-releases/main/latest.json" -ForegroundColor White
}
Write-Host ""

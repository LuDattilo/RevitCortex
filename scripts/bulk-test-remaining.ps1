# Bulk test — remaining groups: workflows, exports, IFC validate, PBI read, link reads,
# schedule data, selection sets, and a few not-yet-covered read tools. Plus the isolated
# lines_per_view_count probe (the queue-poisoner) LAST.
. "$PSScriptRoot\bulk-test-bridge.ps1"

$results = New-Object System.Collections.ArrayList
$stamp = '0529' + (Get-Random -Minimum 1000 -Maximum 9999)
$tmp = Join-Path $env:TEMP ("rc_bulk2_" + $stamp); New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function Run {
    param([string]$Tool, [hashtable]$Params = @{}, [int]$Timeout = 60, [string]$Note='')
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $json = Invoke-RC -Method $Tool -Params $Params -TimeoutSec $Timeout
    $sw.Stop()
    $status = 'OK'
    try {
        $o = $json | ConvertFrom-Json
        if ($null -ne $o.error) { $status = 'ERR' }
        elseif ($null -ne $o.result -and $o.result.success -eq $false) { $status = 'FAIL' }
    } catch { if ($json -match 'transport') { $status='ERR' } }
    $snip = $json.Substring(0, [Math]::Min(300, $json.Length))
    [void]$results.Add([pscustomobject]@{ tool=$Tool; status=$status; ms=$sw.ElapsedMilliseconds; note=$Note; snippet=$snip })
    Write-Host ("[{0,-4}] {1,-36} {2,6}ms {3}" -f $status, $Tool, $sw.ElapsedMilliseconds, $Note)
}

# ── Schedule / data reads not yet covered ──
Run 'create_preset_schedule' @{ action='list' } 20 'list presets?'
# get_schedule_data needs a scheduleId — find one first
$sched = Invoke-RC -Method 'create_schedule' -Params @{ categoryName='OST_Walls'; name='RC_BULK2_SD_'+$stamp } -TimeoutSec 30
$sid = ($sched | ConvertFrom-Json).result.scheduleId
if ($sid) {
    Run 'get_schedule_data' @{ scheduleId=$sid; maxRows=5 } 30 "scheduleId=$sid"
    Run 'modify_schedule' @{ scheduleId=$sid; action='set_filter'; filterField='Family and Type'; filterType='contains'; filterValue='MUR' } 25 'set_filter (Phase0)'
    Run 'export_schedule' @{ scheduleId=$sid; format='CSV'; outputDirectory=$tmp } 30 'export CSV'
    Run 'duplicate_schedule' @{ scheduleId=$sid; newName='RC_BULK2_SDUP_'+$stamp } 20 'real'
}

# ── Exports (write to temp dir, real) ──
Run 'export_elements_data' @{ categoryFilter='OST_Doors'; maxElements=5; outputFormat='csv'; outputDirectory=$tmp } 40 'real csv'
Run 'export_room_data' @{ maxResults=10; outputDirectory=$tmp } 40 'real'
Run 'export_to_excel' @{ categories=@('OST_Doors'); outputDirectory=$tmp } 60 'real xlsx'
Run 'export_shared_parameter_file' @{ outputDirectory=$tmp } 30 'real'
Run 'batch_export' @{ format='PDF'; viewIds=@(); sheetIds=@(); outputDirectory=$tmp } 30 'PDF empty-set (Phase0) — expect graceful'

# ── IFC validate + export dryish ──
Run 'ifc_validate_request' @{ filePath=(Join-Path $env:USERPROFILE 'Documents\Snowdon Towers Sample Architectural_luigi.dattilo7VWCL.rvt') } 20 'validate'
Run 'ifc_get_export_configuration' @{ configurationName='IFC 2x3 Coordination View 2.0' } 20 'get config'

# ── Workflows (read-heavy composites) ──
Run 'workflow_model_audit' @{ includeWarnings=$true; includeFamilies=$false; maxWarnings=10 } 90 'composite audit'

# ── Link reads not yet covered ──
Run 'get_linked_elements' @{ categories=@('OST_Walls'); maxElements=5 } 40 'linked elems'
Run 'get_link_transform' @{ linkInstanceId=2403932 } 20 'transform'

# ── Selection set lifecycle (real, small) ──
Run 'save_selection' @{ name='RC_BULK2_SEL_'+$stamp; elementIds=@(619340) } 20 'real save'
Run 'load_selection' @{ action='list' } 15 'list'

# ── ISOLATED: lines_per_view_count (queue poisoner) — LAST, generous timeout ──
Run 'lines_per_view_count' @{ threshold=20; limit=3 } 120 'ISOLATED heavy probe'

$results | ConvertTo-Json -Depth 6 | Set-Content "$PSScriptRoot\bulk2-remaining-results.json" -Encoding UTF8
$grp = $results | Group-Object status | Sort-Object Name
Write-Host "`n=== REMAINING DONE ==="; $grp | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Name, $_.Count) }

# Bulk test — WRITE / create tools via the plugin bridge (port 8080).
# Strategy: dryRun=true wherever supported (validates the tool without firing the native
# confirmation dialog). Real creates use RC_BULK2_* names (no confirmation — not destructive).
# Confirmation-gated tools without dryRun get a short timeout; if a modal dialog blocks them
# the call times out and is recorded as BLOCKED rather than hanging the run.
# NOTHING here disables sandbox/audit; destructive ops stay in dryRun.
. "$PSScriptRoot\bulk-test-bridge.ps1"

$results = New-Object System.Collections.ArrayList
$stamp = '0529' + (Get-Random -Minimum 1000 -Maximum 9999)

function Run {
    param([string]$Tool, [hashtable]$Params = @{}, [int]$Timeout = 30, [string]$Note = '')
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $json = Invoke-RC -Method $Tool -Params $Params -TimeoutSec $Timeout
    $sw.Stop()
    $status = 'OK'
    try {
        $o = $json | ConvertFrom-Json
        if ($null -ne $o.error) { $status = 'ERR' }
        elseif ($null -ne $o.result -and $o.result.success -eq $false) {
            $status = if ($o.result.error.code -eq 'Cancelled') { 'CANCEL' } else { 'FAIL' }
        }
        else { $status = 'OK' }
    } catch {
        if ($json -match 'transport') { $status = 'BLOCKED?' } else { $status = 'OK' }
    }
    $snip = $json.Substring(0, [Math]::Min(320, $json.Length))
    [void]$results.Add([pscustomobject]@{ tool = $Tool; status = $status; ms = $sw.ElapsedMilliseconds; note = $Note; snippet = $snip })
    Write-Host ("[{0,-7}] {1,-34} {2,6}ms {3}" -f $status, $Tool, $sw.ElapsedMilliseconds, $Note)
}

Write-Host "=== WRITE battery (stamp RC_BULK2_$stamp) ===`n"

# ── dryRun-supported destructive/bulk tools (validate, no real change) ──
Run 'set_element_parameters' @{ requests=@(@{ elementId=619340; parameterName='Comments'; value='RC_BULK2_'+$stamp }); dryRun=$true } 20 'dryRun'
Run 'bulk_modify_parameter_values' @{ parameterName='Comments'; categoryName='Walls'; operation='set'; value='RC_BULK2'; dryRun=$true } 30 'dryRun'
Run 'clear_parameter_values' @{ categoryName='Walls'; parameterName='Comments'; dryRun=$true } 20 'dryRun'
Run 'purge_unused' @{ dryRun=$true } 60 'dryRun'
Run 'set_compound_structure' @{ typeId=0; action='replace'; dryRun=$true } 15 'dryRun (typeId placeholder)'
Run 'set_material_properties' @{ requests=@(@{ materialId=0; name='x' }); dryRun=$true } 15 'dryRun'
Run 'batch_rename' @{ category='views'; operation='add_prefix'; prefix='RC_BULK2_'; dryRun=$true } 20 'dryRun'
Run 'rename_views' @{ action='add_prefix'; prefix='ZZ_'; dryRun=$true } 20 'dryRun'
Run 'rename_families' @{ operation='add_prefix'; prefix='ZZ_'; dryRun=$true } 20 'dryRun'
Run 'wipe_empty_tags' @{ dryRun=$true } 20 'dryRun'
Run 'renumber_elements' @{ category='OST_Doors'; dryRun=$true } 20 'dryRun'
Run 'match_element_properties' @{ sourceId=619340; targetIds=@(); parameterNames=@('Comments'); dryRun=$true } 15 'dryRun'

# ── Real small creates (no confirmation — not destructive) ──
Run 'create_level' @{ action='create'; name='RC_BULK2_LVL_'+$stamp; elevation=88000; isBuildingStory=$true } 25 'real'
Run 'create_grid' @{ action='create'; xCount=1; xStartLabel='ZZ'+$stamp.Substring(0,2) } 25 'real'
Run 'create_sheet' @{ number='RC2-'+$stamp; name='RC_BULK2_SHEET' } 25 'real'
Run 'create_revision' @{ action='create'; description='RC_BULK2 rev '+$stamp; issued=$false; visibility='cloud_and_tag' } 25 'real'
Run 'create_view' @{ viewType='FloorPlan'; name='RC_BULK2_FP_'+$stamp; levelName='L1 - Block 43' } 30 'real'
Run 'create_view' @{ viewType='Drafting'; name='RC_BULK2_DRAFT_'+$stamp } 25 'real (drafting — was a Phase0 bug)'
Run 'create_schedule' @{ categoryName='OST_Walls'; name='RC_BULK2_SCHED_'+$stamp; fields=@('Family and Type','Count') } 30 'real'
Run 'create_text_note' @{ textNotes=@(@{ text='RC_BULK2'; position=@{x=0;y=0;z=0}; rotation=45; leader='left' }) } 25 'real (rotation+leader — Phase3e)'
Run 'duplicate_system_type' @{ action='duplicate'; sourceTypeName='Generic - 200mm'; category='Walls'; newName='RC_BULK2_WT_'+$stamp } 25 'real'
Run 'create_array' @{ elementIds=@(619340); arrayType='linear'; count=3; spacingX=2000; associative=$true } 30 'real (associative ArrayElement — Phase4e)'

# ── Confirmation-gated, no dryRun: short timeout, may BLOCK on dialog ──
Run 'set_project_info' @{ status='RC_BULK2 CD' } 12 'confirm-gated'
Run 'manage_worksets' @{ action='create'; name='RC_BULK2_WS_'+$stamp } 12 'confirm-gated'

$results | ConvertTo-Json -Depth 6 | Set-Content "$PSScriptRoot\bulk2-write-results.json" -Encoding UTF8
$grp = $results | Group-Object status | Sort-Object Name
Write-Host "`n=== WRITE DONE ==="
$grp | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Name, $_.Count) }

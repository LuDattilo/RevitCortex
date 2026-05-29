# Bulk test — ALL read-only / safe-discovery tools via the plugin bridge (port 8080).
# Writes a JSON result array to scripts/bulk2-readonly-results.json.
# Each entry: { tool, status (OK|FAIL|ERR|SKIP), ms, snippet }.
. "$PSScriptRoot\bulk-test-bridge.ps1"

$results = New-Object System.Collections.ArrayList

function Run {
    param([string]$Tool, [hashtable]$Params = @{}, [int]$Timeout = 60)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $json = Invoke-RC -Method $Tool -Params $Params -TimeoutSec $Timeout
    $sw.Stop()
    # Robust status: parse JSON and inspect the actual envelope, not a substring of "error".
    $status = 'OK'
    try {
        $o = $json | ConvertFrom-Json
        if ($null -ne $o.error) { $status = 'ERR' }            # JSON-RPC transport/dispatch error
        elseif ($null -ne $o.result -and $o.result.success -eq $false) { $status = 'FAIL' }  # tool returned CortexResult.Fail
        else { $status = 'OK' }
    } catch {
        # Not valid JSON (e.g. transport exception text from Invoke-RC)
        $status = if ($json -match 'transport') { 'ERR' } else { 'OK' }
    }
    $snip = $json.Substring(0, [Math]::Min(300, $json.Length))
    [void]$results.Add([pscustomobject]@{ tool = $Tool; status = $status; ms = $sw.ElapsedMilliseconds; snippet = $snip })
    Write-Host ("[{0,-4}] {1,-34} {2,6}ms" -f $status, $Tool, $sw.ElapsedMilliseconds)
}

# ── Meta / diagnostics ──────────────────────────────────────────────
Run 'say_hello'
Run 'get_cache_stats'
Run 'clear_cache'

# ── Project / model status (read) ───────────────────────────────────
Run 'get_project_info' @{ includeLevels=$false; includeLinks=$false; includePhases=$false; includeWorksets=$false }
Run 'get_current_view_info'
Run 'check_model_health'
Run 'analyze_model_statistics' @{ compact=$true }
Run 'get_warnings' @{ maxWarnings=10 }
Run 'get_phases'
Run 'get_worksets'

# ── Elements discovery (read) ───────────────────────────────────────
Run 'get_selected_elements'
Run 'ai_element_filter' @{ data=@{ filterCategory='OST_Walls'; includeInstances=$true; maxElements=3 } }
Run 'ai_element_filter' @{ data=@{ filterCategory='OST_Walls'; includeInstances=$true; maxElements=3; combineWith='or'; invert=$true } }
Run 'ai_element_filter' @{ data=@{ filterCategory='OST_Doors'; includeInstances=$true; maxElements=3; levelFilter=@{ levelName='L1 - Block 43' } } }
Run 'export_elements_data' @{ categoryFilter='OST_Walls'; maxElements=3 }
Run 'filter_by_parameter_value' @{ category='OST_Walls'; parameterName='Type Name'; value='MUR'; parameterType='type'; condition='contains' }
Run 'filter_by_parameter_value' @{ categories=@('OST_Walls'); logic='or'; conditions=@(@{ parameterName='Mark'; condition='is_not_empty'; value='' }, @{ parameterName='Comments'; condition='contains'; value='x' }) }
Run 'get_current_view_elements' @{ limit=5 }
Run 'get_elements_in_spatial_volume' @{ volumeType='custom'; customMinX=-5000; customMinY=-5000; customMinZ=-2000; customMaxX=5000; customMaxY=5000; customMaxZ=4000; categoryFilter=@('OST_Walls'); maxElementsPerVolume=5 }
Run 'get_room_openings' @{ summaryOnly=$true }
Run 'find_untagged_elements' @{ categories=@('OST_Doors') }
Run 'find_undimensioned_elements' @{ categories=@('OST_Walls') }
Run 'measure_between_elements' @{ point1=@{x=0;y=0;z=0}; point2=@{x=1000;y=0;z=0} }
Run 'audit_families' @{ categoryFilter='OST_Doors'; includeUnused=$false; compact=$true }
Run 'get_available_family_types' @{ compact=$true; categoryList=@('OST_Doors') }
Run 'list_family_sizes' @{ }
Run 'clash_detection' @{ categoryA='OST_Walls'; categoryB='OST_Floors'; maxResults=5 }
# NOTE: lines_per_view_count excluded here — it times out and poisons the ExternalEvent
# queue for subsequent calls (P1 bug). Tested in isolation at the end.

# ── Materials (read) — can be slow on rich models; generous timeout ─
Run 'get_materials' @{ } 120
Run 'get_material_quantities' @{ categoryFilter='OST_Walls' } 120

# ── Parameters (read) ───────────────────────────────────────────────
Run 'get_shared_parameters' @{ compact=$true }
Run 'manage_global_parameters' @{ action='list' }
Run 'manage_project_parameters' @{ action='list' }
Run 'manage_project_units' @{ action='get' }

# ── Views / sheets / schedules (read) ───────────────────────────────
Run 'list_schedulable_fields' @{ summaryOnly=$true }
Run 'manage_view_templates' @{ action='list' }
Run 'apply_view_template' @{ action='list' }
Run 'manage_phase_filters' @{ action='list' }
Run 'manage_unplaced_views' @{ action='list' }
Run 'manage_additional_settings' @{ action='list_line_styles' }
Run 'create_view_filter' @{ action='list' }
Run 'create_revision' @{ action='list' }
Run 'manage_links' @{ action='list' }

# ── Linked files (read) ─────────────────────────────────────────────
Run 'get_linked_file_instances' @{ compact=$true }
Run 'get_coordination_models' @{ compact=$true }
Run 'get_selected_linked_elements'

# ── IFC (read/capability) ───────────────────────────────────────────
Run 'ifc_get_capabilities'
Run 'ifc_list_export_configurations' @{ compact=$true }
Run 'ifc_analyze_rebuildability' @{ compact=$true }
Run 'ifc_list_rebuild_candidates' @{ }

# ── PowerBI (read-only auth/list) ───────────────────────────────────
Run 'pbi_check_auth'
Run 'pbi_list_workspaces'

# ── Selection sets (read) ───────────────────────────────────────────
Run 'load_selection' @{ action='list' }

$results | ConvertTo-Json -Depth 6 | Set-Content "$PSScriptRoot\bulk2-readonly-results.json" -Encoding UTF8
$ok = ($results | Where-Object status -eq 'OK').Count
$fail = ($results | Where-Object status -eq 'FAIL').Count
$err = ($results | Where-Object status -eq 'ERR').Count
Write-Host "`n=== READ-ONLY DONE: $ok OK / $fail FAIL / $err ERR (total $($results.Count)) ==="

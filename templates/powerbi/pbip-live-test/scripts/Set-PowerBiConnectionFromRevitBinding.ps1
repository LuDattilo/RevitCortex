param(
    [string]$SettingsPath = "$env:USERPROFILE\.revitcortex\powerbi-live.json",

    [string]$ProjectKey,

    [string]$WorkspaceNameOrId,

    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SettingsPath)) {
    throw "RevitCortex Power BI settings not found at $SettingsPath"
}

$settings = Get-Content $SettingsPath -Raw | ConvertFrom-Json
if (-not $settings.ProjectBindings) {
    throw "No ProjectBindings found in $SettingsPath. Publish from Revit first."
}

$bindings = @()
$settings.ProjectBindings.PSObject.Properties | ForEach-Object {
    $binding = $_.Value
    $bindings += [pscustomobject]@{
        Key = $_.Name
        WorkspaceId = $binding.WorkspaceId
        WorkspaceName = $binding.WorkspaceName
        WorkspaceDisplayName = $binding.WorkspaceDisplayName
        DatasetId = $binding.DatasetId
        DatasetName = $binding.DatasetName
        ProjectName = $binding.ProjectName
        SchemaVersion = $binding.SchemaVersion
        UpdatedAtUtc = $binding.UpdatedAtUtc
    }
}

if ($ProjectKey) {
    $selected = $bindings | Where-Object { $_.Key -eq $ProjectKey } | Select-Object -First 1
    if (-not $selected) {
        throw "Project binding '$ProjectKey' was not found in $SettingsPath"
    }
} else {
    $selected = $bindings |
        Sort-Object @{ Expression = { if ($_.UpdatedAtUtc) { [datetime]$_.UpdatedAtUtc } else { [datetime]::MinValue } }; Descending = $true } |
        Select-Object -First 1
}

if (-not $selected.WorkspaceId -or -not $selected.DatasetId -or -not $selected.DatasetName) {
    throw "Selected binding is incomplete. WorkspaceId, DatasetId and DatasetName are required."
}

if (-not $Force) {
    throw "RevitCortex bindings currently point to REST push semantic models. Power BI Desktop PBIP byConnection uses XMLA-style live connections, and Microsoft does not expose REST push semantic models through XMLA. Re-run with -Force only if this binding was migrated to an XMLA-accessible semantic model."
}

if (-not $WorkspaceNameOrId) {
    if ($selected.WorkspaceName) {
        $WorkspaceNameOrId = $selected.WorkspaceName
    } elseif ($selected.WorkspaceDisplayName) {
        $WorkspaceNameOrId = $selected.WorkspaceDisplayName
    } else {
        throw "The local RevitCortex binding contains WorkspaceId '$($selected.WorkspaceId)' but not the workspace display name required by Power BI Desktop live connections. Re-run with -WorkspaceNameOrId '<Power BI workspace display name>'."
    }
}

$connectionScript = Join-Path $PSScriptRoot "Set-PowerBiConnection.ps1"
& $connectionScript `
    -WorkspaceNameOrId $WorkspaceNameOrId `
    -SemanticModelName $selected.DatasetName `
    -SemanticModelId $selected.DatasetId

Write-Host ""
Write-Host "Selected RevitCortex binding:"
Write-Host "  Project:        $($selected.ProjectName)"
Write-Host "  Schema version: $($selected.SchemaVersion)"
Write-Host "  Binding key:    $($selected.Key)"

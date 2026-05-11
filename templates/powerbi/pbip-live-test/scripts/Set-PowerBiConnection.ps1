param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceNameOrId,

    [Parameter(Mandatory = $true)]
    [string]$SemanticModelName,

    [Parameter(Mandatory = $true)]
    [string]$SemanticModelId
)

$ErrorActionPreference = "Stop"

$templateRoot = Split-Path -Parent $PSScriptRoot
$definitionPath = Join-Path $templateRoot "RevitCortexLiveTest.Report\definition.pbir"

if (-not (Test-Path $definitionPath)) {
    throw "definition.pbir not found at $definitionPath"
}

$dataSource = "powerbi://api.powerbi.com/v1.0/myorg/$WorkspaceNameOrId"
$connectionString = "Data Source=`"$dataSource`";initial catalog=$SemanticModelName;access mode=readonly;integrated security=ClaimsToken;semanticmodelid=$SemanticModelId"

$definition = Get-Content $definitionPath -Raw | ConvertFrom-Json
$definition.datasetReference.byConnection.connectionString = $connectionString
$json = $definition | ConvertTo-Json -Depth 20
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($definitionPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Updated Power BI semantic model connection:"
Write-Host "  Workspace:      $WorkspaceNameOrId"
Write-Host "  Semantic model: $SemanticModelName"
Write-Host "  Semantic ID:    $SemanticModelId"
Write-Host "  File:           $definitionPath"

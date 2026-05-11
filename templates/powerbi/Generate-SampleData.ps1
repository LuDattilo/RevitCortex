#requires -version 5.1
<#
.SYNOPSIS
    Generates sample CSV data in OneDrive\RevitCortex\TestProject\ to simulate
    a complete Revit -> push_to_powerbi -> Power BI workflow without needing
    Revit running.

.DESCRIPTION
    Creates 3 CSV files mimicking real exports from RevitCortex:
      - elements_walls.csv  (walls dataset with ElementId, Mark, Volume, etc.)
      - elements_floors.csv (floors dataset)
      - elements_doors.csv  (doors dataset)
    Plus the standard last_refresh.json sidecar.

    Use this to validate your Power BI pipeline (Get Data, Power Query, visuals,
    drillthrough) before pointing to real exports.

.EXAMPLE
    .\Generate-SampleData.ps1
    .\Generate-SampleData.ps1 -OutputPath "C:\Custom\Folder"
#>
param(
    [string]$OutputPath = $null
)

$ErrorActionPreference = 'Stop'

# Force invariant culture so numbers are written with '.' as decimal separator,
# which is what Power BI's Csv.Document expects for clean Number.FromText parsing.
$inv = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentCulture = $inv
[System.Threading.Thread]::CurrentThread.CurrentUICulture = $inv

if (-not $OutputPath) {
    $candidates = @(
        (Join-Path $env:USERPROFILE "OneDrive - GPA Ingegneria Srl"),
        (Join-Path $env:USERPROFILE "OneDrive - GPA Partners"),
        (Join-Path $env:USERPROFILE "OneDrive")
    )
    $base = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $base) { $base = $env:USERPROFILE }
    $OutputPath = Join-Path $base "RevitCortex\TestProject"
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
Write-Host "Output folder: $OutputPath" -ForegroundColor Cyan

# ── Walls ───────────────────────────────────────────────────────────────
$walls = @()
$wallTypes = @("Generic - 200mm","Generic - 300mm","Exterior - Insulation on Masonry","Interior - 90mm Partition","Foundation - 12 Concrete")
$levels = @("Level 1","Level 2","Roof","Foundation")
$phases = @("New Construction","Existing","Demolition")

for ($i = 1; $i -le 60; $i++) {
    $type = $wallTypes | Get-Random
    $vol = [math]::Round((Get-Random -Min 200 -Max 5000) / 100.0, 3)
    $area = [math]::Round($vol * 5, 2)
    $walls += [PSCustomObject]@{
        ElementId = 600000 + $i
        Category = "Walls"
        Family = "Basic Wall"
        Type = $type
        Mark = "M{0:D3}" -f $i
        Comments = if ((Get-Random -Min 0 -Max 4) -eq 0) { "Verifica strutturale" } else { "" }
        Volume = $vol
        Area = $area
        Level = $levels | Get-Random
        'Phase Created' = $phases | Get-Random
        'WBS_Code' = "C.{0:D2}.{1:D3}" -f (Get-Random -Min 1 -Max 6), (Get-Random -Min 1 -Max 99)
    }
}
$walls | Export-Csv -Path (Join-Path $OutputPath "elements_walls.csv") -NoTypeInformation -Encoding UTF8
Write-Host "  walls:  $($walls.Count) rows" -ForegroundColor Green

# ── Floors ──────────────────────────────────────────────────────────────
$floors = @()
$floorTypes = @("Generic - 250mm","Generic - 200mm","Wood Joist 10","Concrete on Metal Deck")

for ($i = 1; $i -le 25; $i++) {
    $type = $floorTypes | Get-Random
    $vol = [math]::Round((Get-Random -Min 800 -Max 8000) / 100.0, 3)
    $area = [math]::Round($vol * 4, 2)
    $floors += [PSCustomObject]@{
        ElementId = 610000 + $i
        Category = "Floors"
        Family = "Floor"
        Type = $type
        Mark = "F{0:D3}" -f $i
        Comments = ""
        Volume = $vol
        Area = $area
        Level = $levels | Get-Random
        'Phase Created' = "New Construction"
        'WBS_Code' = "P.{0:D2}.{1:D3}" -f (Get-Random -Min 1 -Max 4), (Get-Random -Min 1 -Max 50)
    }
}
$floors | Export-Csv -Path (Join-Path $OutputPath "elements_floors.csv") -NoTypeInformation -Encoding UTF8
Write-Host "  floors: $($floors.Count) rows" -ForegroundColor Green

# ── Doors ───────────────────────────────────────────────────────────────
$doors = @()
$doorTypes = @("Single-Flush 0915 x 2134mm","Single-Flush 0815 x 2134mm","Double-Glass 1830 x 2134mm","Sliding 0915 x 2134mm")
$rooms = @("Office","Conference","Bathroom","Kitchen","Storage","Reception","Corridor")

for ($i = 1; $i -le 40; $i++) {
    $doors += [PSCustomObject]@{
        ElementId = 620000 + $i
        Category = "Doors"
        Family = "Single-Flush"
        Type = $doorTypes | Get-Random
        Mark = "D{0:D3}" -f $i
        Comments = $rooms | Get-Random
        Volume = ""  # doors typically don't expose Volume
        Area = [math]::Round((Get-Random -Min 150 -Max 400) / 100.0, 2)
        Level = $levels | Get-Random
        'Phase Created' = "New Construction"
        'WBS_Code' = "I.{0:D2}.{1:D3}" -f (Get-Random -Min 1 -Max 3), (Get-Random -Min 1 -Max 80)
    }
}
$doors | Export-Csv -Path (Join-Path $OutputPath "elements_doors.csv") -NoTypeInformation -Encoding UTF8
Write-Host "  doors:  $($doors.Count) rows" -ForegroundColor Green

# ── Refresh sidecar ─────────────────────────────────────────────────────
$meta = [PSCustomObject]@{
    refreshed_at = (Get-Date).ToUniversalTime().ToString('o')
    document = "TestProject.rvt"
    element_count = $walls.Count + $floors.Count + $doors.Count
    categories = "OST_Walls, OST_Floors, OST_Doors"
    file = "elements_*.csv"
    mode = "discovery"
}
$meta | ConvertTo-Json | Out-File -FilePath (Join-Path $OutputPath "last_refresh.json") -Encoding UTF8

Write-Host ""
Write-Host "Total: $($walls.Count + $floors.Count + $doors.Count) rows in 3 CSV files" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "  1. Open Power BI Desktop" -ForegroundColor Yellow
Write-Host "  2. Home -> Get Data -> Folder -> $OutputPath" -ForegroundColor Yellow
Write-Host "  3. Or paste RevitCortex-PowerQuery.pq into the Advanced Editor" -ForegroundColor Yellow

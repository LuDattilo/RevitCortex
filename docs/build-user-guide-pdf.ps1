# Genera docs/USER_GUIDE.pdf da docs/USER_GUIDE.md.
# Pipeline: pandoc (md -> HTML standalone con CSS) -> Chrome headless (HTML -> PDF).
# Requisiti: pandoc nel PATH + Google Chrome installato.
$ErrorActionPreference = 'Stop'
$docs = $PSScriptRoot
$md   = Join-Path $docs 'USER_GUIDE.md'
$pdf  = Join-Path $docs 'USER_GUIDE.pdf'
$css  = Join-Path $env:TEMP 'rc_guide_style.css'
$html = Join-Path $env:TEMP 'rc_guide.html'

@'
@page { size: A4; margin: 18mm 16mm; }
body { font-family: "Segoe UI","Helvetica Neue",Arial,sans-serif; font-size: 10.5pt; line-height: 1.5; color: #1a1a1a; }
h1 { font-size: 24pt; color: #0b5394; border-bottom: 3px solid #0b5394; padding-bottom: 8px; margin-top: 0; }
h2 { font-size: 16pt; color: #0b5394; margin-top: 26px; border-bottom: 1px solid #c9d6e5; padding-bottom: 4px; }
h3 { font-size: 13pt; color: #134f8a; margin-top: 22px; padding-top: 4px; }
h4 { font-size: 11pt; color: #2c3e50; margin-top: 16px; page-break-after: avoid; }
h2,h3,h4 { page-break-after: avoid; }
table { border-collapse: collapse; width: 100%; margin: 10px 0 16px; font-size: 9.3pt; }
th { background: #0b5394; color: #fff; text-align: left; padding: 6px 8px; border: 1px solid #0b5394; font-weight: 600; }
td { padding: 5px 8px; border: 1px solid #cdd7e2; vertical-align: top; }
tr:nth-child(even) td { background: #f4f7fb; }
tr { page-break-inside: avoid; }
code { font-family: "Cascadia Code","Consolas",monospace; background: #eef1f5; color: #134f8a; padding: 1px 5px; border-radius: 3px; font-size: 9pt; white-space: nowrap; }
pre { background: #f4f7fb; border: 1px solid #cdd7e2; border-radius: 5px; padding: 10px 12px; overflow-x: auto; font-size: 8.8pt; page-break-inside: avoid; }
pre code { background: none; color: #1a1a1a; padding: 0; white-space: pre; }
blockquote { border-left: 4px solid #0b5394; background: #f0f5fb; margin: 12px 0; padding: 8px 14px; color: #333; }
a { color: #0b5394; text-decoration: none; }
hr { border: none; border-top: 1px solid #d6dde6; margin: 18px 0; }
ul,ol { margin: 8px 0; padding-left: 22px; }
strong { color: #0b1f33; }
'@ | Set-Content -Path $css -Encoding UTF8

Write-Host "pandoc: md -> HTML..."
# NB: niente --metadata title: il markdown ha gia' il proprio '# RevitCortex - Guida Utente'
# come primo h1; passarlo come metadata genererebbe un h1.title duplicato in prima pagina.
# --metadata title vuoto sopprime l'avviso pandoc sul titolo mancante.
pandoc $md -f gfm -t html5 -s --embed-resources --css $css --metadata title="" -o $html
if ($LASTEXITCODE -ne 0) { throw "pandoc fallito" }

$chrome = @(
  "C:\Program Files\Google\Chrome\Application\chrome.exe",
  "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw "Google Chrome non trovato" }

Write-Host "Chrome: HTML -> PDF..."
& $chrome --headless --disable-gpu --no-pdf-header-footer --print-to-pdf="$pdf" "file:///$($html -replace '\\','/')" 2>$null
Start-Sleep -Seconds 2
if (Test-Path $pdf) {
  Write-Host ("OK -> {0} ({1:N0} KB)" -f $pdf, ((Get-Item $pdf).Length/1KB))
} else { throw "PDF non creato" }

# RevitCortex → Power BI starter pack

Tutto quello che serve per costruire un report Power BI agganciato all'output
del tool `push_to_powerbi` (cartella OneDrive monitorata) in meno di 5 minuti.

## File inclusi

| File | Cosa fa |
|---|---|
| `Generate-SampleData.ps1` | Genera dataset di prova nella tua cartella OneDrive (3 categorie con dati realistici) |
| `RevitCortex-PowerQuery.pq` | Script M Power Query già configurato con drillthrough URL |
| `RevitCortex-DAX-Measures.txt` | Misure DAX per le metriche più comuni (count, somma volumi, %) |
| `Build-Report-Steps.md` | Istruzioni passo-passo per costruire il report PBI |

## Quick start

1. Apri PowerShell, esegui `Generate-SampleData.ps1` per popolare la cartella `OneDrive\RevitCortex\TestProject\`
2. Apri Power BI Desktop → Home → Trasforma dati → Editor avanzato
3. Incolla il contenuto di `RevitCortex-PowerQuery.pq` → cambia il path → Fine
4. Pubblica sul workspace Premium GPA per il refresh automatico

Vedi `Build-Report-Steps.md` per la procedura completa.

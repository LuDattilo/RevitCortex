# Costruire il report Power BI di test in 5 minuti

Procedura completa per validare end-to-end il flusso `RevitCortex → CSV → Power BI`
senza dipendere da un modello Revit reale.

## 1. Genera dati di prova (1 min)

```powershell
cd C:\percorso\al\repo\templates\powerbi
.\Generate-SampleData.ps1
```

Output atteso:
```
Output folder: C:\Users\<tu>\OneDrive - GPA Ingegneria Srl\RevitCortex\TestProject
  walls:  60 rows
  floors: 25 rows
  doors:  40 rows
Total: 125 rows in 3 CSV files
```

## 2. Connetti Power BI Desktop (2 min)

1. Apri **Power BI Desktop**
2. **Home → Trasforma dati → Editor avanzato**
3. Apri `RevitCortex-PowerQuery.pq` con un editor di testo, copia tutto il contenuto
4. Incollalo nell'Editor avanzato di PBI
5. **Modifica `FolderPath`** sostituendo con il tuo path reale stampato dallo script
6. **Fine** → **Chiudi e applica**

A questo punto vedi una tabella `RevitCortex` con 125 righe e queste colonne:
- `ElementId`, `Category`, `Family`, `Type`, `Mark`, `Comments`
- `Volume`, `Area` (testo originale es. "12.34 m³")
- `Volume_num`, `Area_num` (parsati come numeri, usabili nei calcoli)
- `Level`, `Phase Created`, `WBS_Code`
- `RevitCortex Drillthrough` (URL `revitcortex://select?ids=...`)

## 3. Aggiungi le misure DAX (1 min)

1. Visualizza riquadro **Modello** a destra
2. Click destro sulla tabella `RevitCortex` → **Nuova misura**
3. Apri `RevitCortex-DAX-Measures.txt`
4. Copia ogni misura (separata da righe vuote) e incolla una alla volta nella formula bar

Avrai disponibili: `Total Elements`, `Walls Count`, `Floors Count`, `Doors Count`,
`Total Volume m3`, `Total Area m2`, `% Walls with Mark`, `Last Refresh`, ecc.

## 4. Costruisci 3 visual base (1 min)

Sulla pagina vuota:

| Visual | Tipo | Campo |
|---|---|---|
| **Card** "Total Volume" | Card | Misura `Total Volume m3` |
| **Bar Chart** per categoria | Istogramma a barre | Asse: `Category`, Valori: `Total Volume m3` |
| **Tabella drillthrough** | Tabella | `ElementId`, `Mark`, `Type`, `Volume_num`, `RevitCortex Drillthrough` |

Per la tabella drillthrough:
- Click sull'icona della colonna `RevitCortex Drillthrough` → **Format → URL Icon = On** (così si rende cliccabile)

## 5. Test del drillthrough Revit ↔ PBI

1. Apri Revit con un modello aperto (anche uno qualsiasi, basta che `Cortex Switch` sia ON)
2. In PBI clicca l'icona URL di una riga della tabella
3. Browser chiede conferma per aprire `revitcortex://` → permetti
4. Revit torna in primo piano e seleziona/zooma sull'ElementId

**Caveat noto**: con dati di prova generati dallo script PS, gli ElementId
sono inventati (600001, 600002…) e non esistono nel modello Revit aperto.
Il drillthrough farà errore "elemento non trovato" → questo è giusto.
Quando esporterai dati reali con `push_to_powerbi`, gli ElementId saranno
quelli del tuo modello e il drillthrough funzionerà.

## 6. Pubblica per scheduled refresh (opzionale)

1. PBI Desktop → **File → Pubblica → Workspace Premium GPA**
2. Su PBI Service → workspace → tre puntini sul dataset → **Pianifica aggiornamento**
3. Configura fino a 48 refresh/giorno (Premium)
4. Per il refresh da OneDrive serve un **Personal Gateway** o connessione SharePoint
   (per la cartella OneDrive aziendale dovrebbe già funzionare via Microsoft 365)

## 7. Sostituisci con dati reali

Una volta validato il flusso:
1. Apri Revit con un modello reale
2. Wizard "Power BI Export" → seleziona categorie/parametri reali → Esporta
3. PBI carica il dataset reale dalla stessa cartella
4. Tutte le misure DAX e i visual continuano a funzionare se i nomi colonna
   corrispondono (ElementId, Category, Family, Type, Mark, Comments, Volume, Area, Level)

## Pulizia

Per rifare il test da zero:
```powershell
Remove-Item -Path "C:\Users\<tu>\OneDrive - GPA Ingegneria Srl\RevitCortex\TestProject" -Recurse -Force
.\Generate-SampleData.ps1
```

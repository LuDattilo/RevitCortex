# Revit MCP — Piano IFC per Claude Code

## Obiettivo
Implementare il supporto IFC in due step separati:

1. **Gestione file IFC dentro Revit**.[cite:14][cite:31]
2. **Creazione/ricostruzione di elementi Revit da file IFC**.[cite:18][cite:20]

---

## Step 1 — Gestione file IFC

### Scopo
Gestire un IFC come file di riferimento, input o output: link, open/import, export, configurazioni e diagnostica.[cite:36][cite:7]

### Base tecnica
- `IFCImportOptions` per link/import.[cite:14]
- `IFCExportOptions` per export.[cite:31]
- Layer `revit-ifc` per setup avanzati e compatibilità con la UI di Revit.[cite:7][cite:32]

### Moduli
- **Capabilities**: versioni, opzioni disponibili, presenza addin IFC.[cite:14][cite:31]
- **Import/Link**: apertura IFC, import IFC, collegamento IFC.[cite:14][cite:36]
- **Export**: export base e con opzioni avanzate.[cite:31][cite:37]
- **Configurations**: lettura/applicazione setup built-in e custom.[cite:7][cite:32]
- **Diagnostics**: posizione, orientamento, warning, effective options.[cite:1][cite:7]

### Tool MCP
- `ifc.get_capabilities`
- `ifc.link`
- `ifc.reload_link`
- `ifc.open_or_import`
- `ifc.export_basic`
- `ifc.export_with_configuration`
- `ifc.list_export_configurations`
- `ifc.get_export_configuration`
- `ifc.set_family_mapping_file`
- `ifc.validate_request`

### Priorità implementativa
1. Capabilities.[cite:14][cite:31]
2. Link IFC minimo con posizione/orientamento.[cite:1][cite:14]
3. Open/import IFC.[cite:14][cite:36]
4. Export base con proprietà tipizzate.[cite:31]
5. Extra options / configuration layer da `revit-ifc`.[cite:7][cite:32]
6. Diagnostics completo.[cite:1][cite:7]

### Note implementative
- Supportare almeno `Action`, `Intent`, `CreateLinkInstanceOnly`, `ForceImport`, `LinkOrientation`, `LinkPosition`, `RevitLinkFileName`.[cite:14]
- Supportare almeno `FileVersion`, `FilterViewId`, `ExportBaseQuantities`, `FamilyMappingFile`, `SpaceBoundaryLevel`, `WallAndColumnSplitting`.[cite:31]
- Prevedere un option bag key/value per le opzioni extra di export/import.[cite:7][cite:14]

---

## Step 2 — Creazione elementi da IFC

### Scopo
Ricostruire elementi Revit nativi da un IFC quando il file deve diventare base progettuale e non solo riferimento di coordinamento.[cite:20][cite:36]

### Principio
Non promettere conversione completa in nativo. Implementare invece:
- analisi di ricostruibilità,
- ricostruzione per categorie compatibili,
- fallback espliciti per geometrie non parametriche o complesse.[cite:20][cite:18]

### Strategia consigliata
Usare una pipeline a due fasi:
1. **Import ponte** dell’IFC in Revit tramite motore esistente.[cite:18][cite:36]
2. **Rebuild nativo** solo sugli oggetti con geometria e classificazione affidabili.[cite:20]

### Moduli
- **Analysis**: legge entità, geometria, livelli, host, materiali, aperture.[cite:20]
- **Candidate extraction**: filtra ciò che è davvero ricostruibile.[cite:20]
- **Category rebuilders**: ricostruzione per categoria Revit.[cite:20]
- **QA/Comparison**: confronto tra sorgente e ricostruito.[cite:20]

### Tool MCP
- `ifc.analyze_rebuildability`
- `ifc.list_rebuild_candidates`
- `ifc.rebuild_walls`
- `ifc.rebuild_floors`
- `ifc.rebuild_roofs`
- `ifc.rebuild_structural_members`
- `ifc.rebuild_openings`
- `ifc.rebuild_family_instances`
- `ifc.compare_original_vs_rebuilt`
- `ifc.tag_unreconstructable_elements`

### Categorie prioritarie
- Walls.[cite:20]
- Floors / Slabs.[cite:20]
- Roofs.[cite:20]
- Columns.[cite:20]
- Beams.[cite:20]
- Openings.[cite:20]
- Doors / Windows dopo la stabilizzazione degli host.[cite:20]

### Categorie da posticipare
- Curtain systems complessi.[cite:20]
- Scale / rampe articolate.[cite:20]
- MEP dettagliato.[cite:20]
- B-rep / freeform avanzati.[cite:20]
- Mesh / triangulated geometry.[cite:20]

### Ordine implementativo
1. Import ponte.[cite:18][cite:36]
2. Analyze rebuildability.[cite:20]
3. Rebuild walls.[cite:20]
4. Rebuild floors.[cite:20]
5. Rebuild columns/beams.[cite:20]
6. Rebuild roofs.[cite:20]
7. Rebuild openings.[cite:20]
8. QA originale vs ricostruito.[cite:20]

### Oggetto dati consigliato
```json
{
  "sourceIfcGuid": "string",
  "ifcEntity": "string",
  "sourceName": "string",
  "suggestedRevitCategory": "string",
  "geometryType": "extrusion|sweep|brep|mesh",
  "hostLevel": "string|null",
  "topLevel": "string|null",
  "profileData": {},
  "thicknessOrSection": {},
  "materialHints": [],
  "openingRelations": [],
  "rebuildStrategy": "string",
  "rebuildConfidence": 0.0,
  "fallbackStrategy": "string",
  "warnings": []
}
```

---

## Raccomandazione architetturale
Separare il codice in due layer:

- **IFC File Management Layer**: link/import/export/configurations/diagnostics.[cite:14][cite:31][cite:32]
- **IFC Native Reconstruction Layer**: analysis + rebuilders + QA.[cite:18][cite:20]

---

## Prompt breve per Claude Code
Implementa un MCP Revit diviso in due step:

1. **IFC file management**: capability discovery, link, open/import, export, configuration compatibility con `revit-ifc`, family mapping e diagnostics.[cite:14][cite:31][cite:7][cite:32]
2. **IFC native reconstruction**: import ponte, analisi di ricostruibilità, ricostruzione per categorie, confronto originale-vs-ricostruito e fallback per geometrie non parametriche.[cite:18][cite:20]

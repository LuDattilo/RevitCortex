# RevitCortex - Capitolo IFC

## 19. IFC (20 strumenti)

RevitCortex include 20 strumenti dedicati all'interoperabilita' IFC, divisi in due gruppi:
- **Gestione file** (10 strumenti): importazione, esportazione, link, validazione
- **Ricostruzione nativa** (10 strumenti): analisi e conversione di elementi IFC in elementi Revit nativi

---

### Gestione File IFC

#### ifc_get_capabilities

Mostra le versioni IFC supportate e verifica se il plug-in revit-ifc e' installato.

**Quando usarlo:**
All'inizio di qualsiasi workflow IFC per sapere cosa il sistema supporta.

**Esempio 1:**
> "Quali versioni IFC posso esportare?"
>
> Versioni supportate: IFC2x2, IFC2x3, IFC2x3CV2, IFC4, IFC4RV, IFC4DTV, IFC4x3. Plug-in revit-ifc: installato (v24.3)

**Esempio 2:**
> "Revit puo' esportare in IFC4x3?"
>
> Si, IFC4x3 e' disponibile (richiede revit-ifc add-in, rilevato nella sessione corrente)

---

#### ifc_validate_request

Valida un file IFC prima di importarlo: verifica che il percorso esista, l'estensione sia corretta (.ifc, .ifczip, .ifcxml), e legge la versione dallo schema nel file.

**Quando usarlo:**
Prima di ogni operazione di import o link IFC, per evitare errori.

**Esempio 1:**
> "Verifica il file C:/Progetti/Strutture.ifc"
>
> File valido: 12.4 MB, schema IFC4, 50,234 righe

**Esempio 2:**
> "Posso importare questo file IFC?"
>
> File non trovato: C:/path/sbagliato.ifc. Verifica il percorso.

---

#### ifc_link

Collega un file IFC nel documento Revit attivo. Crea un file intermedio .ifc.RVT e un RevitLinkInstance.

**Quando usarlo:**
Per aggiungere un modello IFC come collegamento (reference) nel progetto.

**Esempio 1:**
> "Collega il file IFC del progetto strutturale"
>
> Conferma Revit -> Link IFC creato: 'Strutture.ifc', 2,345 elementi importati, file .RVT intermedio creato

**Esempio 2:**
> "Aggiungi il modello MEP come link IFC"
>
> Link IFC 'Impianti.ifc' creato con successo. RevitLinkInstance ID: 890123

> **NOTA:** Il file .RVT intermedio viene creato accanto al file IFC originale. Usa `recreateLink: false` per riutilizzare un file .RVT esistente.

---

#### ifc_reload_link

Ricarica un link IFC esistente, eventualmente da un nuovo percorso file.

**Quando usarlo:**
Quando il file IFC e' stato aggiornato o spostato.

**Esempio 1:**
> "Ricarica il link IFC strutturale"
>
> Link 'Strutture.ifc' ricaricato con successo

**Esempio 2:**
> "Aggiorna il link IFC dal nuovo file C:/Nuovi/Strutture_Rev2.ifc"
>
> Link ricaricato dal nuovo percorso, file .RVT rigenerato

---

#### ifc_open_or_import

Apre o importa un file IFC come nuovo documento Revit. Due modalita':
- **open**: crea un nuovo documento Revit dal file IFC
- **link**: crea un collegamento di riferimento

**Quando usarlo:**
Per convertire un file IFC in un documento Revit nativo.

**Esempio 1:**
> "Apri il file IFC come nuovo progetto Revit"
>
> Documento creato da 'Edificio.ifc': 5,678 elementi importati come DirectShape

**Esempio 2:**
> "Importa il file IFC in modo parametrico"
>
> Import completato con intent 'parametric': elementi importati come tipi Revit modificabili

> **NOTA:** L'opzione `parametric` crea elementi piu' editabili ma l'importazione e' piu' lenta e potrebbe non preservare tutta la geometria.

---

#### ifc_export_basic

Esporta il documento Revit corrente in formato IFC con opzioni base.

**Quando usarlo:**
Per esportazioni rapide con impostazioni standard.

**Esempio 1:**
> "Esporta il modello in IFC4 Reference View"
>
> File esportato: C:/Export/Edificio.ifc (IFC4RV, 3,456 elementi, 15.2 MB)

**Esempio 2:**
> "Esporta in IFC2x3 con quantita' base"
>
> File esportato con BaseQuantities attive, splitting muri per livello attivo

**Parametri principali:**
- `fileVersion`: Default, IFC2x2, IFC2x3, IFC2x3CV2, IFC4, IFC4RV, IFC4DTV, IFC4x3
- `exportBaseQuantities`: include quantita' IFC (volume, area, lunghezza)
- `wallAndColumnSplitting`: divide muri/pilastri multi-livello per piano
- `filterViewId`: esporta solo gli elementi visibili in una vista specifica

---

#### ifc_export_with_configuration

Esporta in IFC usando una configurazione predefinita con possibilita' di override.

**Quando usarlo:**
Per esportazioni conformi a standard specifici (COBie, MVD, ecc.).

**Esempio 1:**
> "Esporta in formato COBie 2.4"
>
> Configurazione 'IFC2x3 COBie 2.4' applicata. File esportato con dati facility management.

**Esempio 2:**
> "Esporta IFC4 Design Transfer View con override per esportare stanze nella vista"
>
> Configurazione applicata con override: ExportRoomsInView=true

**Configurazioni disponibili:**
- IFC4 Reference View
- IFC4 Design Transfer View
- IFC2x3 Coordination View 2.0
- IFC2x3 COBie 2.4
- IFC4x3

---

#### ifc_list_export_configurations

Elenca tutte le configurazioni di esportazione IFC disponibili.

**Quando usarlo:**
Per scoprire quali preset sono disponibili prima di esportare.

**Esempio:**
> "Mostra le configurazioni IFC disponibili"
>
> 5 configurazioni: IFC4 Reference View (IFC4RV), IFC4 Design Transfer View (IFC4DTV), IFC2x3 Coordination View 2.0 (IFC2x3CV2), IFC2x3 COBie 2.4 (IFC2x3), IFC4x3 (IFC4x3)

---

#### ifc_get_export_configuration

Mostra i dettagli completi di una configurazione di esportazione: opzioni, versione, descrizione.

**Quando usarlo:**
Per capire esattamente cosa include una configurazione prima di usarla.

**Esempio:**
> "Cosa include la configurazione COBie?"
>
> IFC2x3 COBie 2.4: versione IFC2x3, ExportBaseQuantities=true, ExportSchedulesAsPsets=true, ExportSpecificSchedules=true, COBieCompanyInfo=true...

---

#### ifc_set_family_mapping_file

Imposta il file di mapping famiglie per le esportazioni IFC. Il file persiste nella sessione.

**Quando usarlo:**
Per personalizzare come le famiglie Revit vengono mappate alle entita' IFC.

**Esempio 1:**
> "Usa il file di mapping personalizzato per l'esportazione"
>
> File mapping impostato: C:/Config/CustomMapping.txt. Le prossime esportazioni useranno questo mapping.

**Esempio 2:**
> "Rimuovi il file di mapping personalizzato"
>
> Mapping personalizzato rimosso dalla sessione. Le esportazioni useranno il mapping predefinito.

---

### Ricostruzione Nativa IFC

Dopo aver importato un file IFC (con `ifc_open_or_import` o `ifc_link`), il documento contiene elementi **DirectShape** -- geometria grezza senza intelligenza Revit. Questi strumenti analizzano i DirectShape e li ricostruiscono come **elementi Revit nativi** (muri, pavimenti, tetti, pilastri, travi).

> **WORKFLOW TIPICO:**
> 1. `ifc_analyze_rebuildability` -- analizza cosa puo' essere ricostruito
> 2. `ifc_list_rebuild_candidates` -- filtra i candidati con confidenza sufficiente
> 3. `ifc_rebuild_walls` / `floors` / `roofs` / `structural_members` -- ricostruisci (prima in dryRun!)
> 4. `ifc_rebuild_openings` -- taglia le aperture nei muri/pavimenti ricostruiti
> 5. `ifc_rebuild_family_instances` -- posiziona porte e finestre
> 6. `ifc_compare_original_vs_rebuilt` -- verifica la qualita'
> 7. `ifc_tag_unreconstructable_elements` -- marca gli elementi non ricostruibili

---

#### ifc_analyze_rebuildability

Scansiona gli elementi DirectShape importati dall'IFC e classifica ciascuno come ricostruibile o meno, con un punteggio di confidenza.

**Quando usarlo:**
Come primo passo dopo un import IFC, per capire cosa puo' diventare nativo.

**Esempio 1:**
> "Analizza quali elementi IFC posso ricostruire come nativi"
>
> 450 elementi analizzati: 280 ricostruibili (62%), 170 non ricostruibili.
> Riepilogo: 120 muri (conf. 0.9), 80 pavimenti (0.85), 45 pilastri (0.85), 35 travi (0.8)

**Esempio 2:**
> "Analizza solo i muri IFC importati"
>
> 120 muri DirectShape analizzati: 95 ricostruibili con Wall.Create (conf. media 0.87), 25 geometria complessa (conf. 0.3)

---

#### ifc_list_rebuild_candidates

Filtra gli elementi che superano una soglia di confidenza per la ricostruzione.

**Quando usarlo:**
Per ottenere la lista degli elementi pronti per la ricostruzione.

**Esempio 1:**
> "Mostra gli elementi IFC con confidenza sopra 0.7"
>
> 180 candidati: 95 muri, 45 pavimenti, 25 pilastri, 15 travi

**Esempio 2:**
> "Quali pavimenti posso ricostruire?"
>
> 45 pavimenti candidati: 38 con confidenza alta (>0.8), 7 con confidenza media (0.5-0.8)

---

#### ifc_rebuild_walls

Ricostruisce muri nativi Revit dai DirectShape IFC. Estrae profilo (linea base + altezza + spessore) e cerca il WallType piu' simile per spessore.

**Quando usarlo:**
Per convertire i muri IFC in muri Revit nativi e modificabili.

**Esempio 1 (anteprima):**
> "Mostrami cosa succederebbe se ricostruissi i muri"
>
> dryRun: 95 muri verrebbero ricostruiti. Tipo 'Basic Wall 200mm' (spessore 200mm), livello Piano Terra, lunghezza media 4.5m

**Esempio 2 (esecuzione):**
> "Ricostruisci i muri IFC come nativi"
>
> 95 muri ricostruiti, 25 saltati (geometria complessa). Tipo assegnato in base allo spessore (tolleranza 50mm).

> **IMPORTANTE:** Usa sempre `dryRun: true` prima per verificare. I muri con geometria non lineare (curvi, inclinati) verranno saltati.

---

#### ifc_rebuild_floors

Ricostruisce pavimenti nativi dai DirectShape IFC. Estrae il profilo della faccia inferiore come CurveLoop.

**Quando usarlo:**
Per convertire le solette/pavimenti IFC in Floor Revit nativi.

**Esempio 1:**
> "Anteprima ricostruzione pavimenti"
>
> dryRun: 45 pavimenti, area totale 2,340 mq, tipo 'Generic Floor 200mm'

**Esempio 2:**
> "Ricostruisci i pavimenti IFC"
>
> 38 pavimenti ricostruiti, 7 saltati (profilo non estraibile)

---

#### ifc_rebuild_roofs

Ricostruisce tetti nativi dai DirectShape IFC usando NewFootPrintRoof.

**Quando usarlo:**
Per convertire i tetti IFC in tetti Revit nativi.

**Esempio:**
> "Ricostruisci i tetti IFC"
>
> dryRun: 8 tetti candidati, tipo 'Generic Roof 300mm', livello 'Copertura'

---

#### ifc_rebuild_structural_members

Ricostruisce pilastri e travi dai DirectShape IFC. Pilastri usano posizionamento a punto, travi usano posizionamento a curva.

**Quando usarlo:**
Per convertire gli elementi strutturali IFC in famiglie strutturali Revit.

**Esempio 1:**
> "Ricostruisci tutti gli elementi strutturali IFC"
>
> dryRun: 25 pilastri (sezione 400x400mm, h=3000mm) + 15 travi (IPE300, luce media 6m)

**Esempio 2:**
> "Ricostruisci solo le travi"
>
> `memberType: "beams"` -- 15 travi ricostruite con profilo HE160A

---

#### ifc_rebuild_openings

Taglia aperture nei muri e pavimenti ricostruiti, basandosi sugli elementi IfcOpeningElement importati.

**Quando usarlo:**
Dopo aver ricostruito muri e pavimenti, per aggiungere le aperture.

**Esempio:**
> "Taglia le aperture nei muri ricostruiti"
>
> dryRun: 45 aperture trovate, 38 con host muro, 7 con host pavimento. Dimensioni: 900x2100mm (porte), 1200x1400mm (finestre)

> **NOTA:** Lo strumento cerca automaticamente il muro/pavimento host tramite sovrapposizione del bounding box.

---

#### ifc_rebuild_family_instances

Posiziona porte, finestre e altri componenti dai DirectShape IFC. Cerca il FamilySymbol piu' adatto e il muro host piu' vicino.

**Quando usarlo:**
Per convertire porte e finestre IFC in istanze di famiglia Revit.

**Esempio:**
> "Ricostruisci porte e finestre dai DirectShape IFC"
>
> dryRun: 45 porte (tipo 'M_Single-Flush 900x2100mm', host muro trovato per 40), 30 finestre (tipo 'M_Fixed 1200x1400mm')

> **NOTA:** Se non trova un muro host entro 600mm, posiziona l'istanza senza host.

---

#### ifc_compare_original_vs_rebuilt

Confronta un elemento DirectShape originale con il corrispondente elemento Revit ricostruito. Calcola differenza di volume, sovrapposizione bounding box (IoU), e punteggio di qualita' 0-100.

**Quando usarlo:**
Per verificare la qualita' della ricostruzione elemento per elemento.

**Esempio:**
> "Confronta il muro originale 12345 con quello ricostruito 67890"
>
> Volume originale: 2.45 m3, ricostruito: 2.41 m3 (-1.6%). Sovrapposizione BB: 94.2%. Qualita': 96.3 (eccellente)

**Scala di qualita':**
- 90-100: Eccellente -- geometria quasi identica
- 70-89: Buono -- piccole differenze accettabili
- 50-69: Discreto -- differenze visibili, verifica manuale consigliata
- 0-49: Scarso -- ricostruzione imprecisa, intervento manuale necessario

---

#### ifc_tag_unreconstructable_elements

Marca gli elementi che non possono essere ricostruiti impostando il parametro Commenti con un valore identificativo.

**Quando usarlo:**
Alla fine del workflow di ricostruzione, per segnalare gli elementi che richiedono intervento manuale.

**Esempio 1:**
> "Marca tutti gli elementi non ricostruibili"
>
> Conferma Revit -> 170 elementi marcati con 'IFC_UNRECONSTRUCTABLE' nel parametro Commenti

**Esempio 2:**
> "Tagga gli elementi falliti con 'DA_VERIFICARE'"
>
> 45 elementi taggati con 'DA_VERIFICARE'. Puoi filtrarli con un abaco su Commenti.

> **SUGGERIMENTO:** Dopo il tagging, crea un abaco filtrato per Commenti = 'IFC_UNRECONSTRUCTABLE' per avere una lista completa degli elementi da verificare manualmente.

---

### Workflow IFC Completo -- Esempio Pratico

Ecco un esempio completo di conversione IFC-to-native in 7 passi:

```
1. "Verifica il file C:/Progetti/Strutture.ifc"
   -> ifc_validate_request: File valido, IFC4, 15MB

2. "Aprilo come nuovo progetto"
   -> ifc_open_or_import: 2,345 DirectShape importati

3. "Analizza cosa posso ricostruire"
   -> ifc_analyze_rebuildability: 1,800 ricostruibili (77%)

4. "Ricostruisci tutto con dryRun"
   -> ifc_rebuild_walls (dryRun): 450 muri
   -> ifc_rebuild_floors (dryRun): 120 pavimenti
   -> ifc_rebuild_structural_members (dryRun): 180 pilastri + 200 travi

5. "Procedi con la ricostruzione reale"
   -> ifc_rebuild_walls: 450 muri creati
   -> ifc_rebuild_floors: 120 pavimenti creati
   -> ifc_rebuild_openings: 280 aperture tagliate
   -> ifc_rebuild_family_instances: 180 porte + 120 finestre

6. "Verifica qualita' su un campione"
   -> ifc_compare_original_vs_rebuilt: Score medio 87 (buono)

7. "Marca gli elementi non ricostruiti"
   -> ifc_tag_unreconstructable_elements: 545 elementi marcati
```

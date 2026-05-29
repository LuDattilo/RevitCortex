# RevitCortex — Catalogo Comandi

> Riferimento completo dei **175 comandi** di RevitCortex, con una breve descrizione e un **esempio di prompt in linguaggio naturale** per ciascuno — il tipo di frase che diresti a Claude/Codex per usarlo.
>
> Non devi conoscere i nomi tecnici dei comandi: scrivi la richiesta in italiano naturale e l'assistente sceglie lo strumento giusto. Gli esempi qui sotto servono a darti un'idea di *cosa* puoi chiedere.

**Versione:** 1.0.28 · **Aggiornato:** 2026-05-29

## Come si usa

Apri Claude Code (o Codex) sul progetto, assicurati che RevitCortex sia avviato in Revit (pulsante **Cortex Switch** sul ribbon), poi scrivi la tua richiesta. Esempi:

- *"Trova tutte le porte del piano L2 senza contrassegno"*
- *"Crea una pianta del piano terra in scala 1:100 e mettila su una tavola A-101"*
- *"Controlla le interferenze tra strutture e impianti"*

Per le operazioni che modificano il modello (creazione, modifica, eliminazione) RevitCortex mostra una **conferma nativa in Revit** prima di agire. In modalità *Auto* (pulsante sul ribbon) le conferme vengono approvate automaticamente per operazioni in serie.

## Indice

**Lettura & analisi**
- [Meta & diagnostica](#meta--diagnostica)
- [Lettura & ricerca elementi](#lettura--ricerca-elementi)

**Modifica & creazione**
- [Modifica elementi](#modifica-elementi)
- [Creazione elementi](#creazione-elementi)
- [Annotazioni & tag](#annotazioni--tag)

**Parametri & materiali**
- [Parametri & shared parameters](#parametri--shared-parameters)
- [Materiali & stratigrafie](#materiali--stratigrafie)

**Viste, fogli, abachi**
- [Viste, fogli, abachi](#viste-fogli-abachi)

**Progetto & manutenzione**
- [Impostazioni di progetto](#impostazioni-di-progetto)
- [Manutenzione & audit modello](#manutenzione--audit-modello)

**Collegamenti & coordinamento**
- [File collegati (link) & coordinamento](#file-collegati-link--coordinamento)

**Dati & interoperabilità**
- [Export & batch](#export--batch)
- [Power BI](#power-bi)
- [IFC (import, export, ricostruzione)](#ifc-import-export-ricostruzione)

**Automazione**
- [Workflow compositi](#workflow-compositi)
- [Script & automazione](#script--automazione)

---

## Meta & diagnostica

#### `say_hello`
Verifica la connessione MCP a RevitCortex mostrando un saluto dentro Revit.
> *"Controlla se RevitCortex è connesso a Revit"*

#### `get_project_info`
Restituisce nome progetto, indirizzo, livelli, fasi, workset e link del documento Revit attivo.
> *"Dammi le informazioni del progetto: livelli, fasi e file collegati"*

#### `set_project_info`
Imposta i campi modificabili delle Informazioni di Progetto; vengono cambiati solo i campi passati, gli altri restano invariati.
> *"Imposta il nome del progetto su 'Edificio Uffici Lotto B' e il numero progetto su 2024-017"*

#### `get_cache_stats`
Restituisce le statistiche diagnostiche di hit/miss della cache dei risultati lato plugin.
> *"Mostrami le statistiche della cache dei tool"*

#### `clear_cache`
Svuota tutte le voci della cache dei risultati lato plugin.
> *"Svuota la cache dei risultati"*

## Lettura & ricerca elementi

#### `get_element_parameters`
Restituisce tutti i parametri di specifici elementi a partire dai loro ElementId Revit.
> *"Dammi tutti i parametri della porta con id 345210"*

#### `get_elements_by_unique_id`
Risolve le stringhe UniqueId di Revit in record ElementId per i flussi cross-app.
> *"Risolvi questi UniqueId in ElementId per allinearli con Navisworks"*

#### `get_selected_elements`
Restituisce gli elementi attualmente selezionati in Revit.
> *"Cosa ho selezionato adesso in Revit?"*

#### `get_current_view_elements`
Elenca gli elementi visibili nella vista attualmente attiva.
> *"Elenca gli elementi visibili nella vista corrente, solo muri e porte"*

#### `get_current_view_info`
Restituisce le informazioni sulla vista attualmente attiva in Revit.
> *"Dimmi su quale vista sono posizionato in questo momento"*

#### `ai_element_filter`
Interroga gli elementi per categoria, classe, tipo di famiglia, bounding box o livello, combinando i filtri in AND/OR (con eventuale inversione NOT); per filtrare sul valore di un parametro usa `filter_by_parameter_value`.
> *"Trova tutti i muri esterni del piano L2 e dammene i primi 10"*

#### `filter_by_parameter_value`
Filtra gli elementi su una condizione di parametro, o più condizioni combinate in AND/OR (equals, contains, greater_than, is_empty, ecc.).
> *"Trova tutte le porte con il parametro Contrassegno vuoto"*

#### `export_elements_data`
Esporta i dati degli elementi per categoria in JSON o CSV, con filtri sui parametri e un filtro opzionale di inclusione basato su parametro.
> *"Esporta in CSV tutti i pilastri con livello e dimensioni"*

#### `get_elements_in_spatial_volume`
Trova gli elementi contenuti in un bounding box 3D o nel volume di un vano (volumeType=room oppure custom).
> *"Trovami tutti gli elementi che ricadono dentro il vano 'Sala riunioni' al piano terra"*

#### `get_room_openings`
Restituisce porte e finestre adiacenti ai vani con le relative dimensioni, filtrabili per roomIds, roomNumbers o levelName.
> *"Dammi quante porte e finestre affacciano su ogni vano del piano primo"*

#### `measure_between_elements`
Misura in mm la distanza tra due elementi o tra due punti (elementId1/elementId2 oppure point1/point2).
> *"Misura la distanza in mm tra il pilastro 882140 e il muro 882200"*

#### `find_untagged_elements`
Trova gli elementi privi di tag in una vista.
> *"Nella vista corrente quali porte non hanno il tag?"*

#### `find_undimensioned_elements`
Trova gli elementi non referenziati da alcuna quota.
> *"Quali muri non sono ancora quotati in questa pianta?"*

#### `audit_families`
Verifica le famiglie del progetto: di default elenca quelle caricabili (.rfa); con includeSystemFamilies=true include anche i tipi di famiglie di sistema (muri, pavimenti, coperture, controsoffitti).
> *"Fammi l'audit delle famiglie di porte, escludendo quelle non utilizzate"*

#### `get_available_family_types`
Elenca i tipi di famiglia disponibili nel progetto.
> *"Elencami i tipi di finestra disponibili nel progetto"*

#### `list_family_sizes`
Elenca le famiglie caricate con il conteggio di tipi/istanze e, con includeSize=true, la dimensione del file famiglia in KB (lento perché esporta ogni famiglia).
> *"Quali famiglie pesano di più nel modello? Mostrami anche la dimensione in KB"*

#### `check_model_health`
Esegue un controllo di salute del modello e restituisce un punteggio.
> *"Fammi un check rapido sullo stato di salute del modello"*

#### `analyze_model_statistics`
Analizza il conteggio degli elementi per categoria nel documento attivo.
> *"Dammi le statistiche del modello: quanti elementi per categoria"*

#### `get_warnings`
Restituisce le segnalazioni (warning) del modello attivo.
> *"Mostrami i primi 10 warning del modello"*

#### `lines_per_view_count`
Conta le linee di dettaglio e/o di modello per vista a fini di audit prestazionale (su modelli grandi usare sempre threshold >= 20).
> *"Quali viste hanno troppe linee di dettaglio? Usa una soglia di 20"*

## Modifica elementi

#### `set_element_parameters`
Imposta valori di parametro su uno o più elementi; per i parametri di lunghezza/area passa una stringa con unità (es. "3000 mm") per la scrittura unit-aware, un null azzera il parametro.
> *"Imposta il commento 'da verificare' sulla porta con id 345210"* oppure *"Metti l'altezza a 3000 mm sul muro selezionato"*

#### `bulk_modify_parameter_values`
Modifica in blocco i valori di un parametro su tutti gli elementi di una categoria (set, trova-e-sostituisci e altre operazioni).
> *"Su tutte le porte imposta il parametro Commenti a 'REV.A'"*

#### `clear_parameter_values`
Azzera i valori di un parametro sugli elementi, per categoria o ambito.
> *"Svuota il parametro Contrassegno su tutte le finestre"*

#### `sync_csv_parameters`
Sincronizza i valori dei parametri da dati CSV verso gli elementi Revit.
> *"Sincronizza i parametri delle porte dal file CSV che ho preparato"*

#### `transfer_parameters`
Copia i valori dei parametri da un elemento sorgente a uno o più elementi di destinazione.
> *"Copia i parametri dal muro 990100 a questi altri tre muri"*

#### `add_prefix_suffix`
Aggiunge un prefisso e/o un suffisso ai valori di un parametro, sull'intero modello o sulla selezione.
> *"Aggiungi il prefisso 'P-' al Contrassegno di tutti i pilastri selezionati"*

#### `match_element_properties`
Copia i valori di parametro da un elemento sorgente verso uno o più elementi di destinazione.
> *"Allinea i parametri di queste porte prendendoli come modello dalla porta 345210"*

#### `modify_element`
Sposta, ruota, specchia o copia elementi con vettori in mm (move/rotate/mirror/copy).
> *"Sposta il pilastro selezionato di 500 mm verso est"*

#### `operate_element`
Seleziona, evidenzia, isola, nasconde, zooma o elimina elementi (select, isolate, hide, temphide, setcolor, ecc.).
> *"Isola nella vista gli elementi con id 882140 e 882200"*

#### `copy_elements`
Copia elementi con offset opzionale in mm, potendo puntare a un'altra vista o a un altro documento aperto.
> *"Copia questi muri nel documento 'Coordinamento_Strutture' aperto in Revit"*

#### `change_element_type`
Cambia il tipo di uno o più elementi assegnando un tipo di destinazione indicato per ID o nome.
> *"Cambia il tipo di queste porte in 'Porta singola 90x210'"*

#### `renumber_elements`
Rinumera vani/porte/finestre per posizione o per nome, scrivendo nel parametro indicato con prefisso/suffisso e start/incremento.
> *"Rinumera le porte del piano terra per posizione partendo da 101"*

#### `set_element_phase`
Assegna agli elementi la fase di creazione e/o di demolizione (array di richieste con phaseCreatedId/phaseDemolishedId).
> *"Imposta la fase di creazione 'Stato di Progetto' sui muri selezionati"*

#### `set_element_workset`
Sposta gli elementi su un workset diverso (array con worksetId o worksetName).
> *"Sposta questi elementi sul workset 'Architettonico'"*

#### `delete_element`
Elimina elementi dal progetto Revit.
> *"Elimina l'elemento con id 882140"*

#### `delete_selection`
Elimina un filtro di selezione salvato indicandone il nome.
> *"Cancella la selezione salvata chiamata 'Muri da verificare'"*

#### `save_selection`
Salva la selezione di elementi come filtro denominato.
> *"Salva gli elementi selezionati come selezione 'Pilastri piano terra'"*

#### `load_selection`
Elenca o carica le selezioni salvate.
> *"Ricarica la selezione salvata 'Pilastri piano terra'"*

#### `section_box_from_selection`
Crea un section box 3D a partire dagli elementi selezionati.
> *"Crea un section box 3D attorno agli elementi che ho selezionato"*

## Creazione elementi

#### `create_floor`
Crea un pavimento architettonico da un contorno (o da un vano), eventualmente con fori.
> *"Crea un pavimento di tipo 'Solaio 30cm' sul perimetro del vano 'Soggiorno'"*

#### `create_surface_based_element`
Crea elementi basati su superficie (pavimenti, controsoffitti) da un contorno di punti su un livello.
> *"Crea un controsoffitto al piano L2 sul contorno che ti passo"*

#### `create_line_based_element`
Crea elementi basati su linea (muri, travi) da una linea di posizione; aggiungi un punto medio per ottenere muri/travi curvi.
> *"Crea un muro perimetrale dal punto (0,0) al punto (5000,0) alto 3 metri al piano terra"*

#### `create_point_based_element`
Crea elementi puntuali (pilastri, arredi) posizionati su coordinate e su un livello, con rotazione opzionale.
> *"Inserisci un pilastro 30x30 nella posizione (2000, 2000) al piano L1"*

#### `create_grid`
Crea un sistema di fili fissi (griglie X e/o Y per numero e passo), oppure rinomina/elimina un filo esistente.
> *"Crea una griglia di 5 fili in X con passo 6 metri e 4 fili in Y con passo 5 metri"*

#### `create_level`
Crea, modifica, rinomina o elimina un livello.
> *"Aggiungi un livello a quota 7 metri chiamato 'Piano Secondo'"*

#### `create_room`
Crea un nuovo vano nel progetto.
> *"Crea un vano al piano terra e chiamalo 'Ufficio 01'"*

#### `create_array`
Crea una serie lineare o radiale; di default costruisce un vero ArrayElement associativo di Revit (conteggio modificabile).
> *"Crea una serie lineare di 6 pilastri con passo 500mm lungo X"*

#### `create_structural_framing_system`
Crea un sistema di travi su un livello sopra un'area rettangolare; di default costruisce un vero BeamSystem associativo di Revit.
> *"Crea un sistema di travi secondarie al piano L1 sull'area rettangolare che ti indico"*

#### `create_filled_region`
Crea una regione riempita in una vista a partire da un contorno chiuso, eventualmente con fori.
> *"Crea una regione riempita tratteggiata nella vista attiva per indicare l'area di demolizione"*

#### `create_text_note`
Crea note di testo in una vista, con allineamento, rotazione e leader opzionali.
> *"Aggiungi una nota di testo 'Verificare in cantiere' vicino alla scala nella vista attiva"*

#### `create_dimensions`
Crea quote nella vista attiva, in modalità da elemento a elemento oppure punto-punto.
> *"Quota la distanza tra i due muri perimetrali nella pianta attiva"*

#### `create_color_legend`
Colora gli elementi per valore di parametro e, opzionalmente, crea una vista legenda.
> *"Colora i vani per destinazione d'uso e creami la legenda dei colori"*

#### `color_elements`
Colora gli elementi di una categoria nella vista attiva raggruppandoli per un valore di parametro, oppure azzera gli override di colore.
> *"Colora i muri della vista attiva in base al tipo"*

#### `load_family`
Carica una famiglia nel progetto Revit.
> *"Carica la famiglia di porte che si trova in C:\Librerie\Porta_REI60.rfa"*

## Annotazioni & tag

#### `tag_rooms`
Tagga i vani nella vista attiva (opera solo sulla vista attiva).
> *"Tagga tutti i vani nella pianta attiva"*

#### `tag_walls`
Tagga i muri al loro punto medio nella vista attiva (tutti i muri di default, o un sottoinsieme).
> *"Tagga tutti i muri nella vista attiva con il leader"*

#### `wipe_empty_tags`
Trova e rimuove i tag vuoti o orfani.
> *"Trova e cancella tutti i tag vuoti nel modello"*

## Parametri & shared parameters

#### `add_shared_parameter`
Aggiunge un parametro condiviso alle categorie di progetto, rispettando il tipo di dato della definizione creata.
> *"Aggiungi il parametro condiviso 'Codice_WBS' di tipo testo alle porte e alle finestre"*

#### `get_shared_parameters`
Elenca tutti i parametri di progetto con i relativi binding e categorie, eventualmente filtrati per categoria.
> *"Elenca i parametri di progetto associati ai muri"*

#### `export_shared_parameter_file`
Esporta il contenuto del file dei parametri condivisi.
> *"Esportami il contenuto del file dei parametri condivisi"*

#### `manage_global_parameters`
Gestisce i parametri globali di progetto (list, get, create, set, delete, rename, set_formula, riordino); a differenza dei parametri di progetto/condivisi, i globali possono essere rinominati.
> *"Crea un parametro globale 'Altezza_Standard' e impostalo a 3000 mm"*

#### `manage_project_parameters`
Gestisce i parametri di progetto (list, create, delete, modify, set_group, set_binding_type, rename); 'modify' agisce sui binding di categoria e 'set_binding_type' commuta tra istanza e tipo.
> *"Crea un parametro di progetto 'Stato_Verifica' di istanza associato a porte e finestre"*

## Materiali & stratigrafie

#### `get_materials`
Elenca tutti i materiali presenti nel documento Revit attivo.
> *"Elencami tutti i materiali del progetto"*

#### `get_material_properties`
Restituisce le proprietà dettagliate di un materiale (fisiche, termiche, aspetto) per ID o nome.
> *"Mostrami le proprietà fisiche e termiche del materiale 'Calcestruzzo C25/30'"*

#### `get_material_quantities`
Calcola area e volume di un materiale sugli elementi, eventualmente filtrando per categoria o limitandosi alla selezione.
> *"Calcolami il volume totale di calcestruzzo nei pilastri"*

#### `create_material`
Crea un nuovo materiale nel progetto Revit.
> *"Crea un nuovo materiale chiamato 'Intonaco bianco'"*

#### `duplicate_material`
Duplica un materiale esistente assegnandogli un nuovo nome.
> *"Duplica il materiale 'Calcestruzzo C25/30' e chiamalo 'Calcestruzzo C32/40'"*

#### `delete_material`
Elimina un materiale dal progetto per ID o nome.
> *"Elimina il materiale 'Intonaco bianco'"*

#### `set_material_properties`
Imposta identità, aspetto, dati prodotto e assegnazione degli asset sui materiali (nome, colore #RRGGBB, trasparenza, classe, produttore, costo, asset di aspetto/struttura/termici, ecc.).
> *"Imposta il colore del materiale 'Intonaco bianco' a #F0F0F0 e la trasparenza a 0"*

#### `get_compound_structure`
Restituisce la stratigrafia di un tipo di muro/pavimento/copertura/controsoffitto per ID o nome del tipo.
> *"Dammi la stratigrafia del tipo di muro 'Muro esterno 30 cm'"*

#### `set_compound_structure`
Modifica la stratigrafia di un tipo di muro/pavimento/copertura/controsoffitto (replace, add, remove, modify, set_wrapping).
> *"Aggiungi uno strato di isolante da 80 mm alla stratigrafia del 'Muro esterno 30 cm'"*

#### `duplicate_family_type`
Duplica un tipo di una famiglia caricabile con un nuovo nome e override opzionali dei parametri.
> *"Duplica il tipo di finestra 'F1 120x150' come 'F1 120x180' impostando l'altezza a 1800 mm"*

## Viste, fogli, abachi

#### `create_view`
Crea una nuova vista: pianta, pianta controsoffitti, sezione, prospetto, vista di disegno o 3D.
> *"Crea una pianta del piano L2 in scala 1:100"*

#### `duplicate_view`
Duplica una vista esistente.
> *"Duplica la pianta del piano terra con dettaglio"*

#### `rename_views`
Rinomina viste in blocco con operazioni di trova/sostituisci, prefisso o suffisso.
> *"Rinomina tutte le viste sostituendo 'Copia di' con niente"*

#### `manage_view_templates`
Elenca, duplica, elimina o rinomina i template di vista.
> *"Duplica il template di vista 'Pianta Architettonica' chiamandolo 'Pianta Strutturale'"*

#### `apply_view_template`
Elenca, applica o rimuove i template di vista dalle viste.
> *"Applica il template 'Pianta Architettonica' a tutte le piante dei piani"*

#### `batch_modify_view_range`
Modifica gli offset dell'area di visualizzazione (alto, piano di taglio, basso, profondità) per più viste.
> *"Imposta il piano di taglio a 1200mm su tutte le piante dei piani"*

#### `create_view_filter`
Crea, applica o elenca filtri di vista basati su parametri, con una o più regole combinate in AND/OR.
> *"Crea un filtro di vista che evidenzi tutti i muri con resistenza al fuoco REI120"*

#### `override_graphics`
Sovrascrive la grafica degli elementi in una vista (colori, trasparenza, mezzatinta, spessore linea).
> *"Metti in mezzatinta tutti gli elementi esistenti nella vista attiva"*

#### `manage_unplaced_views`
Elenca o elimina le viste non collocate su alcun foglio.
> *"Elencami tutte le viste non piazzate su nessuna tavola"*

#### `create_views_from_rooms`
Crea viste di richiamo, sezione o prospetto a partire dai vani, con un pattern di denominazione.
> *"Crea una vista di richiamo per ogni vano del piano terra"*

#### `create_sheet`
Crea una nuova tavola nel progetto.
> *"Crea una tavola A-101 intitolata 'Pianta piano terra'"*

#### `batch_create_sheets`
Crea più tavole con cartiglio ed eventuale collocazione delle viste, da un array JSON.
> *"Crea le tavole da A-101 ad A-105 con il cartiglio standard"*

#### `create_placeholder_sheets`
Crea, elenca, converte o elimina tavole segnaposto.
> *"Crea 10 tavole segnaposto per l'elenco elaborati architettonici"*

#### `duplicate_sheet_with_content`
Duplica una tavola includendo annotazioni e elementi di dettaglio.
> *"Duplica la tavola A-101 mantenendo annotazioni e dettagli"*

#### `duplicate_sheet_with_views`
Duplica una tavola N volte con opzioni configurabili di duplicazione delle viste.
> *"Duplica la tavola A-101 cinque volte duplicando anche le viste collocate"*

#### `place_viewport`
Colloca una vista su una tavola come viewport, con rotazione e tipo di viewport opzionali.
> *"Piazza la pianta del piano terra sulla tavola A-101"*

#### `align_viewports`
Allinea i viewport tra le tavole; 'placement' allinea i centri, 'model' allinea l'angolo minimo dell'inquadratura.
> *"Allinea i viewport delle piante su tutte le tavole in base al modello"*

#### `create_schedule`
Crea una nuova vista abaco nel progetto.
> *"Crea un abaco delle porte con contrassegno, livello e tipo"*

#### `create_preset_schedule`
Crea un abaco da un template predefinito (es. RoomFinish, DoorHardware, WallQuantities, WindowSchedule).
> *"Creami l'abaco delle finiture dei vani usando il modello predefinito"*

#### `modify_schedule`
Modifica campi, ordinamenti, filtri di un abaco, oppure lo rinomina.
> *"Aggiungi il campo 'Area' all'abaco dei vani e ordinalo per livello"*

#### `duplicate_schedule`
Duplica un abaco con un nuovo nome.
> *"Duplica l'abaco delle porte chiamandolo 'Abaco Porte REI'"*

#### `delete_schedule`
Elimina un abaco per ID o nome.
> *"Elimina l'abaco 'Abaco Porte di prova'"*

#### `get_schedule_data`
Esporta i dati di un abaco esistente come JSON.
> *"Estraimi i dati dell'abaco dei vani, massimo 20 righe"*

#### `list_schedulable_fields`
Scopre i campi disponibili e abacabili per una categoria.
> *"Quali campi posso mettere in un abaco delle porte?"*

## Impostazioni di progetto

#### `manage_project_units`
Legge o imposta le unità di progetto (lunghezza, area, volume, angolo, ecc.).
> *"Imposta le unità di lunghezza in millimetri e le aree in metri quadri"*

#### `manage_phase_filters`
Elenca, imposta o crea i filtri di fase di Revit.
> *"Crea un filtro di fase che mostri solo gli elementi nuovi e nasconda gli esistenti"*

#### `get_phases`
Elenca tutte le fasi di progetto del documento attivo.
> *"Quali fasi sono definite in questo modello?"*

#### `get_worksets`
Elenca tutti i workset del documento attivo.
> *"Elencami i workset di questo modello"*

#### `manage_worksets`
Crea, rinomina, elimina o imposta il workset attivo (solo modelli workshared).
> *"Crea un nuovo workset chiamato 'Strutture' e impostalo come attivo"*

#### `manage_additional_settings`
Gestisce le Impostazioni aggiuntive (scheda Gestisci): stili linea, spessori linea, pattern linea, pattern riempimento, mezzatinta/sfondo.
> *"Crea un nuovo stile di linea rosso continuo spesso per le demolizioni"*

#### `create_revision`
Elenca, crea, aggiorna o assegna revisioni alle tavole.
> *"Crea una revisione 'Emissione per appalto' e assegnala a tutte le tavole architettoniche"*

## Manutenzione & audit modello

#### `batch_rename`
Rinomina in blocco elementi o tipi di sistema (sia famiglie caricabili sia tipi di sistema muro/pavimento/controsoffitto/copertura).
> *"Rinomina in blocco tutti i tipi di muro aggiungendo il prefisso 'MR_'"*

#### `rename_families`
Rinomina le famiglie caricate (ed eventualmente i loro tipi) con operazioni di trova/sostituisci, prefisso o suffisso.
> *"Rinomina le famiglie di porte sostituendo 'Porta' con 'PRT'"*

#### `duplicate_system_type`
Duplica, rinomina o elimina un tipo di sistema (muro, pavimento, copertura, controsoffitto).
> *"Duplica il tipo di muro 'Generico 200mm' chiamandolo 'Tamponamento 200mm'"*

#### `purge_unused`
Elimina famiglie/tipi e materiali inutilizzati e, opzionalmente, template di vista e filtri di vista non referenziati.
> *"Fai una pulizia degli elementi inutilizzati, inclusi template e filtri di vista"*

#### `clash_detection`
Rileva le interferenze tra due categorie di elementi, usando di default la vera intersezione di geometria solida.
> *"Controlla le interferenze tra i pilastri strutturali e gli impianti meccanici"*

## File collegati (link) & coordinamento

#### `get_linked_file_instances`
Elenca tutti i file Revit collegati raggruppati per tipo, con trasformate e stato di caricamento.
> *"Quali file Revit sono collegati al modello e qual è il loro stato di caricamento?"*

#### `get_linked_elements`
Interroga gli elementi dei modelli Revit collegati con filtri opzionali; parameterNames è additivo (senza, tornano solo i campi base).
> *"Dammi i pilastri del modello strutturale collegato, max 50, con il loro livello"*

#### `get_selected_linked_elements`
Restituisce le informazioni sulle istanze di link attualmente selezionate.
> *"Quali istanze di link ho selezionato adesso?"*

#### `get_link_transform`
Restituisce la trasformata completa di un'istanza di file collegato.
> *"Dammi la trasformata completa del link strutturale"*

#### `get_coordination_models`
Elenco in sola lettura dei Modelli di Coordinamento Autodesk Revit con metadati di tipo ed eventuali istanze.
> *"Elencami i modelli di coordinamento presenti nel progetto"*

#### `add_linked_file`
Aggiunge un nuovo file Revit collegato da un percorso, posizionando opzionalmente un'istanza in una data posizione.
> *"Collega il file 'Impianti.rvt' e posiziona un'istanza all'origine"*

#### `manage_links`
Elenca, ricarica, ricarica-da-percorso, scarica o rimuove i file collegati (per aggiungere un link nuovo usa `add_linked_file`).
> *"Ricarica tutti i file collegati del progetto"*

#### `reload_linked_file_from`
Ricarica un file Revit collegato da un percorso diverso.
> *"Ricarica il link strutturale dal nuovo percorso sul server di progetto"*

#### `move_link_instance`
Sposta un'istanza di file collegato; mode=delta applica un offset (x,y,z), mode=absolute posiziona l'origine in (x,y,z), valori in mm.
> *"Sposta l'istanza del link impianti di 200 mm verso nord"*

#### `align_link_to_host`
Allinea un'istanza di link all'origine interna del progetto host, alle coordinate condivise o al punto base di progetto.
> *"Allinea il link strutturale alle coordinate condivise del progetto"*

#### `pin_unpin_link_instance`
Blocca o sblocca le istanze dei file collegati.
> *"Blocca tutte le istanze dei link per evitare spostamenti accidentali"*

#### `highlight_linked_element`
Evidenzia un elemento all'interno di un modello collegato, con un eventuale section box.
> *"Evidenzia il pilastro id 12345 dentro il link strutturale, con section box"*

#### `show_cross_model_elements`
Seleziona elementi host più elementi nei modelli Revit collegati, rendendoli visibili tramite marker DirectShape (default) oppure tramite l'isolamento nativo di Revit (usePostCommandIsolate).
> *"Mostrami insieme questi muri host e i pilastri del link strutturale"*

#### `cad_link_cleanup`
Analizza e ripulisce i file CAD importati/collegati (action=list|delete).
> *"Elencami tutti i file CAD importati e collegati nel modello"*

#### `cross_app_selection`
Ponte di selezione simmetrico Revit↔Navis: mode=export emette i CortexElementRefs dalla selezione Revit corrente, mode=import li consuma per selezionare/isolare (priorità di risoluzione: revitUniqueId → ifcGuid → revitElementId).
> *"Esporta la selezione corrente di Revit per passarla a Navisworks"*

## Export & batch

#### `batch_export`
Esporta viste/tavole in formato DWG, DXF, DGN, PDF o immagine (PNG).
> *"Esporta tutte le tavole architettoniche in PDF"*

#### `export_to_excel`
Esporta i dati degli elementi di una categoria Revit in un file Excel.
> *"Esporta tutte le porte in Excel con contrassegno, livello e dimensioni"*

#### `export_room_data`
Esporta i dati dei vani inclusi area, perimetro, livello ed elementi di bordo.
> *"Esportami i dati di tutti i vani: area, perimetro e livello"*

#### `export_schedule`
Esporta un abaco in CSV/TSV o JSON.
> *"Esporta l'abaco dei vani in CSV"*

#### `export_families`
Esporta le famiglie caricate come file .rfa in una cartella di destinazione.
> *"Esporta tutte le famiglie di porte caricate nella cartella C:\Librerie\Porte"*

#### `import_from_excel`
Importa i valori dei parametri da un file Excel negli elementi Revit.
> *"Importa i valori dei parametri dal file Excel che ho compilato"*

#### `import_table`
Importa un file CSV/TSV come tabella formattata in una vista di disegno o legenda.
> *"Importa questo CSV come tabella nella vista di disegno 'Note generali'"*

## Power BI

#### `pbi_check_auth`
Riporta lo stato di login a Power BI; con signIn=true avvia il flusso device-code MSAL dentro Revit (TaskDialog con URL e codice).
> *"Sono autenticato su Power BI? Se no, fammi accedere"*

#### `pbi_list_workspaces`
Elenca (sola lettura) i workspace Power BI accessibili all'utente autenticato.
> *"Elencami i workspace di Power BI a cui ho accesso"*

#### `pbi_list_datasets`
Elenca (sola lettura) i push dataset di un workspace Power BI, utile per trovare un dataset RevitCortex esistente prima di pubblicare.
> *"Quali dataset push ci sono nel workspace 'BIM Coordinamento'?"*

#### `pbi_create_dataset`
Crea un push dataset RevitCortex in un workspace Power BI; è idempotente (se esiste già un dataset con lo stesso nome ne restituisce l'id senza duplicarlo).
> *"Crea il dataset push 'Modello Lotto B' nel workspace 'BIM Coordinamento'"*

#### `pbi_get_binding`
Restituisce il ProjectBinding Power BI salvato per il documento attivo (workspaceId, datasetId, nome, docKey, updatedAt); sola lettura.
> *"A quale workspace e dataset Power BI è collegato questo modello?"*

#### `pbi_publish_elements`
Pubblica gli elementi del modello Revit su un push dataset Power BI (tabella Elements), in modalità replace/append/create; workspaceId e datasetId possono essere omessi se esiste già un ProjectBinding.
> *"Pubblica su Power BI tutte le porte e finestre in modalità replace"*

#### `pbi_publish_schedules`
Pubblica gli abachi Revit nella tabella Schedules di Power BI in formato long (una riga per cella), in modalità replace/append.
> *"Pubblica su Power BI l'abaco delle porte in modalità replace"*

#### `pbi_publish_selection`
Pubblica la selezione corrente di Revit nella tabella Selection di Power BI (una riga per elemento), sostituendo lo snapshot precedente a ogni chiamata.
> *"Pubblica su Power BI gli elementi che ho selezionato adesso"*

#### `pbi_query`
Esegue una query DAX sul dataset Power BI collegato e seleziona in Revit gli elementi corrispondenti (parametri template oppure daxQuery raw; action='isolate' per isolarli).
> *"Da Power BI seleziona in Revit tutte le porte del livello L2"*

#### `pbi_sign_out`
Esegue il logout da Power BI revocando tutti i token MSAL in cache.
> *"Disconnettimi da Power BI"*

#### `push_to_powerbi`
Esporta parametri di elementi o abachi esistenti in file CSV in una cartella locale/OneDrive per il refresh di Power BI (modalità categorie+parameterNames → elements.csv, oppure scheduleIds → un CSV per abaco).
> *"Esporta in CSV per Power BI i pilastri con livello e dimensioni nella cartella OneDrive"*

#### `push_table_to_powerbi`
Scrive una tabella arbitraria (intestazioni + righe) in un CSV nella cartella RevitCortex/OneDrive di Power BI; da usare quando i dati sono stati calcolati da te e non sono un dump diretto di elementi Revit.
> *"Salva in CSV per Power BI questa tabella di riepilogo che hai calcolato dai tre modelli"*

#### `import_from_powerbi`
Legge un CSV Power BI esportato (o modificato a mano) e riscrive i valori dei parametri negli elementi Revit identificandoli tramite la colonna ElementId; di default dryRun=true per l'anteprima.
> *"Reimporta in Revit i parametri dal CSV Power BI che ho modificato, prima in anteprima"*

#### `select_from_powerbi`
Seleziona, evidenzia o isola nella vista Revit attiva gli elementi provenienti da un drillthrough di Power BI (action=select|highlight|isolate).
> *"Isola in Revit gli elementi che arrivano dal drillthrough di Power BI"*

## IFC (import, export, ricostruzione)

#### `ifc_get_capabilities`
Rileva il supporto delle versioni IFC e la presenza dell'add-in revit-ifc.
> *"Verifica se l'export IFC è disponibile e quali versioni supporta"*

#### `ifc_validate_request`
Valida percorso, estensione e versione dello schema di un file IFC.
> *"Controlla se questo file IFC è valido prima di importarlo"*

#### `ifc_list_export_configurations`
Elenca le configurazioni di export integrate disponibili.
> *"Quali configurazioni di export IFC sono disponibili?"*

#### `ifc_get_export_configuration`
Restituisce i dettagli completi di una specifica configurazione di export per nome.
> *"Mostrami i dettagli della configurazione di export 'IFC 2x3 Coordination View'"*

#### `ifc_export_basic`
Esporta il documento attivo in IFC; i flag di primo livello coprono le opzioni comuni.
> *"Esporta il modello in IFC 2x3 con le impostazioni standard"*

#### `ifc_export_with_configuration`
Esporta usando una configurazione con nome (integrata o personalizzata), con override opzionali.
> *"Esporta in IFC usando la configurazione 'Coordination View 2.0'"*

#### `ifc_open_or_import`
Apre o importa un file IFC come progetto Revit nativo.
> *"Importa il file IFC dell'impiantista come modello Revit nativo"*

#### `ifc_link`
Collega un file IFC nel documento attivo (crea un file sidecar .ifc.RVT gestito da Revit).
> *"Collega il file IFC strutturale al modello attivo"*

#### `ifc_reload_link`
Ricarica un collegamento IFC esistente, eventualmente da un nuovo file.
> *"Ricarica il collegamento IFC strutturale dall'ultima versione aggiornata"*

#### `ifc_set_family_mapping_file`
Imposta il file di mappatura famiglie usato dagli export IFC successivi.
> *"Imposta il file di mappatura famiglie per gli export IFC"*

#### `ifc_analyze_rebuildability`
Analizza i DirectShape IFC e valuta la fattibilità di ricostruirli come elementi Revit nativi.
> *"Analizza il modello IFC importato e dimmi quali elementi si possono ricostruire come nativi"*

#### `ifc_list_rebuild_candidates`
Elenca gli elementi al di sopra di una soglia di confidenza di ricostruzione.
> *"Elencami gli elementi IFC con confidenza di ricostruzione superiore al 70%"*

#### `ifc_rebuild_walls`
Ricostruisce muri nativi dai DirectShape IFC (dryRun di default attivo).
> *"Ricostruisci i muri nativi dal modello IFC importato"*

#### `ifc_rebuild_floors`
Ricostruisce pavimenti nativi dai DirectShape IFC (dryRun di default attivo).
> *"Ricostruisci i pavimenti nativi dai DirectShape IFC"*

#### `ifc_rebuild_roofs`
Ricostruisce coperture native dai DirectShape IFC (dryRun di default attivo).
> *"Ricostruisci le coperture native dal modello IFC"*

#### `ifc_rebuild_structural_members`
Ricostruisce pilastri e travi dai DirectShape IFC (dryRun di default attivo).
> *"Ricostruisci pilastri e travi nativi dal modello IFC strutturale"*

#### `ifc_rebuild_family_instances`
Posiziona istanze di famiglia (porte, finestre, arredi) dai DirectShape IFC.
> *"Posiziona le porte e finestre native a partire dai DirectShape IFC"*

#### `ifc_rebuild_openings`
Taglia le aperture nei muri/pavimenti ricostruiti in base ai DirectShape di apertura IFC.
> *"Taglia le aperture nei muri ricostruiti usando le aperture del modello IFC"*

#### `ifc_compare_original_vs_rebuilt`
Confronta volume/geometria tra il DirectShape originale e la sua ricostruzione nativa.
> *"Confronta i volumi tra gli elementi IFC originali e quelli ricostruiti nativi"*

#### `ifc_tag_unreconstructable_elements`
Tagga i DirectShape IFC non ricostruibili scrivendo un parametro marcatore.
> *"Marca tutti gli elementi IFC che non si possono ricostruire come nativi"*

## Workflow compositi

#### `workflow_model_audit`
Esegue un workflow completo di audit del modello.
> *"Fammi un audit completo del modello"*

#### `workflow_clash_review`
Rileva le interferenze tra due categorie e crea una vista 3D con section box per la revisione visiva.
> *"Controlla le interferenze tra strutture e impianti e creami una vista 3D per rivederle"*

#### `workflow_room_documentation`
Genera automaticamente viste di richiamo (ed eventualmente sezioni) per ogni vano di un livello.
> *"Genera automaticamente le viste di richiamo per tutti i vani del piano terra"*

#### `workflow_sheet_set`
Crea automaticamente un set di tavole con cartiglio a partire da un elenco di definizioni.
> *"Crea automaticamente il set di tavole architettoniche dall'elenco che ti passo"*

#### `workflow_data_roundtrip`
Esporta i parametri in Excel per la modifica esterna, poi li reimporta una volta salvato il file.
> *"Esportami i parametri dei vani in Excel, li modifico e poi li reimporti"*

## Script & automazione

#### `send_code_to_revit`
ULTIMA RISORSA — esegue codice C# personalizzato in Revit; gli script vengono salvati in `~/.revitcortex/scripts/` e richiedono conferma esplicita dell'utente prima dell'esecuzione (preferire sempre i tool dedicati).
> *"Scrivi uno script che conta i pilastri per livello"* (richiede conferma esplicita dell'utente prima di eseguire)

---

*Catalogo generato dalle definizioni canoniche dei tool MCP (`tool-schemas.txt` / annotazioni server). Per la guida concettuale all'uso efficiente, vedi [USER_GUIDE.md](USER_GUIDE.md).*

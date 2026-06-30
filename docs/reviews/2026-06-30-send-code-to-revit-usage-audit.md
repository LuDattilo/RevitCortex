# Report audit uso send_code_to_revit

Data: 2026-06-30

Worktree analizzato: `C:\tmp\RevitCortex-automode-1046`

Branch: `codex/automode-window-zorder-v1.0.46`

Versione di riferimento: `v1.0.46`

## Sintesi

Il comportamento segnalato e confermato: `send_code_to_revit` viene esposto come strumento disponibile e l'audit locale mostra usi che potevano essere coperti da tool dedicati.

Nel log locale `~\.revitcortex\audit.jsonl` ci sono 2196 esecuzioni tool totali e 54 chiamate a `send_code_to_revit`, pari a circa il 2.46% delle chiamate. Il dato piu rilevante non e il volume assoluto, ma il tasso di fallimento: 44 chiamate fallite e 10 riuscite.

La causa non sembra essere una singola chiamata errata, ma una combinazione di fattori:

- `EnableCodeExecution` risulta attivo nelle impostazioni locali.
- Il server C# descrive correttamente `send_code_to_revit` come "LAST RESORT ONLY".
- Il tool runtime ha gate di settings, sandbox e conferma.
- Il file compatto `tool-schemas.txt` mostra solo la firma del tool e non il warning "last resort".
- Il wrapper TypeScript legacy in `v1.0.46` ha una descrizione troppo permissiva: "Execute custom C# code in the Revit context".
- In `v1.0.46` la conferma critica per script passa ancora dal normale callback di conferma, quindi non e separata in modo netto dal dialogo distruttivo standard.

## Dati audit

Origine: `~\.revitcortex\audit.jsonl`

Periodo osservato per `send_code_to_revit`:

- Prima chiamata rilevata: 2026-04-15 08:21:06
- Ultima chiamata rilevata: 2026-06-04 15:59:57
- Totale chiamate: 54
- Risultati `ok`: 10
- Risultati `fail`: 44

Breakdown errori:

| Esito | Conteggio |
|---|---:|
| ok | 10 |
| fail | 44 |

| Error code | Conteggio |
|---|---:|
| Unknown | 26 |
| PermissionDenied | 13 |
| InvalidInput | 5 |
| null, cioe ok | 10 |

Script name piu ricorrenti:

| Script name | Conteggio | Nota |
|---|---:|---|
| `<none>` | 30 | Chiamate senza nome script, tracciabilita bassa |
| `wall-count` | 3 | Candidato per tool nativi di statistiche/filter |
| `check-rebar-types` | 2 | Candidato per tool nativi rebar |
| `test-connection` | 2 | Uso diagnostico plausibile |
| `hello-test` | 2 | Uso diagnostico plausibile |

## Esempi di uso non ottimale

Alcune chiamate recenti indicano casi in cui era preferibile partire dai tool dedicati:

| Caso audit | Perche non ideale | Tool nativi da preferire |
|---|---|---|
| `wall-count` | Conteggio muri via script custom | `analyze_model_statistics`, `ai_element_filter`, `export_elements_data` |
| `check-rebar-types` | Discovery tipi/forme rebar via script | `list_rebar_bar_types`, `list_rebar_shapes`, tool rebar dedicati |
| Script senza `scriptName` | Bassa tracciabilita nel log e negli script persistiti | Tool nativo o script con nome esplicito solo dopo consenso |
| Test ripetuti `hello-*` | Utile per diagnosi, ma non per operazioni modello | `say_hello`, check connessione/server |

## Stato del codice

Punti gia positivi:

- `src\RevitCortex.Server\Tools\ProjectTools.cs:426` descrive il tool C# come "LAST RESORT ONLY" e dice di preferire tool dedicati.
- `src\RevitCortex.Tools\Elements\SendCodeToRevitTool.cs:37-45` blocca il tool quando `EnableCodeExecution` e disattivo.
- `src\RevitCortex.Tools\Elements\SendCodeToRevitTool.cs:59-64` applica il sandbox tramite `CodeSandbox.Validate`.
- `src\RevitCortex.Tools\Elements\SendCodeToRevitTool.cs:66-68` richiede conferma esplicita prima di eseguire uno script.

Punti deboli rilevati:

- `tool-schemas.txt:248` contiene solo `send_code_to_revit(code:string!, transactionMode:string,null, reusable:boolean,null, scriptName:string,null)`, senza avviso "last resort".
- In `v1.0.46`, `server\src\tools\send_code_to_revit.ts` descrive il tool legacy solo come esecuzione custom C#, quindi puo apparire come uno strumento normale a client o prompt che leggono quel wrapper.
- In `v1.0.46`, `CortexSession.RequestConfirmation(... critical: true)` evita AutoMode e ApproveAll prima del dialogo, ma usa ancora `ConfirmAction`, lo stesso callback della conferma standard. In `RevitCortexApp` risulta cablato solo `ConfirmAction`, non una conferma critica separata.

## Impatto

Impatto operativo:

- Maggiore probabilita che l'assistente scelga script C# anche quando un tool nativo sarebbe piu sicuro, tracciabile e stabile.
- Risposte meno affidabili: 44 fallimenti su 54 chiamate indicano che questo percorso e fragile nella pratica.
- Debug piu difficile quando manca `scriptName`.

Impatto sicurezza:

- Il sandbox riduce il rischio, ma non elimina il problema di policy: `send_code_to_revit` deve restare opzione di ultima istanza.
- Se `EnableCodeExecution` resta attivo, il tool rimane disponibile ai client.
- La conferma critica dovrebbe avere un dialogo dedicato senza opzioni di auto-approvazione o "yes to all".

## Raccomandazioni

1. Disabilitare `EnableCodeExecution` quando non serve realmente.

   Impostazione: `~\.revitcortex\settings.json`

   Valore consigliato per uso quotidiano:

   ```json
   "EnableCodeExecution": false
   ```

2. Rigenerare o aggiornare `tool-schemas.txt` in modo che la firma compatta includa almeno un warning sintetico per `send_code_to_revit`.

3. Mantenere la descrizione "LAST RESORT ONLY" anche nel wrapper TypeScript legacy.

4. Separare la conferma critica dalla conferma distruttiva standard:

   - callback dedicato per operazioni critiche;
   - nessun Auto mode;
   - nessun Yes to All;
   - fail closed se il callback critico non e disponibile.

5. Aggiornare le linee guida operative con una tabella "prima prova questi tool" per casi comuni:

   | Obiettivo | Prima scelta |
   |---|---|
   | Conteggi/statistiche modello | `check_model_health`, `analyze_model_statistics` |
   | Filtri elementi | `export_elements_data`, `ai_element_filter` |
   | Parametri | `get_element_parameters`, `set_element_parameters`, `bulk_modify_parameter_values` |
   | Rebar | tool `list_rebar_*`, `create_rebar`, `set_rebar_*` |
   | Diagnostica connessione | `say_hello`, check stato server |

## Patch locale non distribuita

Nel worktree analizzato sono presenti modifiche locali non committate che vanno nella direzione corretta, ma non fanno parte di questo report come modifica applicativa:

- `CortexSession` con `CriticalConfirmAction` separato.
- `RevitCortexApp` che collega `ConfirmationHelper.ConfirmCritical`.
- `ConfirmationHelper` con dialogo critico dedicato.
- Test di sessione aggiornati per assicurare che critical ignori AutoMode e ApproveAll.
- Wrapper TypeScript legacy con descrizione "LAST RESORT ONLY".
- Nota workflow per audit uso improprio di `send_code_to_revit`.

Queste modifiche devono essere considerate proposta di remediation, non stato distribuito, finche non passano build/test e deploy.

## Criteri di accettazione consigliati

- `send_code_to_revit` non viene usato se esiste un tool dedicato ragionevole.
- Ogni uso di `send_code_to_revit` richiede consenso esplicito dell'utente.
- Ogni script ha `scriptName` leggibile.
- `EnableCodeExecution=false` blocca il tool con errore chiaro e senza retry automatici.
- Conferme critiche non possono essere approvate da Auto mode o Yes to All.
- Build obbligatorie prima del rilascio: `Debug R25` e `Debug R24`.
- Test mirati: `CortexSessionConfirmationTests` e test sandbox/security.

## Conclusione

Il problema e reale ma circoscritto: non serve rimuovere `send_code_to_revit`, serve renderlo chiaramente eccezionale in tutti i punti dove il modello o il client possono scoprirlo. La mitigazione immediata piu semplice e disattivare `EnableCodeExecution`; la correzione strutturale e allineare schema compatto, wrapper legacy e conferma critica separata.

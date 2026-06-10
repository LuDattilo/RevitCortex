# RevitCortex -- Analisi di Sicurezza

**Data:** 2026-04-11
**Autore:** Luigi Dattilo
**Versione:** 1.0

---

## Superficie di attacco

RevitCortex e un'applicazione locale, non un servizio cloud. Non espone API su internet, non ha un database remoto, non gestisce autenticazione di utenti. Tutto avviene sul computer del professionista: Claude Desktop comunica con il plugin Revit attraverso un canale locale (stdio), il modello BIM non lascia mai la macchina. Questo riduce drasticamente la superficie di attacco rispetto a un'applicazione web o SaaS.

In termini pratici: non c'e un server da bucare, non ci sono credenziali da rubare su un endpoint pubblico, non ci sono dati che transitano su internet durante l'utilizzo operativo.

## Rischio principale: esecuzione di codice arbitrario

RevitCortex eredita dal progetto originale uno strumento chiamato `send_code_to_revit`, che permette di inviare blocchi di codice C# arbitrario e farli eseguire direttamente dentro Revit. E lo strumento piu potente del sistema ma anche il rischio di sicurezza piu significativo del progetto.

Il problema non e tecnico ma di fiducia: se qualcuno riuscisse a far credere al modello AI di dover eseguire un certo blocco di codice, potrebbe in teoria fargli fare qualsiasi cosa sulla macchina -- leggere file, modificare il modello, accedere al filesystem locale attraverso le API .NET disponibili in C#. Questo tipo di attacco si chiama **prompt injection**: un contenuto malevolo nascosto in un file che l'AI sta analizzando (ad esempio un file IFC o un CSV importato) che contiene istruzioni camuffate da testo normale.

Nel contesto di un professionista BIM che usa il sistema su modelli propri, il rischio e basso in uso normale. Ma e un rischio che deve essere gestito esplicitamente nel codice, non ignorato.

### Contromisure implementate

1. **Sandbox namespace**: Lista di namespace .NET proibiti per `send_code_to_revit` (`System.IO`, `System.Net`, `System.Diagnostics.Process`, `Microsoft.Win32`, `System.Reflection.Emit`), verificata a runtime prima dell'esecuzione del codice
2. **Warning visibile**: Ogni invocazione di `send_code_to_revit` mostra un avviso all'utente
3. **Modalita locked**: `send_code_to_revit` puo essere disabilitato nelle impostazioni per ambienti di produzione

## Gestione dei dati del modello BIM

I modelli Revit contengono informazioni sensibili: dati di progetto, localizzazione di edifici, dati cliente, planimetrie. RevitCortex non trasmette questi dati a server esterni durante l'esecuzione -- tutto rimane locale. Tuttavia quando Claude Desktop elabora una richiesta che include dati del modello (ad esempio "analizza questi parametri"), quei dati transitano verso i server Anthropic per l'elaborazione del linguaggio naturale.

Questo e un aspetto che il titolare del trattamento deve considerare nell'ottica del GDPR (Regolamento UE 2016/679). Non si tratta di dati personali nel senso classico, ma se i modelli contengono dati riferibili a persone fisiche (proprietari, residenti, dati catastali nominativi), la trasmissione verso un servizio cloud di AI va documentata come trattamento. La base giuridica e tipicamente il legittimo interesse professionale o il contratto con il cliente, ma deve essere esplicitata.

Anthropic pubblica una data processing agreement (DPA) e politiche di utilizzo dei dati che specificano come vengono trattati i dati inviati tramite Claude Desktop. E consigliabile leggere queste politiche e, se necessario, valutare l'uso di Claude for Enterprise che offre garanzie piu stringenti sul non utilizzo dei dati per il training.

## Dependency e supply chain

RevitCortex dipende dalla Revit API (Microsoft/Autodesk, affidabile), da librerie .NET standard, e dal protocollo MCP. La catena di dipendenze e corta e controllabile, molto diversa da un progetto Node.js con centinaia di pacchetti npm che ognuno introduce rischi di supply chain. Questo e un vantaggio concreto della scelta C# nativo.

Il rischio residuo e il fork originale: se mcp-servers-for-revit o il fork LuDattilo/revit-mcp-server introducesse codice malevolo in un aggiornamento, e RevitCortex lo usasse come riferimento senza verifica, si potrebbe ereditare il problema. La strategia di riscrittura guidata -- leggere il fork come specifica funzionale e non copiarne il codice -- elimina proprio questo rischio.

## Buone pratiche

### Gia presenti nel design

- Transazioni esplicite con rollback automatico su errore (impedisce modifiche parziali e corruzione del modello)
- Error handling tipizzato che non espone stack trace all'utente finale
- Binding socket solo su `IPAddress.Loopback` (127.0.0.1)
- Conferma utente obbligatoria per operazioni distruttive via `RequestConfirmation()`

> **Nota (2026-05-12):** la dichiarazione "Nessun accesso di rete in uscita da parte dei tool" non è più corretta. I tool `PowerBiLive` (`PbiPublish*`, `PbiTriggerRefreshTool`) e `PowerBiServiceClient` effettuano chiamate HTTPS verso `api.powerbi.com` e `login.microsoftonline.com` (auth MSAL). Vedere sezione "Nuova superficie di rete — PBI REST" più sotto.

### Implementate come requisiti di sicurezza

- **Sandbox per `send_code_to_revit`**: lista di namespace .NET proibiti verificata prima dell'esecuzione
- **Audit log locale**: ogni operazione registrata in `~/.revitcortex/audit.jsonl` (tool, elementi, timestamp)
- **Modalita read-only**: flag configurabile che disabilita tutti i tool di scrittura

## Nuova superficie di rete — PBI REST API (2026-05-12)

Con l'integrazione `PowerBiLive` (Pipeline B) e `PbiTriggerRefreshTool` (Opzione C), il plugin effettua chiamate HTTPS in uscita verso:

| Endpoint | Scopo | Attivato da |
|---|---|---|
| `https://login.microsoftonline.com/...` | MSAL OAuth — acquisizione/rinnovo access token | Sign-in utente, silent refresh |
| `https://api.powerbi.com/v1.0/myorg/...` | Push righe, trigger refresh, list datasets/workspaces | Tools `PbiPublish*`, `PbiTriggerRefreshTool` |

### Caratteristiche

- **Solo su azione esplicita utente**: le chiamate partono unicamente quando l'utente preme "Esporta" (con checkbox refresh attivo) o invoca un tool via chat. Non c'è polling o heartbeat.
- **Auth tramite MSAL**: nessuna credenziale è salvata in chiaro. MSAL usa la cache DPAPI (`~/.revitcortex/msal_cache.json`, cifrata per l'utente corrente).
- **Dati trasmessi**: ID di workspace e dataset (GUID, non sensibili), payload righe CSV aggregate (dati del modello BIM). Nessuna credenziale, nessun file completo.
- **AllowExternalWrites flag**: `PowerBiSettings.AllowExternalWrites` deve essere `true` per abilitare le chiamate. Default `false` → tutte le push bloccate finché l'utente non abilita esplicitamente.

### Classificazione rischio

| Area | Livello | Note |
|---|---|---|
| HTTPS verso api.powerbi.com | Basso | Transport cifrato, API ufficiale Microsoft, token OAuth short-lived |
| Dati BIM verso PBI Service | Medio | Stesso tenant M365 dell'utente — dati non escono dall'organizzazione |
| Token MSAL in cache locale | Basso | DPAPI cifra per utente, non leggibile da altri utenti |

### GDPR — aggiornamento

I dati del modello Revit trasmessi via RevitCortex raggiungono ora **tre destinazioni cloud**:
1. **Anthropic** (Claude/LLM): parametri e descrizioni inviati nelle prompt. Vedi DPA Anthropic.
2. **Microsoft OneDrive / SharePoint**: file CSV scritti nella cartella OneDrive locale e sincronizzati in cloud. Soggetto alla DPA Microsoft 365 del tenant GPA.
3. **Microsoft Power BI Service**: righe aggregate inviate via REST API al workspace. Stesso tenant M365 — trattamento interno all'organizzazione.

Per tutte e tre: base giuridica = legittimo interesse professionale / esecuzione contratto con il cliente. Se i modelli contengono dati riferibili a persone fisiche (es. dati catastali nominativi), documentare il flusso nel registro trattamenti GDPR.

---

## PBI Live Phase 2C — listener HTTP locale (porta 27016)

Per consentire al custom visual di Power BI Desktop di guidare la selezione in Revit, il plugin avvia un `HttpListener` sulla porta locale `27016` mentre Cortex Switch è attivo. Caratteristiche:

- **Binding solo localhost**: il prefisso è `http://localhost:27016/`, non `+` o `*` -- il sistema operativo rifiuta connessioni da altri host
- **Nessuna autenticazione**: il listener accetta qualunque POST localhost senza token
- **Operazioni esposte**: selezione e isolamento temporaneo di elementi (entrambi non distruttivi: nessun salvataggio, nessuna modifica al modello)
- **Auto-stop**: il listener si ferma quando l'utente clicca Cortex Switch, quando il documento viene chiuso, o quando Revit termina

### Modello di trust

Il listener si fida di **qualunque processo locale** che possa aprire una connessione TCP a `localhost:27016`. In pratica:

- Altri utenti sulla stessa macchina (sessioni Windows separate) NON possono raggiungerlo (Windows isola il loopback per sessione)
- Browser web aperti dallo stesso utente potrebbero teoricamente fare richieste cross-origin -- mitigato dai CORS preflight (i browser bloccano POST con `Content-Type: application/json` senza preflight, e il listener risponde solo `Access-Control-Allow-Origin: *` su OPTIONS, non echo dell'origin)
- Altri programmi avviati dallo stesso utente possono inviare POST e forzare una selezione Revit

### Impatto

Le operazioni esposte (select, isolate temporary) **non modificano il modello e non scrivono su disco**. L'unico effetto è cambiare cosa l'utente vede o ha selezionato in Revit -- fastidioso ma non distruttivo. Una `Selection.SetElementIds` non può corrompere il file, non può eseguire codice, non può esfiltrare dati.

### Mitigazioni future (non implementate)

Se in futuro venissero esposte operazioni distruttive via HTTP:

1. **Token per-sessione**: generare un token random in `~/.revitcortex/pbi-token.json` (permessi solo utente), il custom visual lo leggerebbe però il PBIVIZ sandbox non permette filesystem access -- alternativa: token passato via query string PBI-side e configurato dall'utente
2. **Allowlist `Host` header**: rifiutare richieste con `Host` diverso da `localhost:27016` (mitiga DNS rebinding)
3. **Rate limiting**: max N richieste/secondo per evitare flooding

### Stato attuale (v1.0.0.10)

Il listener Phase 2C è classificato come **rischio basso**: operazioni non distruttive, binding loopback, auto-stop al cambio documento. Il rischio principale residuo è un'altra app locale che fa selezioni indesiderate (UX, non security).

---

## Validazione percorsi file — PathSafety (2026-06-10)

I tool che accettano percorsi file dal chiamante (MCP) passano attraverso `PathSafety.TryResolveSafe` (`RevitCortex.Tools/Utilities/PathSafety.cs`): il percorso viene canonicalizzato (`Path.GetFullPath`, che collassa `..`) e accettato solo se ricade sotto directory di proprietà dell'utente — Documents, Desktop, Downloads, profilo utente, temp. Percorsi di sistema (`C:\Windows`, `C:\ProgramData`, ...) e traversal vengono rifiutati con `InvalidInput` strutturato.

### Tool coperti

**Policy stretta (solo directory utente, niente UNC):** `import_table`, `workflow_data_roundtrip`, `ifc_validate_request`, `ifc_export_basic`, `ifc_export_with_configuration`, `ifc_set_family_mapping_file`, `ifc_open_or_import`, `export_to_excel`, `export_families`, `import_from_excel`, `batch_export`, `export_shared_parameter_file`.

**Policy link (UNC ammesso, `allowUnc: true`):** `ifc_link`, `ifc_reload_link`, `add_linked_file`, `reload_linked_file_from`.

### Trade-off UNC sui tool di link

Collegare modelli da share di rete (`\\server\share\...`) è un workflow BIM standard (modelli centrali, coordinamento multidisciplinare): bloccare gli UNC su questi quattro tool li renderebbe inutilizzabili in studio. Il rischio residuo è contenuto perché:

1. ogni tool di link è già gated da `RequestConfirmation()` — il dialogo nativo Revit mostra il percorso all'utente prima di procedere;
2. i percorsi locali restano comunque vincolati alle directory utente anche con `allowUnc: true`;
3. l'operazione di scrittura derivata (la cache `.ifc.RVT` creata da `CreateFromIFC`) finisce accanto al file IFC validato, e l'eventuale sovrascrittura della cache è dichiarata nel dialogo di conferma.

I tool di export e import dati restano invece su policy stretta: scrivere o leggere file arbitrari su share di rete non è necessario per quei workflow e amplierebbe la superficie di esfiltrazione/sovrascrittura.

---

## Livello di rischio

| Area | Livello rischio | Stato |
|------|----------------|-------|
| Esposizione rete | Basso | Architettura locale |
| Prompt injection via `send_code_to_revit` | Medio-alto | Mitigato con sandbox |
| Dati BIM verso cloud AI | Medio | Da documentare per GDPR |
| Supply chain dipendenze | Basso | Strategia riscrittura guidata |
| Corruzione modello per errore | Basso | Transazioni con rollback |
| Audit trail operazioni | Basso | Implementato audit log |
| PBI listener localhost (Phase 2C) | Basso | Loopback + operazioni non distruttive + auto-stop |
| PBI REST API in uscita (Pipeline B + Opzione C) | Basso | HTTPS, stesso tenant M365, dati non escono dall'org |
| Token MSAL cache locale | Basso | DPAPI cifra per utente Windows corrente |
| Percorsi file arbitrari dai tool | Basso | PathSafety su tutti i tool con path caller-supplied; UNC solo sui tool di link con conferma utente |

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
- Nessun accesso di rete in uscita da parte dei tool
- Binding socket solo su `IPAddress.Loopback` (127.0.0.1)
- Conferma utente obbligatoria per operazioni distruttive via `RequestConfirmation()`

### Implementate come requisiti di sicurezza

- **Sandbox per `send_code_to_revit`**: lista di namespace .NET proibiti verificata prima dell'esecuzione
- **Audit log locale**: ogni operazione registrata in `~/.revitcortex/audit.jsonl` (tool, elementi, timestamp)
- **Modalita read-only**: flag configurabile che disabilita tutti i tool di scrittura

## Livello di rischio

| Area | Livello rischio | Stato |
|------|----------------|-------|
| Esposizione rete | Basso | Architettura locale |
| Prompt injection via `send_code_to_revit` | Medio-alto | Mitigato con sandbox |
| Dati BIM verso cloud AI | Medio | Da documentare per GDPR |
| Supply chain dipendenze | Basso | Strategia riscrittura guidata |
| Corruzione modello per errore | Basso | Transazioni con rollback |
| Audit trail operazioni | Basso | Implementato audit log |

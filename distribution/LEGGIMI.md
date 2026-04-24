# RevitCortex v1.0.10 — Guida Installazione

Assistente AI per Autodesk Revit (2023–2027). Server MCP in C# + plugin Revit.

## Cosa serve prima

- **Autodesk Revit** (2023, 2024, 2025, 2026 o 2027) installato
- **Claude Desktop** oppure **Claude Code** (almeno uno dei due)
- Permessi amministratore sul PC (solo per l'installazione)

## Installazione — 3 passi

1. **Estrai** `RevitCortex-v1.0.10.zip` in una cartella temporanea (es. `Desktop\RevitCortex-setup`).
2. **Tasto destro su `install.ps1` → Esegui con PowerShell**
   (se non appare l'opzione, apri PowerShell come Amministratore e lancia `powershell -ExecutionPolicy Bypass -File install.ps1`)
3. L'installer chiede:
   - **Windows Defender exclusion** (y/N) → rispondi `N` se non vuoi toccare le impostazioni di sicurezza, `y` solo se l'EXE viene segnalato come minaccia
   - **Client Claude** → `3` per configurare sia Claude Desktop che Claude Code, altrimenti scegli il tuo

4. **Riavvia Revit** e **Claude** (Desktop e/o Code).

## Verifica

- In Revit apri un modello qualsiasi: nel ribbon deve comparire il gruppo **RevitCortex** con icona verde.
- In Claude chiedi: *"quanti muri ci sono nel modello?"* — Claude userà il tool `ai_element_filter` per contare.

## Sicurezza — importante

RevitCortex include un tool avanzato `send_code_to_revit` che permette di **eseguire codice C# arbitrario sul documento Revit attivo**. Per sicurezza è **disabilitato di default**.

Se vuoi abilitarlo (solo se sai cosa stai facendo):
1. Apri il file `%USERPROFILE%\.revitcortex\settings.json` (di solito `C:\Users\<tuoNome>\.revitcortex\settings.json`)
2. Aggiungi la riga: `"EnableCodeExecution": true`
3. Salva. Ogni chiamata sarà tracciata in `audit.jsonl`.

**Se non sai cosa sia, NON abilitarlo.** Tutti gli altri 150+ tool funzionano senza.

## Disinstallazione

Tasto destro su `uninstall.ps1` → **Esegui con PowerShell** (come Amministratore).

## Cartelle toccate dall'installer

| Cosa | Dove |
|---|---|
| Plugin Revit | `C:\ProgramData\Autodesk\Revit\Addins\<versione>\RevitCortex\` |
| Server MCP | `%USERPROFILE%\.revitcortex\server\` |
| Impostazioni utente | `%USERPROFILE%\.revitcortex\settings.json` |
| Log audit | `%USERPROFILE%\.revitcortex\audit.jsonl` |
| Config Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` (aggiornato automaticamente) |
| Config Claude Code | registrato con `claude mcp add revitcortex ...` |

## Problemi comuni

- **Icona RevitCortex non appare in Revit** → plugin non caricato. Controlla `%ProgramData%\Autodesk\Revit\Addins\<ver>\RevitCortex\RevitCortex.addin` esiste.
- **"Connection refused" in Claude** → server MCP non parte. Apri `%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe` manualmente e controlla errori.
- **Port 8080 già in uso** → modifica `settings.json` aggiungendo `"Port": 8081` e riavvia plugin + server.

### MCP scompare dopo un aggiornamento di Claude

Gli aggiornamenti di **Claude Code** o **Claude Desktop** possono cancellare la registrazione del server MCP. Se Claude non trova più i tool RevitCortex, il server EXE è ancora al suo posto — basta ri-registrarlo.

**Claude Code** — apri un terminale e lancia:

```powershell
claude mcp add revitcortex "%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe"
```

Verifica con `claude mcp list`: deve comparire `revitcortex`.

**Claude Desktop** — apri il file `%APPDATA%\Claude\claude_desktop_config.json` con Blocco Note e assicurati che contenga il blocco `mcpServers`:

```json
{
  "mcpServers": {
    "revitcortex": {
      "command": "C:\\Users\\<tuoNome>\\.revitcortex\\server\\RevitCortex.Server.exe",
      "args": []
    }
  }
}
```

Sostituisci `<tuoNome>` con il tuo nome utente Windows. Se il file contiene già altri contenuti (es. `"preferences"`), aggiungi solo il blocco `"mcpServers"` senza toccare il resto.

Riavvia Claude Desktop dopo aver salvato.

## Segnalazione bug

Se incontri un problema, usa il bottone **"Send log to support"** nella tab RevitCortex del ribbon: raccoglie automaticamente log e contesto in uno ZIP, apre una mail precompilata verso il supporto. I report vengono salvati in `%USERPROFILE%\.revitcortex\support-reports\` (ultimi N conservati, configurabile dalle Impostazioni).

Dalle **Impostazioni → Support Reports** puoi:
- impostare quanti report conservare (default 10)
- aprire la cartella dei report
- cancellare tutti i report con un click

Vedi `COME_INVIARE_BUG_REPORT.md` per dettagli.

## Novità v1.0.10

- **Fix critico: plugin non si caricava dopo installazione da ZIP** — i file DLL scaricati da GitHub venivano bloccati da Windows (HRESULT 0x80131515). L'installer ora chiama `Unblock-File` automaticamente su tutti i file copiati.
- **Fix: banner stato server in tempo reale** — la pagina Impostazioni ora riflette immediatamente il cambio di stato del Cortex Switch senza dover riaprire il pannello.

## Novità v1.0.9

- **Governance script `send_code_to_revit`**: ogni script viene salvato in `%USERPROFILE%\.revitcortex\scripts\` con intestazione `// TEMP` o `// REUSABLE`. Gli script TEMP vengono cancellati automaticamente alla chiusura di Revit.
- Gate di consenso esplicito prima di ogni esecuzione script.
- Claude decide autonomamente se uno script è TEMP o REUSABLE in base alla riusabilità.

## Novità v1.0.8

- Fix: `batch_rename` ora supporta esplicitamente i **system floor types** (e altri tipi sistema: wall, ceiling, roof).
- Fix: `override_graphics` restituisce errore chiaro se applicato a un Drawing Sheet.
- Fix: `ifc_export` usa timeout esteso (900 s) per modelli grandi.
- Fix: `set_element_parameters` gestisce correttamente i parametri a livello tipo.

## Novità v1.0.7

- **Download & Install automatico**: il banner aggiornamenti ora scarica il ZIP direttamente dentro Revit e lancia l'installer con UAC, senza aprire il browser. Progresso in tempo reale, pulsante Annulla durante il download, retry in caso di errore.
- Fix: `settings.json` aggiornato con merge (non sovrascrittura) — le impostazioni avanzate vengono ora preservate al salvataggio.
- Fix: propagazione errori JSON-RPC strutturata via campo `data` (§5.1 spec) per diagnostica più precisa.

## Novità v1.0.3

- **Banner aggiornamenti in-app**: al lancio di Revit il plugin controlla se è disponibile una versione più recente. Se sì, compare un banner giallo nella pagina Impostazioni.
- Controllo non invadente: una sola chiamata in background al boot, timeout 5 s, nessun popup, nessun blocco se offline.

## Novità v1.0.2

- **Fix**: il doppio click su "Send log to support" non blocca più Revit (chiamata COM a Outlook su thread dedicato con timeout)
- Report di supporto spostati dal Desktop in `%USERPROFILE%\.revitcortex\support-reports\` con rotazione automatica
- Nuove impostazioni: numero di report da conservare, bottoni "Apri cartella" / "Elimina tutti adesso"
- UI e dialog tradotti automaticamente in base alla lingua di Revit (EN / IT)
- ZIP di supporto include ora anche `usage-mcp.db` (DB token usage post-migrazione SQLite)
- Campo `plugin_version` in `context.txt` per filtraggio automatico dei known-issues

## Novità v1.0.1

- Consent gate obbligatorio per `send_code_to_revit`
- Sandbox anti-reflection (blocca bypass con `Type.GetType`, `Activator`, `MethodInfo.Invoke`)
- Strip intelligente di commenti e stringhe (no più falsi positivi)
- Defender exclusion ora opt-in (non più automatica)
- `transactionMode: "group"` per script complessi
- Stack trace completo negli errori runtime

## Supporto

Luigi Dattilo

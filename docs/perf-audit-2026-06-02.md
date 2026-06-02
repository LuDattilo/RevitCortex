# Report di verifica — Audit prestazioni & bug RevitCortex

**Data:** 2026-06-02
**Branch:** `main` · **HEAD al momento dell'audit:** `8fe3553`
**Obiettivo:** revisione di tutte le funzionalità con focus su bug, ottimizzazioni di velocità di esecuzione dei comandi, attenzione alle regressioni. Modifiche architetturali/funzionali solo previa approvazione.

> Questo documento serve alla revisione da parte di terzi. Ogni affermazione è verificabile con i comandi riportati in fondo. Nessun commit è stato creato: le modifiche sono nel working tree.

---

## 1. Metodo

1. **Scoperta** con 4 agent read-only paralleli su 4 assi: (a) uso di `FilteredElementCollector`/query, (b) transazioni e rigenerazioni, (c) layer di trasporto/dispatch (server, bridge, router, threading), (d) silent-drop e regressioni (mismatch parametri wrapper/plugin, `catch` vuoti).
2. **Verifica manuale sul codice reale** di ogni finding prima di agire. Molti finding degli agent erano falsi positivi e sono stati scartati (vedi §5).
3. **Applicazione solo dei fix verificati e sicuri** (behavior-preserving). I cambiamenti architetturali/funzionali sono stati **solo proposti**, non applicati (vedi §4).
4. **TDD/caratterizzazione** dove c'era rischio di regressione (B1), e build su **tutti e 5 i target** R23–R27.

---

## 2. Modifiche applicate (verificate, behavior-preserving)

### A1 — `AnalyzeModelStatisticsTool`: distribuzione per livello da O(L×N) a O(N)

**File:** `src/RevitCortex.Tools/Project/AnalyzeModelStatisticsTool.cs`

**Problema (verificato):** per *ogni* livello veniva creato un nuovo `FilteredElementCollector` full-document seguito da `.Where(e => e.LevelId == level.Id).Count()` in memoria. Con `L` livelli e `N` elementi → `L` scansioni dell'intero documento + `L×N` confronti. Inoltre ricollezionava elementi già materializzati in `allElements` poche righe sopra.

**Fix:** un singolo passaggio su `allElements` con raggruppamento per `LevelId` in un `Dictionary<ElementId,int>`, poi lookup per livello.

**Equivalenza di output (perché non è una regressione):**
- Il conteggio per livello resta «numero di elementi non-tipo con `LevelId == level.Id`».
- Gli elementi senza livello (`ElementId.InvalidElementId`) erano esclusi prima (non corrispondevano a nessun `level.Id`) e restano esclusi (`continue`).
- L'ordinamento dei livelli per `Elevation` è invariato.
- I campi del risultato (`level`, `elevation = Elevation*304.8`, `count`) sono identici.

### B1 — `CortexRouter.HashParams`: niente più deep-clone dell'input ad ogni chiamata cacheable

**File:** `src/RevitCortex.Plugin/CortexRouter.cs`

**Problema (verificato):** `HashParams` chiamava `Canonicalize`, che ricostruiva un intero albero `JToken` parallelo (nuovi `JObject`/`JArray`) e faceva `DeepClone()` di ogni foglia, **ad ogni chiamata di tool cacheable, inclusi i cache hit** (`HashParams` è invocato prima del `TryGet`). Costo O(n) di allocazione per chiamata, su un percorso caldo eseguito per ogni comando.

**Fix:** emissione diretta del JSON canonico (chiavi ordinate ricorsivamente, senza whitespace) tramite `JsonTextWriter`, senza costruire alberi né clonare foglie.

**Garanzia anti-regressione (cache key drift):** l'hash è la **chiave di cache**. Se l'output cambiasse, ogni voce in cache verrebbe silenziosamente invalidata. Per blindarlo:
- È stato aggiunto `src/RevitCortex.Tests/Router/CortexRouterHashStabilityTests.cs` con **valori golden SHA-256** catturati dall'implementazione *precedente*:
  - empty `{}` → `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a`
  - flat → `2e6c6b7e3652ea85c432e0cfcff6dc1409948f6efe93177553c1899c7d0772a7`
  - nested+array → `3594eb5e55fd2c73754f9d0263e24b7e63b82a65f47bf580d9086e73bc1264c3`
- I test verificano anche: indipendenza dall'ordine delle chiavi, sensibilità all'ordine degli array.
- Dopo il refactor i golden **non cambiano** → output byte-identico → **zero cache drift**.
- I 7 test comportamentali esistenti in `CortexRouterCacheTests` continuano a passare.

---

## 3. Esito build e test (evidenza)

| Verifica | Esito |
|---|---|
| Build `Debug R23` (net48) Plugin | ✅ 0 errori |
| Build `Debug R24` (net48) Plugin | ✅ 0 errori |
| Build `Debug R25` (net8) Plugin + Tools | ✅ 0 errori |
| Build `Debug R26` (net8) Plugin | ✅ 0 errori |
| Build `Debug R27` (net10) Plugin | ✅ 0 errori |
| Test suite `Debug R25` | ✅ **317 passed / 0 failed / 1 skipped** (baseline pre-sessione 313 + i nuovi test di stabilità hash) |
| Test stabilità hash (B1) vs impl pre-refactor | ✅ golden identici (5/5) |
| Test cache `CortexRouterCacheTests` | ✅ 7/7 |

Lo skipped atteso è `ToolExecutionHandlerTests` (richiede `RevitAPIUI.dll`, assente senza Revit installato — comportamento documentato).

---

## 4. Proposto ma NON applicato (richiede decisione esplicita)

Questi punti cambiano comportamento, contratto di output o trade-off di compliance: **non sono stati toccati**, in linea con la direttiva di chiedere prima.

| ID | File | Cosa | Perché serve approvazione |
|---|---|---|---|
| **A2** | `CortexRouter.EstimateResponseBytes` | *Scartato dopo verifica.* Serializza il `CortexResult` completo per l'audit; `SocketService` però serializza un oggetto **diverso** (`JsonRpcResponse` sui soli `result.Data`). Non sono gli stessi byte → riusarne uno cambierebbe il valore `responseBytes` dell'audit. | Sarebbe un cambio di **contratto audit** per un guadagno di microsecondi. Lasciato invariato. |
| **B2** | `AuditLogger.WriteEntry` | `File.AppendAllText` (open+write+close, lock globale) su ogni chiamata di tool. Un writer bufferizzato/async toglierebbe l'I/O dal percorso. | Trade-off **durabilità vs latenza** su un log usato come *source of truth* per accountability ISO 19650. Un writer async può perdere voci al crash. |
| **B3** | `CreateDimensionsTool.cs:220-221` | Le quote punto-punto creano 2 `DetailCurve` (0.01 ft) **mai cancellate** → il modello si riempie di linee di dettaglio. | Bug **funzionale**: la quota referenzia quelle linee, non si possono cancellare senza valutare un'API alternativa. |
| **B4** | ~30 `catch { }` vuoti (es. `ExportElementsDataTool`, `PurgeUnusedTool`, `PushToPowerBiTool`, `WipeEmptyTagsTool`, ...) | Fallimenti per-item ingoiati senza diagnostica; l'utente non distingue «valore vuoto» da «lettura fallita». | Aggiungere `warnings[]`/`skipped` cambia il **contratto di output** di molti tool. Iniziativa ampia, da pianificare separatamente. |

---

## 5. Finding degli agent REFUTATI (falsi positivi)

Riportati per trasparenza: questi *non* sono problemi e non vanno «corretti».

- **«DocumentAnalyzer / LocaleDetector girano ad ogni richiesta o view-activation».** Falso. `OnViewActivated` è protetto da `if (currentDoc != doc)` (`RevitCortexApp.cs:506`): la riscansione full-document avviene **una volta per apertura/cambio documento**, non per comando e non per attivazione vista. Zero impatto sulla latenza dei comandi.
- **«Transaction-per-element nei loop di scrittura».** Non trovato: i tool usano correttamente una sola `Transaction` attorno al batch. Basso rischio di regressione in quell'area.
- **«Mismatch parametri wrapper/plugin (Class A) che droppano input».** Zero mismatch attivi (i 5 storici risultano già corretti).
- **«5 collector separati = bug».** Vari finding del tipo "molteplici collector" sono codice corretto (5 classi di elementi diverse richiedono 5 collector). Scartati.

---

## 6. Come riprodurre la verifica

```powershell
# Diff delle 2 modifiche di sorgente
git diff -- src/RevitCortex.Tools/Project/AnalyzeModelStatisticsTool.cs
git diff -- src/RevitCortex.Plugin/CortexRouter.cs
# Nuovo test (untracked)
git status --short -- src/RevitCortex.Tests/Router/CortexRouterHashStabilityTests.cs

# Build dei 5 target (Plugin)
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj

# Test (targettare il .csproj, non la solution)
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"

# Solo i test di stabilità hash (B1)
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" \
  --filter "FullyQualifiedName~CortexRouterHashStabilityTests"
```

---

## 7. File toccati da questa sessione

| File | Tipo | Note |
|---|---|---|
| `src/RevitCortex.Tools/Project/AnalyzeModelStatisticsTool.cs` | modificato | A1 |
| `src/RevitCortex.Plugin/CortexRouter.cs` | modificato | B1 |
| `src/RevitCortex.Tests/Router/CortexRouterHashStabilityTests.cs` | nuovo | rete di sicurezza B1 |

> Altri file possono comparire come «modified» nel `git status`: sono lavoro pre-esistente non legato a questo audit oppure normalizzazioni di fine riga (CRLF) segnalate da Git. Non sono stati editati in questa sessione. Nessun commit creato.

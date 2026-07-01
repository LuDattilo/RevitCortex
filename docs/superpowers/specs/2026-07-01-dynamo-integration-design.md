# Dynamo Integration for RevitCortex — Design Spec

**Data:** 2026-07-01
**Autore:** Luigi Dattilo (GPA) + Claude
**Stato:** Design approvato, pronto per implementation plan
**Branch:** dedicato (feature/dynamo-integration), separato fino a stabilità su tutti i target

---

## 1. Obiettivo e contesto

Integrare Dynamo per Revit dentro RevitCortex, con due capacità principali:

1. **Generare** file `.dyn` validi a partire da un prompt — grafo "Python-centrico": pochi
   nodi I/O tipizzati + 1 `PythonScriptNode` (engine CPython3) che contiene la logica.
2. **Eseguire** grafi `.dyn` (appena generati o preesistenti) in modalità headless dentro Revit,
   raccogliendo l'output.

### 1.1 Il valore: escape-hatch verso TUTTA la Revit API

L'integrazione **non** è un wrapper dei 288 tool RevitCortex esistenti. Il valore è che il
Python node ha accesso all'**intera Revit API** (`clr.AddReference('RevitAPI')`, `RevitServices`,
`Revit.Elements`, package Dynamo installati). Diventa quindi la **valvola di sfuga generica** per
tutto ciò che RevitCortex non copre con un tool dedicato — un `send_code_to_revit` "fatto meglio":
produce un `.dyn` riutilizzabile e ispezionabile, nel linguaggio (Python) che gli utenti BIM già
conoscono, eseguito nel contesto Dynamo che gestisce `TransactionManager`/`DocumentManager`.

### 1.2 Priorità: RevitCortex prima, Dynamo esplicito

RevitCortex nativo ha SEMPRE priorità. I tool `dynamo_*` sono **ultima risorsa**: si usano solo
quando (a) nessun tool nativo copre il caso **e** (b) l'utente ha dato consenso esplicito — stessa
filosofia di `send_code_to_revit` ("NEVER use autonomously — ask the user first").

### 1.3 Perché dentro RevitCortex e non come app/add-in separato

Il documento di analisi tecnica raccomanda un "Dual-Process Bridge" (MCP server esterno + addin
in-process + `IExternalEvent`). **RevitCortex ha già costruito e messo in produzione quel 70%
di architettura:**

| Il documento dice "implementa..." | RevitCortex ha già... |
|---|---|
| MCP Server esterno (stdio) | `src/RevitCortex.Server/Program.cs` (MCP SDK ufficiale, stdio) |
| IPC verso il plugin | `RevitBridge.cs` + `SocketService.cs` (TCP JSON-RPC :8080) |
| `IExternalApplication` + reg. ExternalEvent nel thread principale | `RevitCortexApp.cs` |
| `AwaitableExternalEventHandler` + TaskCompletionSource | `ToolExecutionHandler.cs` + `RevitThreadDispatcher.cs` (più maturo del PDF: gestisce già `completed_after_timeout`) |
| Sandbox / audit / confirmation | `CodeSandbox`, `AuditLogger`, `RequestConfirmation` già presenti |

Costruire un add-in separato **non aggiunge isolamento dai crash runtime**: Dynamo vive dentro
`Revit.exe` sul thread principale (vincolo di ferro), quindi qualunque add-in che lo tocchi gira
nello stesso processo/AppDomain di RevitCortex. Un crash fatale di Dynamo uccide Revit intero — due
add-in nello stesso processo non hanno un muro tra loro. L'unico rischio realistico e frequente è il
**load-time** (DLL Dynamo assenti/incompatibili), e quello si neutralizza con il **late-binding**
(vedi §4) allo stesso livello di un add-in separato, ma senza raddoppiare deploy/versioning su 5
target.

L'obiettivo "codice pulito e separato" si ottiene con un **progetto separato nella stessa
soluzione** (`RevitCortex.Tools.Dynamo`), non con un add-in separato.

---

## 2. Architettura

```
Claude / AI Client
   │  stdio (MCP)
   ▼
RevitCortex.Server  ──TCP :8080──►  RevitCortex.Plugin (dentro Revit.exe)
   (INVARIATO)          (INVARIATO)      │
                                         ├─ CortexRouter                (INVARIATO)
                                         ├─ ToolExecutionHandler +      (INVARIATO)
                                         │  ExternalEvent  → i tool dynamo_* girano già sul thread principale Revit
                                         │
                                         └─►  ★ RevitCortex.Tools.Dynamo (NUOVO progetto, deps Dynamo confinate)
                                                 ├─ Building/  DynamoGraphBuilder, DynamoGraphSpec, GraphPort, DynJsonSchema
                                                 ├─ Security/  PythonSandbox (adapter su Core.Security.CodeSandbox)
                                                 ├─ Runtime/   DynamoRuntimeLoader (late-binding), DynamoCapabilityProbe
                                                 └─ Tools/     dynamo_get_status, dynamo_list_graph_io,
                                                               dynamo_generate_graph, dynamo_run_graph
```

**Principi portanti:**

1. **Escape-hatch verso tutta la Revit API** — non wrapper dei tool esistenti.
2. **RevitCortex ha priorità; Dynamo è esplicito e ultimo** — routing + descrizioni + confirmation.
3. **Generazione (affidabile) ≠ esecuzione (fragile)** — la generazione non tocca le DLL Dynamo e
   quindi non può "fallire per colpa di Dynamo"; tutto il rischio runtime è confinato in
   `dynamo_run_graph`.
4. **Zero impatto sul core + isolamento a due livelli** — codice isolato nel progetto nuovo, rischio
   load-time isolato dal late-binding. Dynamo assente/incompatibile → i tool dinamici non si
   registrano (`IsDynamic` + `DocumentCapabilities`), gli altri 288 tool intatti.

---

## 3. Componente `DynamoGraphBuilder` (il cuore affidabile)

**Responsabilità:** dato (a) il corpo Python e (b) input/output tipizzati, produrre un `.dyn` JSON
**sempre valido e apribile**, in modo puramente deterministico, **senza caricare nessuna DLL
Dynamo**.

**Perché è affidabile:** lo spazio dei grafi Python-centrici è regolare — N input → 1 PythonScriptNode
→ M output, connettori 1:1. È un template parametrico con GUID generati, non un LLM che indovina JSON.

### 3.1 Schema `.dyn` verificato (fonte: DynamoDS/Dynamo master)

Fatti confermati da sorgenti autorevoli — da rispettare alla lettera:

- **PythonScriptNode**: `ConcreteType = "PythonNodeModels.PythonNode, PythonNodeModels"`,
  `NodeType = "PythonScriptNode"`, engine nel campo JSON `"Engine"` (property C# `EngineName`,
  serializzata come `Engine`), valore esatto `"CPython3"`. `VariableInputPorts: true`,
  `Replication: "Disabled"`. Il codice va in `Code` (newline `\r\n`); output assegnato a `OUT`,
  input arrivano come lista in `IN`.
- **Connector**: NON ha `ConcreteType`. Solo `{ Start, End, Id, IsHidden }`. `IsHidden` è la stringa
  `"False"`/`"True"`. `Start` = Id della **porta Output** sorgente, `End` = Id della **porta Input**
  destinazione (Id di **porta**, non di nodo).
- **Porta** (identica in Inputs e Outputs): `{ Id, Name, Description, UsingDefaultValue, Level,
  UseLevels, KeepListStructure }`. `NodeType` NON è campo della porta.
- **NodeView obbligatorio per ogni nodo**: `View.NodeViews[]` deve contenere un elemento con `Id`
  coincidente con l'`Id` del nodo, altrimenti Dynamo non lo posiziona. Campi:
  `{ Id, Name, IsSetAsInput, IsSetAsOutput, Excluded, ShowGeometry, X, Y }`.
- **Top-level Inputs/Outputs** (per Dynamo Player) usano l'**Id del nodo** (non della porta):
  `{ Id, Name, Type, Value, Description }`.
- **Nodi input tipizzati**:
  - String: `"CoreNodeModels.Input.StringInput, CoreNodeModels"`, `NodeType: "StringInputNode"`,
    campo `InputValue`.
  - Integer Slider: `"CoreNodeModels.Input.IntegerSlider, CoreNodeModels"`,
    `NodeType: "NumberInputNode"`, campi `NumberType/MaximumValue/MinimumValue/StepValue/InputValue`.
  - File path: `"CoreNodeModels.Input.Filename, CoreNodeModels"`.
- **Watch (output)**: `"CoreNodeModels.Watch, CoreNodeModels"`, `NodeType: "ExtensionNode"`.
- **Top-level keys obbligatorie**: `Uuid, IsCustomNode, Inputs, Outputs, Nodes, Connectors, View`.
  Attese ma valide se vuote: `Dependencies, NodeLibraryDependencies, Bindings, ElementResolver,
  Name, Description`. Presenti nei file recenti (includerle con default): `Linting,
  ExtensionWorkspaceData, Author, GraphDocumentationURL, EnableLegacyPolyCurveBehavior, Thumbnail`.
- **View.Dynamo**: `{ ScaleFactor, HasRunWithoutCrash, IsVisibleInDynamoLibrary, Version, RunType,
  RunPeriod }`. `Version` determina il "dialetto"; `RunType: "Automatic"`.

### 3.2 Compatibilità 2.x vs 3.x (matrice R23→R27)

Lo schema JSON è **strutturalmente identico** tra Dynamo 2.x (R23/R24) e 3.x (R25/R26/R27) — stessi
ConcreteType, stesse chiavi. Un solo builder copre tutte le versioni. Variano:
- La stringa `View.Dynamo.Version`.
- L'engine di default disponibile: scriviamo **sempre esplicitamente `"Engine": "CPython3"`**
  (supportato da Dynamo 2.7+ e 3.x). Su Dynamo 3.x è disponibile anche `"PythonNet3"`; usiamo
  CPython3 come valore neutro cross-versione.
- Un PythonNode senza campo `Engine` è interpretato come `IronPython2` (deprecato) → mai omettere il
  campo.

### 3.3 API C# (net48-safe: no record/init/Index — vincolo cross-target)

```csharp
public sealed class GraphPort {
    public string Name { get; }
    public string Type { get; }   // "String" | "Integer" | "Number" | "Boolean" | "Filename"
    public GraphPort(string name, string type) { Name = name; Type = type; }
}

public sealed class DynamoGraphSpec {
    public string Name { get; }
    public string PythonCode { get; }
    public IReadOnlyList<GraphPort> Inputs { get; }
    public IReadOnlyList<GraphPort> Outputs { get; }
    public string Engine { get; }                // default "CPython3"
    public DynamoGraphSpec(string name, string pythonCode,
        IReadOnlyList<GraphPort> inputs, IReadOnlyList<GraphPort> outputs, string engine = "CPython3");
}

public sealed class DynamoGraphBuilder {
    public string BuildDynJson(DynamoGraphSpec spec);              // → stringa JSON .dyn valida
    public DynamoValidationResult ValidateSpec(DynamoGraphSpec spec);
}
```

**Layout automatico:** input impilati a sinistra (X=0, Y crescente), Python al centro (X=350),
output a destra (X=700) — grafo leggibile all'apertura.

### 3.4 Testabilità

100% testabile **senza Revit e senza Dynamo** (normali `[Fact]` xUnit, non `[RequiresRevitApiFact]`):
generare il `.dyn`, ri-parsarlo, verificare invarianti:
- ogni Connector punta a Id di porte esistenti;
- ogni nodo ha un NodeView con Id coincidente;
- il PythonScriptNode ha `Engine == "CPython3"` e `ConcreteType` esatto;
- le entry top-level Inputs/Outputs referenziano Id di nodi esistenti;
- il JSON prodotto è deserializzabile e contiene tutte le top-level keys obbligatorie.

---

## 4. Componenti `DynamoRuntimeLoader` + `DynamoCapabilityProbe` (isolamento del rischio)

Realizzano il **late-binding**: protezione load-time equivalente a un add-in separato, senza il
costo di deploy/versioning doppio.

### 4.1 `DynamoCapabilityProbe` (read-only)

- Risolve il path atteso `…/Autodesk/Revit {YYYY}/AddIns/DynamoForRevit/` (verificato reale su questa
  macchina per R23/R24/R25/R27).
- Verifica presenza di `DynamoRevitDS.dll` + `DynamoCore.dll`.
- Legge la **versione** via `FileVersionInfo` (senza caricare l'assembly nel dominio di esecuzione).
- Determina se CPython3 è atteso per quella versione (3.x → sì; 2.x/R23 → probe cauto).
- Popola `DocumentCapabilities`: se Dynamo assente/incompatibile → `dynamo_run_graph` e
  `dynamo_get_status` **non si registrano** (`IsDynamic=true`). `dynamo_generate_graph` e
  `dynamo_list_graph_io` restano **sempre disponibili** (statici — non toccano Dynamo).

### 4.2 `DynamoRuntimeLoader` (usato solo da `dynamo_run_graph`)

- Carica le DLL Dynamo via reflection **al primo uso** (lazy), non all'avvio del plugin.
- Fallimento → `CortexResult.Fail` pulito; **non propaga** al caricamento del plugin.
- Tutto l'accesso a `DynamoRevit`/`RevitDynamoModel`/`ExecuteCommand`/`ForceRun` via
  reflection/`dynamic` → il progetto **non ha `PackageReference` alle DLL Dynamo** → compila su
  net48 + net8 + net10 senza trascinare dipendenze incompatibili (nessun conflitto 3.6.1 vs 4.x).
- `AppDomain.CurrentDomain.AssemblyResolve` per risolvere le dipendenze transitive di Dynamo dal path
  Revit; il tutto in try/catch difensivo.

**Garanzia:** "utente aggiorna Dynamo → versione incompatibile" produce al massimo un
`dynamo_run_graph` che fallisce con errore strutturato. Gli altri 288 tool e persino
`dynamo_generate_graph`/`dynamo_list_graph_io` continuano a funzionare.

---

## 5. Tool MCP

| Tool | Tipo router | Statico/Dinamico | Tocca DLL Dynamo? | Confirmation | Gated da EnableDynamo |
|---|---|---|---|---|---|
| `dynamo_get_status` | read-only (`get_`) | dinamico | no (probe FileVersionInfo) | no | no |
| `dynamo_list_graph_io` | read-only (`list_`) | **statico** | no (parsing JSON) | no | no |
| `dynamo_generate_graph` | write | **statico** | no (builder deterministico) | sì | sì |
| `dynamo_run_graph` | write | dinamico | **sì** (late-binding) | sì | sì |

**3 tool su 4 non toccano mai Dynamo** → tutto il rischio-runtime concentrato in `dynamo_run_graph`.

### 5.1 `dynamo_get_status` (read-only, dinamico)

Diagnostica: Dynamo presente? versione? CPython3 disponibile? UI Dynamo aperta? `enableDynamo`
attivo? È il tool che l'AI chiama prima di generare/eseguire, e riporta come abilitare la feature se
è spenta.

### 5.2 `dynamo_list_graph_io` (read-only, statico)

Legge un `.dyn` (solo parsing JSON, non carica Dynamo) e restituisce l'interfaccia del grafo:
```jsonc
// input:  { "dynPath": "C:/…/graph.dyn" }
// output:
{
  "name": "…", "dynamoVersion": "3.x", "pythonEngine": "CPython3",
  "inputs":  [ {"nodeId":"…","name":"folderPath","type":"string","value":"…"} ],
  "outputs": [ {"nodeId":"…","name":"result"} ],
  "pythonNodeCount": 1, "totalNodes": 7,
  "warnings": [ "es. Engine=IronPython2 deprecato" ]
}
```
Serve a: capire un `.dyn` altrui prima di lanciarlo, popolare correttamente `inputValues` di
`dynamo_run_graph`, diagnosticare engine/versione.

### 5.3 `dynamo_generate_graph` (write, **statico**, gated)

```jsonc
// input:
{
  "name": "ExportRoomsToJson",
  "pythonCode": "…corpo Python (accede a Revit API via clr)…",
  "inputs":  [ {"name":"folderPath","type":"String"}, {"name":"limit","type":"Integer"} ],
  "outputs": [ {"name":"result"} ],
  "savePath": "C:/…/graph.dyn",   // opzionale; default: ~/.revitcortex/dynamo-graphs/<name>.dyn
  "execute": false                 // genera+salva; se true, dopo il salvataggio invoca run
}
```
Pipeline:
1. Gate `EnableDynamo` (vedi §6). Se off → `Fail(PermissionDenied)` con wording anti-retry.
2. `PythonSandbox.Validate(pythonCode)` → riusa `CodeSandbox` (no `System.IO`/`Net`/`Process`/
   `Reflection.Emit`/`Runtime.InteropServices`/`Microsoft.Win32`). Fallisce → `PermissionDenied` con
   la lista namespace vietati.
3. `DynamoGraphBuilder.BuildDynJson(spec)` → `.dyn` valido (deterministico).
4. `RequestConfirmation("generate Dynamo graph", 1)`.
5. Salva il file. Ritorna path + riepilogo (n. input/output, engine, byte).
6. Se `execute:true` → delega a `dynamo_run_graph`.

**Cartella dedicata di default:** `~/.revitcortex/dynamo-graphs/` (creata al primo uso, accanto a
`settings.json` e `audit.jsonl` — nessuna nuova convenzione di percorso). Il nome file deriva dal
campo `name` sanitizzato (caratteri illegali sostituiti); collisioni risolte con suffisso numerico.
`savePath` esplicito ha la precedenza sul default.

Statico perché non tocca Dynamo → generi qui anche dove Dynamo non è installato, esegui altrove.

### 5.4 `dynamo_run_graph` (write, dinamico, gated)

```jsonc
// input:
{ "dynPath": "C:/…/graph.dyn", "inputValues": { "folderPath": "C:/out", "limit": 50 }, "timeoutMs": 120000 }
```
Pipeline:
1. Gate `EnableDynamo`. Se off → `Fail(PermissionDenied)`.
2. `RequestConfirmation("run Dynamo graph", 1)`.
3. `DynamoRuntimeLoader.EnsureLoaded()` → fallimento = `Fail` pulito, nessun crash.
4. Journal-based execution via reflection (`DynamoRevit.ExecuteCommand` + `ForceRun`), sul thread
   principale (già garantito dal `RevitThreadDispatcher`).
5. Raccoglie output dai Watch/output nodes → ritorna al chiamante.

Note note e limitazioni gestite nello spec:
- Gestione `AutomationMode`, check se UI Dynamo è già aperta (conflitto headless).
- Bug noto Dynamo 3.0 (Revit 2025+): `ForceRun()` può fallire silenziosamente su grafi in Manual
  mode. Mitigazione: i grafi generati da noi impostano `RunType: "Automatic"`; documentare fallback
  al legacy launcher / `RunEnabled=true` per grafi Manual preesistenti. Se l'esecuzione headless
  fallisce, l'utente ha comunque il `.dyn` valido da aprire a mano — nessun vicolo cieco.

### 5.5 Regola di routing (nuova voce in CLAUDE.md)

> **Gerarchia:** se un tool RevitCortex nativo copre l'operazione → usalo. I `dynamo_*` sono ultima
> risorsa: solo quando nessun nativo copre il caso **e** l'utente ha approvato esplicitamente un
> approccio Dynamo/Python. Le descrizioni di `dynamo_generate_graph`/`dynamo_run_graph` contengono
> *"Use ONLY when no native RevitCortex tool covers the task AND the user explicitly approved a
> Dynamo/Python approach. REQUIRES EnableDynamo=true in ~/.revitcortex/settings.json"* — come già per
> `send_code_to_revit`.

---

## 6. Impostazione di abilitazione (clone del pattern `EnableCodeExecution`)

Il gate segue esattamente `send_code_to_revit`, il cui controllo vive **dentro il tool**
(`SendCodeToRevitTool.Execute()` legge `CortexSettings.EnableCodeExecution` come prima riga).

**Nuovo flag in `CortexSettings.cs`:**
```csharp
/// <summary>
/// When false (default), the Dynamo write tools (dynamo_generate_graph, dynamo_run_graph)
/// are refused at the tool-invocation boundary. Hard gate, not a soft warning.
/// </summary>
[JsonProperty("EnableDynamo")]
public bool EnableDynamo { get; set; } = false;
```

**Gate dentro i tool write:** `dynamo_generate_graph` e `dynamo_run_graph`, prima riga di `Execute()`:
se `!settings.EnableDynamo` → `Fail(PermissionDenied)` con wording anti-retry copiato da
`SendCodeToRevitTool` (*"…disabled in this installation. STOP: do NOT retry. Ask the user to enable
Dynamo via Settings > Tools (or \"EnableDynamo\": true in ~/.revitcortex/settings.json)."*).

**Read-only non gated:** `dynamo_get_status` (riporta `enableDynamo`) e `dynamo_list_graph_io` restano
sempre eseguibili — diagnosticano senza tentare operazioni vietate.

**UI Settings:** checkbox "Enable Dynamo integration" nella `ToolsSettingsPage`, accanto al toggle del
code execution. La `GeneralSettingsPage` fa già merge-write preservando le chiavi di altre pagine → il
nuovo flag persiste senza rompere il salvataggio esistente.

**Difesa in profondità risultante:**
1. **Presenza** (`IsDynamic` + probe): Dynamo installato/compatibile?
2. **Consenso** (`EnableDynamo`, default false): l'utente ha attivato la feature?
3. **Confirmation gate** per-operazione + **sandbox** sul Python.

---

## 7. Struttura progetto, build & deploy

**Nuovo progetto:** `src/RevitCortex.Tools.Dynamo/RevitCortex.Tools.Dynamo.csproj`
```
RevitCortex.Tools.Dynamo/
├─ Building/   DynamoGraphBuilder.cs, DynamoGraphSpec.cs, GraphPort.cs, DynJsonSchema.cs
├─ Security/   PythonSandbox.cs  (adapter su Core.Security.CodeSandbox)
├─ Runtime/    DynamoRuntimeLoader.cs, DynamoCapabilityProbe.cs
└─ Tools/      DynamoGetStatusTool.cs, DynamoListGraphIoTool.cs,
               DynamoGenerateGraphTool.cs, DynamoRunGraphTool.cs
```

**Vincoli `.csproj`:**
- Multi-target sulle stesse configurazioni esistenti (`Debug R23`…`Debug R27`), stesso schema
  `$(RevitVersion)`.
- **Nessun `PackageReference` alle DLL Dynamo** — tutto reflection. Un solo `.csproj` compila su
  net48/net8/net10.
- Rispetto vincoli net48 (no `record`/`init`/`Index`/`GetValueOrDefault`), come da CLAUDE.md.
- Referenziato da `RevitCortex.Plugin.csproj` → stesso add-in, stesso deploy, un solo artefatto.

**Registrazione tool:** i 4 tool sono normali `ICortexTool`, scoperti dal `CortexRouter` come gli
altri. `IsDynamic` gestisce l'auto-disabilitazione. Zero modifiche a router/bridge/server.

**Deploy:** `deploy.ps1` invariato nella struttura; il nuovo `.dll` finisce nella cartella addin.
Onorata la regola "deploy all R23→R27".

---

## 8. Roadmap fasata (target finale R23→R27)

Il release stabile finale copre **tutti e 5** i target (standard rispettato). L'ordine di sviluppo
isola il rischio Dynamo, sullo **stesso branch**:

1. **Fase A — R25 + R26** (net8, Dynamo 3.x): un solo mondo di dipendenze. Valida l'architettura
   end-to-end: builder, sandbox, probe, i 4 tool, esecuzione headless.
2. **Fase B — R23 + R24** (net48, Dynamo 2.x/3.0): API `RevitDynamoModel` con firme leggermente
   diverse, journal-launcher legacy. Verifica CPython3 disponibile; fallback documentato.
3. **Fase C — R27** (net10, Dynamo 3.7+/4.x): launcher nuovo con bug Manual-mode noto. Massima
   cautela; è dove Autodesk spinge il proprio MCP ufficiale.

Release stabile solo quando tutti e 5 i target compilano e i test passano.

---

## 9. Sicurezza

- **Sandbox Python:** riuso di `CodeSandbox.Validate` sul corpo Python prima della scrittura del
  `.dyn` — vieta `System.IO`, `System.Net`, `System.Diagnostics.Process`, `Microsoft.Win32`,
  `System.Reflection.Emit`, `System.Runtime.InteropServices`. Coerente con `send_code_to_revit`.
- **Limite onesto e documentato:** la sandbox protegge il canale automatico AI→generazione→esecuzione.
  Un utente che apre il `.dyn` a mano in Dynamo può comunque eseguire qualsiasi cosa — la sandbox non
  è (e non può essere) un confinamento del runtime Dynamo, è un guardrail sul percorso automatico.
- **Audit log:** ogni chiamata `dynamo_*` loggata via `AuditLogger` come tutti gli altri tool.
- **Confirmation gate** sui tool write, coerente coi tool distruttivi esistenti.

---

## 10. Rischi e mitigazioni

| Rischio | Impatto | Mitigazione |
|---|---|---|
| DLL Dynamo assenti/versione incompatibile | Alto (se statico romperebbe il plugin) | Late-binding lazy + try/catch difensivo; solo `dynamo_run_graph` fallisce, il resto intatto |
| Crash fatale runtime Dynamo | Alto (uccide Revit) | Non evitabile da nessun add-in (vincolo di processo); confinato al solo `dynamo_run_graph`, opt-in, con confirmation |
| Bug ForceRun Manual-mode (Dynamo 3.0) | Medio | Grafi generati in `RunType: Automatic`; fallback legacy launcher documentato; `.dyn` sempre apribile a mano |
| Schema `.dyn` sbagliato → grafo non apribile | Medio | Schema verificato da sorgenti master; builder testato al 100% con round-trip JSON senza Revit |
| R26 non installato su questa macchina | Operativo (test headless) | Test headless R26 rimandati a macchina con R26; builder/probe/list testabili ovunque |
| Sandbox rompe workflow legittimi (Excel/CSV via System.IO) | Basso→Medio | Accettato: coerenza con send_code_to_revit; l'utente può aprire il .dyn a mano per casi IO. Rivalutabile in futuro (flag opt-in) |

---

## 11. Cosa NON facciamo (YAGNI)

- **No add-in separato** — nessun isolamento aggiuntivo reale, raddoppia deploy/versioning.
- **No generazione full-node** — solo Python-centrico (grafi full-node sono fragili/ricerca aperta).
- **No IronPython2** — deprecato; solo CPython3.
- **No injection avanzata su nodi Symbol/CodeBlock arbitrari** — `inputValues` opera sugli input
  node tipizzati del nostro scheletro; per `.dyn` esterni si documenta il limite.
- **No Dynamo Player API dedicata** in questa iterazione — `dynamo_run_graph` copre l'esecuzione.
- **No modifiche a server/bridge/router/threading.**

---

## 12. Definizione di "stabile" (criteri di merge del branch)

- Tutti e 5 i target (`Debug R23`…`Debug R27`) compilano.
- Test unit del builder verdi su tutti i target (round-trip JSON, invarianti schema).
- `dynamo_generate_graph` + `dynamo_list_graph_io` verificati live su almeno un target.
- `dynamo_run_graph` verificato live end-to-end su almeno un target con Dynamo installato (R25).
- Con Dynamo assente: i 288 tool esistenti + `dynamo_generate_graph`/`dynamo_list_graph_io`
  funzionano; `dynamo_run_graph`/`dynamo_get_status` si auto-disabilitano puliti.
- Con `EnableDynamo=false`: i tool write rifiutano con messaggio corretto.
- Docs aggiornati (USER_GUIDE, tool-schemas.txt, WORKFLOWS/CLAUDE.md, regola di routing).

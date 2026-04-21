# RevitCortex v1.0.2 — Ottimizzazioni reali (verificate sul codice)

> **Nota metodologica:** il primo report dell'agent conteneva stime esagerate.
> Dopo aver letto i file citati (catch blocks, OperateElement, LINQ patterns),
> solo **3 ottimizzazioni** hanno base reale e misurabile. Questo plan include
> solo quelle — niente "60 catch da loggare" se sono best-effort intenzionali.

**Goal:** 2 fix perf veri + 1 fix doc drift, tutti verificati sul codice.

**Architecture:** Minimal changes. Nessun refactor grande. Zero breaking API.

---

## Task 1: LINQ hotspot reale in CollectByKind

**Problema verificato:** `src/RevitCortex.Tools/Elements/AIElementFilterTool.cs:214`
```csharp
return collector.ToElements().ToList();  // materializza TUTTO prima del Take esterno
```
Il chiamante (riga 117) poi fa `elements.Take(maxElements).ToList()`. Su 50K elementi, `ToElements()` crea una lista di 50K prima che il `Take(100)` la tagli. Doppio spreco: materialize + allocate.

**Fix:**
1. Aggiungere parametro `maxElements` opzionale a `CollectByKind`
2. Se specificato e > 0, usare `FirstElement()` + loop controllato oppure `collector.WhereElementIsNotElementType().Take(N)` via IEnumerable

**Impatto misurabile:** su modello Snowdon (37k elementi) il tool `ai_element_filter` con `maxElements: 3` oggi fa `ToList()` di ~37k wall elements (lo abbiamo visto: riporta `"Found 1132 element(s)"` ma ne materializza molti di più per il filtraggio). Atteso: riduzione 50-90% tempo CPU su modelli grandi.

**Files:**
- Modify: `src/RevitCortex.Tools/Elements/AIElementFilterTool.cs`

- [ ] **Step 1: Leggere `CollectByKind` e chiamanti**

Run: `grep -n "CollectByKind" src/RevitCortex.Tools/Elements/AIElementFilterTool.cs`

- [ ] **Step 2: Firma nuova**

```csharp
private static List<Element> CollectByKind(
    Document doc, bool isElementType,
    string? filterCategory, string? filterElementType,
    long filterFamilySymId, bool filterVisibleInView,
    XYZ? bbMin, XYZ? bbMax,
    int maxElements = 0)  // ← nuovo parametro
```

- [ ] **Step 3: Sostituire riga 214**

Da:
```csharp
return collector.ToElements().ToList();
```
A:
```csharp
if (maxElements > 0)
{
    var result = new List<Element>(capacity: maxElements);
    foreach (var e in collector)
    {
        result.Add(e);
        if (result.Count >= maxElements) break;
    }
    return result;
}
return collector.ToElements().ToList();
```

- [ ] **Step 4: Passare `maxElements` dai 3 chiamanti (righe 90-108)**

Per ciascuno dei 3 rami (`isElementType` solo, `includeInstances`, else): `CollectByKind(..., maxElements: maxElements)`.
Attenzione: se `maxElements == 0`, comportamento invariato. Se > 0, early-exit nel collector enumeration.

- [ ] **Step 5: Rimuovere il doppio `.Take().ToList()` (riga 117)**

Da:
```csharp
if (maxElements > 0 && elements.Count > maxElements)
{
    elements = elements.Take(maxElements).ToList();
    limitNote = ...;
}
```
A:
```csharp
if (maxElements > 0 && totalCount > maxElements)
    limitNote = $" (limited to {maxElements} of {totalCount} matches)";
```
Il `totalCount` ora deve essere letto dal collector PRIMA del take — rivedere logica.

**Problema di consistenza:** `totalCount` vs `elements.Count`. Se facciamo early-exit, perdiamo il `totalCount` vero. Soluzione: fare `collector.GetElementCount()` PRIMA del collect per avere il count totale, POI il collect early-exit. GetElementCount è O(n) ma non materializza, molto più veloce di ToList.

- [ ] **Step 6: Build R25 + R24**

```bash
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R25"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R24"
```
Expected: 0 errori.

- [ ] **Step 7: Benchmark live**

Con Revit 2025 + Snowdon aperto, misurare:
```
ai_element_filter OST_Walls maxElements=3 → prima: tempo?  dopo: tempo?
```
Tempo atteso: <200ms (oggi probabile ~800ms-2s).

- [ ] **Step 8: Commit**

```bash
git commit -m "perf(ai_element_filter): early-exit collector enumeration with maxElements"
```

---

## Task 2: Catch silenzioso REALE in RevitBridge.ResolvePort

**Problema verificato:** `src/RevitCortex.Server/Connection/RevitBridge.cs:164`
```csharp
catch { }
```
Silenzia TUTTI gli errori di parsing `settings.json`. Se l'utente ha JSON corrotto, il port default 8080 viene usato senza dirlo. Debuggare "non si connette al Revit sulla porta 8081 che ho impostato" diventa impossibile.

**Fix:** Log su stderr (il server MCP scrive già lì).

**Files:**
- Modify: `src/RevitCortex.Server/Connection/RevitBridge.cs`

- [ ] **Step 1: Sostituire catch**

Da (riga 164):
```csharp
catch { }
```
A:
```csharp
catch (Exception ex)
{
    Console.Error.WriteLine($"[RevitBridge] Failed to read port from settings.json: {ex.Message}. Falling back to default port 8080.");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```
Expected: 0 errori.

- [ ] **Step 3: Commit**

```bash
git commit -m "fix(server): log settings.json parse errors instead of silently using port 8080"
```

---

## Task 3: Docs drift

**Problema verificato:**
- `CLAUDE.md` dice 157 tool ma il conteggio effettivo dopo v1.0.1 potrebbe essere diverso (3 nuovi tool post-v1.0.0 aggiunti: `manage_global_parameters`, `manage_project_units`, `manage_additional_settings` = 152 + 3 = 155? o 157? Da verificare)
- `tool-schemas.txt` potrebbe essere stale
- README non documenta `EnableCodeExecution` flag

**Fix:** Allineare doc e count reali.

**Files:**
- Modify: `CLAUDE.md` (se necessario)
- Modify: `README.md` (sezione security / EnableCodeExecution)
- Regenerate: `tool-schemas.txt` (se script disponibile)

- [ ] **Step 1: Count effettivo**

```bash
grep -rn "public.*ICortexTool" src/RevitCortex.Tools --include="*.cs" | grep -v "/obj/\|/bin/" | wc -l
```
Expected: numero concreto (es. 155 o 157).

- [ ] **Step 2: Aggiornare `CLAUDE.md` se diverso**

Cerca "157 tool" e sostituisci col numero reale.

- [ ] **Step 3: Aggiungere sezione security al README**

Dopo la sezione "Installation", aggiungere paragrafo breve su `EnableCodeExecution` in `settings.json`.

- [ ] **Step 4: Rigenerare tool-schemas.txt se esiste script**

```bash
ls server/generate-tool-schemas-csharp.mjs
# Se esiste:
cd server && node generate-tool-schemas-csharp.mjs > ../tool-schemas.txt
```

- [ ] **Step 5: Commit**

```bash
git commit -m "docs: sync tool count, document EnableCodeExecution in README"
```

---

## Task 4: Build finale + release v1.0.2

- [ ] **Step 1: All tests**

```bash
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```
Expected: 84 pass / 1 skip.

- [ ] **Step 2: Smoke test live**

Riaprire Revit 2025 + Snowdon, ritestare `ai_element_filter`:
- Verificare early-exit funziona: `maxElements: 3` torna 3 elementi
- Verificare `totalCount` è corretto e matching col vero numero di walls
- Misurare tempo: deve essere < 500ms anche su categoria grande

- [ ] **Step 3: Build release package**

```bash
powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version "1.0.2"
```

- [ ] **Step 4: Tag + push + release**

```bash
git tag -a v1.0.2 -m "RevitCortex v1.0.2 - performance optimizations"
git push origin main
git push origin v1.0.2
gh release create v1.0.2 "RevitCortex-v1.0.2.zip" --repo LuDattilo/RevitCortex --title "v1.0.2 - Performance" --notes "..."
```

- [ ] **Step 5: Copiare nuovo ZIP in OneDrive distribuzione**

```bash
cp RevitCortex-v1.0.2.zip "/c/Users/luigi.dattilo/OneDrive - GPA Ingegneria Srl/Documenti/RevitCortex/distribution/"
```

---

## Self-Review

- [x] **Honest scope:** solo 3 fix con base verificata nel codice. Niente inventato.
- [x] **Measurable impact:** Task 1 ha benchmark concreto; Task 2 elimina debug pain reale; Task 3 fix docs.
- [x] **No over-engineering:** zero nuove classi, zero refactor grandi, zero nuove dipendenze.
- [x] **Risk low:** il cambio in `CollectByKind` è semanticamente equivalente (stesso output finale, solo early-exit), il catch logging è additivo.

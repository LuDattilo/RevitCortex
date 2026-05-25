# 23 — Dynamic Tools & DocumentCapabilities

**Scope:** Tool che si abilitano solo quando il modello soddisfa prerequisiti.
**Sources:** CLAUDE.md §"IsDynamic Convention", src/RevitCortex.Core/Discovery/
**Last verified:** 2026-05-25

## Pattern

Un tool con `IsDynamic = true` è esposto al client MCP solo se `DocumentCapabilities` lo abilita.

## Flow

1. Plugin apre un documento.
2. `DocumentAnalyzer.Analyze(doc)` popola `DocumentCapabilities`:
   - Categorie presenti (es. presenza di `OST_Rooms`)
   - Worksets
   - Phases
   - Plugin esterni (revit-ifc, ecc.)
3. Per ogni tool con `IsDynamic = true`, l'analyzer chiama `capabilities.EnableTool("tool_name")` se i prerequisiti sono soddisfatti.
4. `CortexRouter` espone solo i tool dove `!IsDynamic || capabilities.IsToolEnabled(tool.Name)`.

## Quando usare IsDynamic

| Tool | IsDynamic? | Motivo |
|---|---|---|
| `get_element_parameters` | false | Funziona su qualsiasi modello |
| `ifc_link` | true | Richiede `revit-ifc` installato |
| `set_element_phase` | true | Solo se `doc.Phases.Size > 0` |
| `pbi_publish_elements` | true | Solo se PBI workspace configurato |

## Verifica capabilities

```csharp
if (session.Capabilities.IsToolEnabled("ifc_link"))
{
    // OK to proceed
}
```

`DocumentAnalyzer` deve aggiornare le capabilities anche quando il documento cambia (apertura nuovo modello, plugin esterni caricati a runtime).

## Required checks

- [ ] `IsDynamic` impostato correttamente per i tool con prerequisiti.
- [ ] `DocumentAnalyzer` aggiornato per chiamare `EnableTool` quando appropriato.
- [ ] `CortexRouter` filtra dinamicamente (verificato in `CortexRouter.cs`).

## Avoid

- Non impostare `IsDynamic = true` per tool sempre disponibili (overhead inutile).
- Non dimenticare di aggiornare `DocumentAnalyzer` quando aggiungi un tool dinamico.
- Non fare check di capability dentro `Execute` (è già filtrato a monte).

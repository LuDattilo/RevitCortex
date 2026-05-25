# 08 — PowerBI Workflows

**Scope:** Push elements/schedules, query roundtrip, selection cross-app.
**Sources:** docs/powerbi-push-architecture-spec.md, docs/USER_GUIDE.md §PBI
**Last verified:** 2026-05-25

## Decision rules

### Auth check

`pbi_check_auth` prima di qualsiasi push. Se non autenticato, l'utente deve fare sign-in manualmente.

### Push elements / schedules / selection

| Caso | Tool |
|---|---|
| Push elementi filtrati | `pbi_publish_elements` |
| Push uno schedule esistente | `pbi_publish_schedules` |
| Push selezione corrente | `pbi_publish_selection` |
| Push tabella arbitraria | `push_table_to_powerbi` |

**REGOLA CRITICA:** Ogni riga pushata DEVE contenere una colonna `ElementId`. È la chiave che permette a PBI di joinare con la tabella master Elements e di abilitare il cross-filter.

### Query / Selection roundtrip

1. `pbi_query` → ottiene risultati DAX/JSON
2. `select_from_powerbi` → applica selezione filtrata in Revit
3. `pbi_get_binding` → verifica binding workspace ↔ dataset

### Override visuali

Gli override pittati da PBI devono essere wipati solo su elementi che PBI stesso ha toccato (non iterare TUTTI gli elementi visibili). Usare `clear_overrides` con scope ristretto.

### Vincoli Push API

- No upsert: ogni push aggiunge righe.
- DELETE è all-or-nothing.
- Limite: 1.000.000 righe/ora.
- API deprecata da 2027-10-31: prevedere migrazione a Service Principal + REST.

## Required checks

- [ ] `pbi_check_auth` chiamato.
- [ ] Ogni payload pushato ha colonna `ElementId`.
- [ ] Per CSV/Excel: ElementId presente.
- [ ] Override reset scoped, non globale.

## Avoid

- Non pushare dati senza `ElementId`.
- Non usare DELETE per "aggiornare": è all-or-nothing.
- Non iterare TUTTI gli elementi visibili nel reset override.
- Non assumere che l'utente sia autenticato.

# 03 — Destructive Operations & DryRun

**Scope:** Qualsiasi tool che cancella, modifica massivamente o sovrascrive dati.
**Sources:** CLAUDE.md §"Confirmation Dialogs", WORKFLOWS.md §"Operazioni Distruttive"
**Last verified:** 2026-05-25

## Tool con conferma nativa Revit

`delete_element`, `delete_selection`, `delete_material`, `purge_unused`, `wipe_empty_tags`, `set_element_parameters`, `set_compound_structure`, `batch_rename`, `override_graphics`, `set_element_phase`, `set_element_workset`, `change_element_type`, `load_family`.

Tutti questi mostrano un TaskDialog nativo prima dell'esecuzione.

## Decision rules

1. Per tool con flag `dryRun`: **sempre** prima call con `dryRun: true`.
2. Leggere SOLO i contatori (`modifiedCount`, `skippedCount`, `plannedCount`) dal dryRun, non la lista elementi.
3. Eseguire la versione reale solo dopo aver mostrato l'anteprima all'utente o aver ottenuto consenso esplicito.
4. Se l'utente annulla, il tool restituisce `CortexErrorCode.Cancelled`:
   ```json
   {"success": false, "error": {"code": "Cancelled", "message": "Operation cancelled by user"}}
   ```
5. Non ripetere automaticamente un'operazione cancellata: chiedere all'utente cosa fare.

## Required checks

- [ ] DryRun eseguito.
- [ ] Contatori letti, non lista elementi.
- [ ] Conferma esplicita dell'utente prima dell'esecuzione reale.
- [ ] Gestione `Cancelled`.

## Avoid

- Non eseguire operazioni distruttive senza dryRun.
- Non leggere l'intera lista elementi dal dryRun (spreca token).
- Non chiamare il tool una seconda volta automaticamente dopo `Cancelled`.
- Non bypassare con `send_code_to_revit` per "saltare" la conferma.

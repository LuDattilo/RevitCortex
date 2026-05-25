# 01 — Session Start & Locale Detection

**Scope:** Prima chiamata MCP di una sessione su modello aperto.
**Sources:** CLAUDE.md §"IMPORTANT: Detect Revit Language First", WORKFLOWS.md §"Rilevamento Lingua Revit"
**Last verified:** 2026-05-25

## Decision rules

1. La **prima** chiamata di sessione deve essere `get_project_info` con tutti gli include attivi (default).
2. Da quel momento, ogni `get_project_info` successivo deve filtrare: `{"includeLevels": false, "includeLinks": false, "includePhases": false, "includeWorksets": false}`.
3. La lingua si rileva dai nomi dei parametri restituiti, non si assume:
   - EN: "Level", "Comments", "Type Name"
   - IT: "Livello", "Commenti", "Nome del tipo"
   - FR: "Niveau", "Commentaires", "Nom du type"
   - DE: "Ebene", "Kommentare", "Typname"
4. Per categorie, preferire sempre i codici `OST_*` (language-independent) ai nomi localizzati.

## Required checks

- [ ] `get_project_info` completo eseguito una sola volta a inizio sessione.
- [ ] Lingua rilevata e annotata nel contesto della conversazione.
- [ ] Se il modello ha fasi (`phases.length > 0`), tenerlo presente per `set_element_phase`.
- [ ] Se è workshared (`isWorkshared: true`), tenerlo presente per `set_element_workset`.

## Avoid

- Non assumere la lingua: sempre verificare.
- Non rieseguire `get_project_info` completo durante la sessione: filtrare.
- Non confondere `isWorkshared` con presenza di fasi: sono indipendenti.

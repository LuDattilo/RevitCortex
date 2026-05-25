# 05 — Model Health, Warnings, Clash

**Scope:** Controlli rapidi sul modello e clash detection.
**Sources:** CLAUDE.md §"Session A — Morning Check", WORKFLOWS.md §"Controllo Qualita Mattutino"
**Last verified:** 2026-05-25

## Decision rules

### Morning check (~500-800 token)

Sequenza canonica:
1. `check_model_health` (compact: true)
2. `get_warnings` con `maxWarnings: 10` (mai 500 di default)
3. [opzionale] `clash_detection` su una coppia di discipline

Dopo questa sequenza, chiudere la sessione. Non incatenare task di authoring.

### get_warnings

| Scenario | maxWarnings |
|---|---|
| Quick check | 10 |
| Analisi categoria | 50 |
| Export completo | (no default) |

### Clash detection

| Caso | Tool |
|---|---|
| Conteggio + lista ID | `clash_detection` (400-600 token) |
| Review visuale 3D con section box | `workflow_clash_review` (800+ token) |

Su modelli architettonici, ricordare: colonne = `OST_Columns`, NON `OST_StructuralColumns`.

### lines_per_view_count

⚠️ ATTENZIONE: questo tool può crashare il server su modelli con 300+ viste.
- Mai eseguire in parallelo con altri tool.
- Sempre con `threshold >= 20`.
- Su modelli grandi, considerare di non chiamarlo affatto.

## Required checks

- [ ] `get_warnings` chiamato con `maxWarnings` esplicito (mai default).
- [ ] `clash_detection`: specificate le due categorie esatte.
- [ ] `workflow_model_audit` usato solo se quick check non basta.

## Avoid

- Non usare `workflow_model_audit` per un check veloce (3000+ token vs 500-800).
- Non chiamare `get_warnings` senza `maxWarnings`.
- Non eseguire `lines_per_view_count` in parallelo.
- Non mescolare QA + authoring nella stessa sessione lunga.

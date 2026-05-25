# 09 — Obsidian Workflows

**Scope:** Integrazione vault Obsidian con RevitCortex (snapshot, note, write-back).
**Sources:** docs/superpowers/specs/2026-05-19-revitcortex-obsidian-integration-design.md
**Last verified:** 2026-05-25

## Stato

Spec approvata, implementazione non ancora rilasciata in production. I tool descritti qui sono pianificati. Verificare in `tool-schemas.txt` se sono disponibili prima di proporli all'utente.

## Decision rules (preview)

### Snapshot verso vault

Esportare lo stato del modello (livelli, fasi, categorie, parametri custom) come file Markdown dentro un vault Obsidian configurato.

### Command note

Note Obsidian con frontmatter `cortex-command` interpretate dalla skill: contengono un comando MCP, lo stato di esecuzione, il timestamp.

### Write-back parametrico

Modifiche fatte nel vault (es. valore di un parametro in un file `.md`) possono essere applicate al modello via `sync_csv_parameters` o tool dedicato, con dry-run obbligatorio.

## Avoid

- Non proporre questi tool se non sono ancora nello schema.
- Non bypassare dry-run su write-back.

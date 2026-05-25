# 10 — send_code_to_revit Escalation

**Scope:** Quando proporre uno script C# custom invece dei tool nativi.
**Sources:** CLAUDE.md §"send_code_to_revit", docs/SECURITY.md §"Sandbox"
**Last verified:** 2026-05-25

## Regola fondamentale

**MAI usare `send_code_to_revit` autonomamente per bulk/batch operations.** Sempre chiedere consenso esplicito all'utente prima.

## Frase standard di consenso

> "Posso usare `send_code_to_revit` per eseguire questa operazione in modo più efficiente con uno script C#, oppure preferisci che proceda con i tool standard (potrebbe richiedere più chiamate)?"

Solo dopo risposta affermativa, procedere con lo script.

## Decision rules

Motivi per cui chiedere e non assumere:

1. Gli script bypassano il safety layer nativo (dryRun, conferme).
2. DLL conflicts (archintelligence, BIM360, altri add-in) possono crashare silenziosamente `send_code_to_revit`.
3. L'utente può preferire tracciabilità via discrete tool calls.
4. **NON chiamare `Document.EditFamily` da `ExternalEvent`**: i dialog modali deadlockano Revit (riferimento: incident b292ace su Snowdon Towers).

## Sandbox

Namespace **vietati** dal sandbox (causano `CortexErrorCode.PermissionDenied`):
- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Validazione in `CodeSandbox.Validate(string code)` (`RevitCortex.Core`).

## Naming variabili

- Document: `document` (non `doc`, `Doc`, `uidoc`).
- UIDocument: `new UIDocument(document)`.
- ElementId: `.Value` su R2024+, `.IntegerValue` su R2023.

## Required checks

- [ ] Consenso utente ottenuto.
- [ ] Alternativa nativa proposta come opzione A.
- [ ] Nessuna chiamata `EditFamily` da `ExternalEvent`.
- [ ] Sandbox validation in `CodeSandbox.Validate` non bypassata.

## Avoid

- Non proporre autonomamente per >1 elemento senza consenso.
- Non usare namespace IO/Net/Process.
- Non chiamare `EditFamily` da `ExternalEvent`.
- Non assumere che l'utente preferisca script: è opzione B di default.

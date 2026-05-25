# 24 — Read-Only Mode, Audit Log, Sandbox

**Scope:** Sicurezza non-funzionale del server RevitCortex.
**Sources:** docs/SECURITY.md, CLAUDE.md §"Security Requirements (NFR)"
**Last verified:** 2026-05-25

## Read-only mode

Quando `readOnlyMode: true` in `~/.revitcortex/settings.json`, `CortexRouter` rifiuta tutti i tool write con `CortexErrorCode.PermissionDenied`.

### Naming convention

Sono **read-only** i tool che iniziano con:
- `get_`, `list_`, `find_`, `analyze_`, `check_`, `measure_`, `audit_`, `export_`
- `say_hello`
- `clash_detection`
- `lines_per_view_count`

Tutto il resto è considerato **write tool** e bloccato in read-only mode.

Implementazione in `CortexRouter.IsReadOnlyTool(string toolName)` (public static, testabile).

## Audit log

Ogni esecuzione tool è loggata in `~/.revitcortex/audit.jsonl` (append-only):

```json
{
  "ts": "2026-05-25T10:30:00Z",
  "tool": "tool_name",
  "input_summary": "...",
  "result": "ok|fail",
  "error_code": null,
  "elements_affected": 0
}
```

`AuditLogger` in `RevitCortex.Core`. `CortexRouter` chiama `AuditLogger.Log()` dopo ogni invocazione.

Audit v2 (Apr 2026) aggiunge `duration_ms` e altri campi async. Parser Python `rclog` aggiornato dopo i dati reali.

## Sandbox send_code_to_revit

Validazione in `CodeSandbox.Validate(string code)`. Namespace vietati:
- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Bypass solo disabilitando `send_code_to_revit` interamente nelle settings.

## Required checks

- [ ] Tool naming rispetta convenzione read-only.
- [ ] `IsReadOnlyTool` aggiornato per tool nuovi che non matchano i prefissi standard.
- [ ] `AuditLogger.Log()` chiamato per ogni esecuzione.
- [ ] Per `send_code_to_revit`: `CodeSandbox.Validate` chiamato prima dell'esecuzione.

## Avoid

- Non aggiungere tool write con prefisso `get_` (confonderebbe la convenzione).
- Non skippare `AuditLogger.Log()` per "performance".
- Non bypassare il sandbox.

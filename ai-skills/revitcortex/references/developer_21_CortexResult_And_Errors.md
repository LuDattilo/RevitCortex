# 21 — CortexResult & Error Codes

**Scope:** Envelope unificato per tutti i tool RevitCortex.
**Sources:** CLAUDE.md §"CortexResult<T>", src/RevitCortex.Core/Results/CortexResult.cs
**Last verified:** 2026-05-25

## Regola fondamentale

Ogni tool ritorna `CortexResult<T>`. **Mai** lanciare eccezioni o ritornare stringhe raw.

## Success

```csharp
return CortexResult<object>.Ok(new {
    greeting = "Hello",
    count = 42
});
```

## Failure

```csharp
return CortexResult<object>.Fail(
    CortexErrorCode.ElementNotFound,
    "Element 12345 does not exist in the active document",
    suggestion: "Check the element ID or ensure the correct document is open");
```

## Error codes

| Code | Numerico | Quando |
|---|---|---|
| `ElementNotFound` | 100 | ID non esiste, elemento eliminato |
| `PermissionDenied` | 200 | Read-only mode, sandbox negato |
| `TransactionFailed` | 300 | Transaction commit failed |
| `InvalidInput` | 400 | Parametri input malformati |
| `Timeout` | 500 | Operation > timeout limit |
| `Cancelled` | 600 | Utente ha annullato (TaskDialog) |
| `Unknown` | 900 | Eccezione non classificata |

## Propagazione errori dal Plugin al Server

`RevitBridge` (server) trasforma `CortexResult.Fail` in payload JSON strutturato senza lanciare eccezioni. Verificato live 2026-05-15.

Esempio payload:
```json
{
  "success": false,
  "error": {
    "code": "Cancelled",
    "message": "Operation cancelled by user",
    "suggestion": null
  }
}
```

## Required checks

- [ ] Tool ritorna `CortexResult<T>` sempre.
- [ ] Errori usano `CortexErrorCode` enum, mai stringhe libere.
- [ ] `suggestion` compilato quando utile per l'utente.
- [ ] Nessun `throw` non gestito da `Execute`.

## Avoid

- Non lanciare eccezioni che escono da `Execute`.
- Non ritornare stringhe raw o JObject sciolti.
- Non inventare error codes oltre quelli enum.
- Non lasciare `suggestion: null` se c'è una azione utile per l'utente.

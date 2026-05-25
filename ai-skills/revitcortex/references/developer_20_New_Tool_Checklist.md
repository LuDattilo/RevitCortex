# 20 — New Tool Checklist

**Scope:** Aggiungere un nuovo `ICortexTool` al server RevitCortex.
**Sources:** CLAUDE.md §"ICortexTool", src/RevitCortex.Core/Tools/ICortexTool.cs
**Last verified:** 2026-05-25

## File da toccare

| File | Responsabilità |
|---|---|
| `src/RevitCortex.Tools/<Category>/<ToolName>Tool.cs` | Implementazione `ICortexTool` |
| `src/RevitCortex.Server/Tools/<Category>Tools.cs` | Definizione MCP (nome, descrizione, JsonSchema) |
| `tool-schemas.txt` | Firma compatta (rigenerare con `node server/generate-tool-schemas-csharp.mjs`) |
| `docs/USER_GUIDE.md` | Documentazione end-user |
| `WORKFLOWS.md` | Se il tool fa parte di un workflow nuovo o esistente |
| `CLAUDE.md` | Se introduce regole/anti-pattern specifici |
| `ai-skills/revitcortex/references/operator_*.md` | Reference operativo se cambia un workflow |

## Naming

- Nome MCP: `snake_case` (es. `get_element_parameters`)
- Classe C#: `PascalCase` + suffisso `Tool` (es. `GetElementParametersTool`)
- Categoria: PascalCase (es. "Elements", "Views", "Materials", "Ifc", "PowerBI")

## Interfaccia minima

```csharp
public class MyNewTool : ICortexTool
{
    public string Name => "my_new_tool";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // 1. Validare input
        // 2. Se distruttivo: session.RequestConfirmation("action", count)
        // 3. Eseguire dentro Transaction se modifica il doc
        // 4. Ritornare CortexResult<object>.Ok(...) o .Fail(...)
    }
}
```

## RequiresDocument

| Valore | Significato |
|---|---|
| `true` | Tool ha bisogno di un modello Revit aperto |
| `false` | Tool meta (es. `say_hello`, capability check) |

## IsDynamic

Se `true`, il tool è registrato solo se `DocumentCapabilities` lo abilita. Vedi `developer_23_Dynamic_Tools_And_Capabilities.md`.

## Required checks

- [ ] `ICortexTool` implementato correttamente.
- [ ] Naming convention rispettata.
- [ ] Schema MCP definito in `<Category>Tools.cs`.
- [ ] `tool-schemas.txt` rigenerato.
- [ ] `USER_GUIDE.md` aggiornato.
- [ ] Se distruttivo: `RequestConfirmation` chiamato.
- [ ] Build R25 + R24 verde (vedi `developer_22`).
- [ ] Test unitario in `RevitCortex.Tests/`.

## Avoid

- Non aggiungere un tool senza aggiornare `tool-schemas.txt`.
- Non aggiungere un tool senza test.
- Non dimenticare il `RequestConfirmation` per operazioni distruttive.
- Non usare `record` types (vedi `developer_22`).

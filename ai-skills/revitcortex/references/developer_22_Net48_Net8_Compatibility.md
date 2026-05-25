# 22 — Net48 vs Net8+ Compatibility

**Scope:** Compilare RevitCortex per Revit 2023→2027 (target framework variabile).
**Sources:** CLAUDE.md §"Cross-Target Compatibility", memoria feedback_revit_target_range
**Last verified:** 2026-05-25

## Matrix target framework

| Revit | Framework |
|---|---|
| 2023 | net48 |
| 2024 | net48 |
| 2025 | net8.0-windows |
| 2026 | net8.0-windows |
| 2027 | net10.0-windows |

## Feature C# vietate su net48

| Feature | net8+ | net48 | Fix |
|---|---|---|---|
| `record` types | OK | **ERROR** CS0518 (`IsExternalInit` missing) | Usare `class` con readonly properties + constructor |
| `Dictionary.GetValueOrDefault()` | OK | **ERROR** CS1061 | `TryGetValue` con ternario |
| `init` accessors | OK | **ERROR** CS0518 | `{ get; }` + constructor |
| `Index`/`Range` (`^1`, `..`) | OK | **ERROR** | `.Length - 1`, `.Substring()` |
| `IAsyncEnumerable<T>` | OK | **ERROR** | Non disponibile su net48 |
| `file`-scoped types | OK | **ERROR** | Usare `internal` |
| Default interface methods | OK | **ERROR** | Spostare su abstract class o helper |

## Regola di verifica

Dopo OGNI modifica a un file C#:

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Una build R25 verde **non garantisce** che R24 compili.

Prima del release: tutti i target R23→R27 devono buildare:
```bash
for cfg in "R23" "R24" "R25" "R26" "R27"; do
  dotnet build -c "Debug $cfg" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
done
```

## R27 e .NET 10 SDK

R27 richiede SDK ≥ 10. `global.json` pinna SDK 8 con `rollForward: latestMajor`. Senza SDK 10 installato: `NETSDK1045`. Runtime end-user: serve .NET 10 runtime (Revit 2027 lo ship).

## Required checks

- [ ] Nessuna `record` type nei file C#.
- [ ] Nessun `GetValueOrDefault()` su `Dictionary`.
- [ ] Build R25 verde.
- [ ] Build R24 verde.
- [ ] Per release: anche R23, R26, R27 verdi.

## Avoid

- Non usare feature C# 9+ senza verificare net48.
- Non assumere che R25 verde = R24 verde.
- Non skippare la build R24 prima del commit.

# 25 — Build, Test, Release Checklist

**Scope:** Pre-commit checks, build matrix, release flow.
**Sources:** CLAUDE.md §"Build Commands", memoria reference_release_flow, feedback_deploy_all_revit_targets
**Last verified:** 2026-05-25

## Build plugin (5 target)

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

**Regola**: Pre-commit basta R25 + R24. Pre-release tutti e 5.

## Build server MCP

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

## Test

```bash
dotnet test -c "Debug R25"
```

## Deploy

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

Default deploya solo R25. Per release multi-target, deploya tutti e 5 (vedi memoria `feedback_deploy_all_revit_targets`).

## Server publish

**Mai mischiare** framework-dependent e self-contained publish su `~/.revitcortex/server`. Fingerprint del problema: `"No frameworks were found"` in `mcp-server-revitcortex.log`.

## Release flow (GitHub Releases)

Repo `LuDattilo/RevitCortex` è pubblico. Repo asset release era separato (`revitcortex-releases`), ora il main è pubblico ma il flow resta:

```bash
./release.ps1 -Version "1.0.26"
gh release create v1.0.26 --repo LuDattilo/revitcortex-releases ./release/*.zip
```

(Verifica `release.ps1` per il flow esatto: in alcune versioni crea solo i pacchetti, in altre fa anche il `gh release create`.)

## Pre-commit checklist

- [ ] Build R25 verde.
- [ ] Build R24 verde.
- [ ] Tool aggiunti/modificati: schema rigenerato (`node server/generate-tool-schemas-csharp.mjs`).
- [ ] `USER_GUIDE.md` aggiornato se nuovi tool.
- [ ] Test unitari passano.

## Pre-release checklist

- [ ] Build R23, R24, R25, R26, R27 tutte verdi.
- [ ] `deploy.ps1` testato per ogni target.
- [ ] CHANGELOG.md aggiornato.
- [ ] Server publish mode coerente (no mix).
- [ ] `gh release create` su repo corretto.

## Avoid

- Non committare con solo build R25 verde.
- Non skippare la rigenerazione di `tool-schemas.txt`.
- Non mischiare publish mode sul server.

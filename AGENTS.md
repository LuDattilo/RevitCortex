# RiveTT — contributor guide

RiveTT supports Autodesk Revit 2026.5+ and 2027 — both run on .NET 10 on Windows
x64, so both build from the same `net10.0-windows` TFM; do not reintroduce
R23–R25 configurations or `net48`/`net8` compatibility branches.

The two targets share one codebase, selected at build time by the
`RevitVersion` MSBuild property (`dotnet build -p:RevitVersion=2026`, default
`2027`; see `build.ps1 -RevitVersion`). It drives both the
`Nice3point.Revit.Api.RevitAPI` package version and the `REVITxxxx_OR_GREATER`
`DefineConstants`. `REVIT2026_OR_GREATER` is unconditional (2026 is the floor);
`REVIT2027_OR_GREATER` is only defined when building the 2027 target. Guard any
API that genuinely doesn't exist in 2026 (e.g. the Coordination Model API,
walls hosted on walls) behind `#if REVIT2027_OR_GREATER`, with an `#else`
branch that reports the feature as unsupported rather than failing to compile
— see `GetCoordinationModelsTool.cs` and `SetWallHostTool.cs` for the pattern.
A single build of the plugin only ever runs against ONE Revit version at a
time: it is not multi-targeted, it is rebuilt per target.

## Architecture

    MCP client
      -> RiveTT.Server (stdio)
      -> Windows named pipe (current user only)
      -> RiveTT.Plugin (Revit ExternalEvent)
      -> CortexRouter
      -> ICortexTool implementations

The C# server is the only server implementation. Everything generated lives under
`dist/` and is never committed: `dist/<year>/plugin` per Revit target, plus
`dist/server` — built ONCE and shared, because the server carries no Revit API
reference. The installer SOURCE is `installer/RiveTT.iss` and is versioned.

The server is published self-contained. Framework-dependent it would need the
.NET 10 runtime under Program Files, and installing that requires local admin —
the one thing the per-user installer exists to avoid. Do not add `PublishTrimmed`:
the MCP SDK and Newtonsoft.Json resolve types by reflection.

## The two-sided contract

The MCP surface (`src/RiveTT.Server/Tools`) and the runtime tools
(`src/RiveTT.Tools`) are separate assemblies that agree only by JSON key
name. Every parameter published by a wrapper MUST be read by the tool it is
forwarded to.

A published parameter nobody reads is worse than a missing one: the caller
believes it took effect. `create_sheet` published `titleBlockId` while the
runtime read `titleBlockTypeId`, so every sheet came out as a bare 210×297 mm
sheet with no frame, silently, and no presentation sheet could be produced at
all. `ServerRuntimeParameterContractTests` now fails the build on that class of
mismatch — do not add a waiver to it without a reason in the table.

When you add or rename a parameter:

- add it on both sides in the same change, or accept the old name as an alias
  in the runtime tool;
- state the unit and the convention in the `[Description]` (mm, degrees,
  absolute vs relative elevation);
- make the response report what was actually applied, not what was requested.

## The write lock

Every Revit session starts read-only. `CortexRouter.Route` refuses any tool whose
`toolReadOnly` classification is false with `PermissionDenied`, before the cache
and before the open-document check, until a human presses *Écriture* in the
**RiveTT** ribbon panel (Add-Ins tab).

Consequences for anything you add here:

- `[ToolSafety(readOnly, destructive)]` is now a permission boundary, not just
  metadata. A write tool marked `readOnly: true` would slip through the lock;
  the registration already traces a mismatch against the name-prefix heuristic,
  so do not silence that trace.
- Never read the lock from a tool, and never write to it: `WriteAccessPolicy.Set`
  belongs to the ribbon. `WriteAccessGateTests` scans the whole of
  `RiveTT.Tools` and fails if a tool calls it.
- `dryRun` is not an exemption. A preview is a tool's own promise; the lock
  cannot depend on 250 implementations keeping it.
- The lock is session state, not document state: `CortexSession.Reinitialize`
  must leave it alone.

## Development rules

- Prefer a dedicated `ICortexTool`; keep `send_code_to_revit` as a fallback.
- Revit API calls must execute through `ExternalEvent`. That context is a valid
  API context and is less restricted than an API *event* handler: switching the
  active document (`OpenAndActivateDocument`) and opening API edit scopes
  (`StairsEditScope`) are supported here, and both were wrongly documented as
  impossible. Before declaring something impossible, check whether the
  restriction applies to API event handlers (Idling, DocumentChanged) or to a
  modal editor — those are different constraints.
- An API edit scope must be started with no transaction open, committed with a
  failure preprocessor (an unhandled warning opens a modal dialog and freezes
  the pipe), and cancelled in a `finally` so Revit never stays in edit mode.
- Writes use a `Transaction`, return a structured `CortexResult`, and must
  not leak exceptions across the router.
- Keep `dryRun` preview behavior where a tool supports it. RiveTT does not
  display confirmation or licensing dialogs.
- Keep the Roslyn sandbox and local audit log intact.
- Use language-independent IDs and `BuiltInCategory` values when possible.
- Resolve free-text parameter names through `ParameterNameResolver` and
  category names through `CategoryResolver`. Never compare a caller's string to
  a localized `Definition.Name` alone: on a French document that silently
  matches nothing.
- Never return an empty value where a name failed to resolve. Report it
  (`unresolvedParameterNames`, `notFoundIds`, `skippedFields[].reason`) with
  suggestions. A silent empty column reads as real data.
- Numeric outputs carry their unit. Revit stores feet, ft² and ft³ whatever the
  project units are; use `ParameterValueFormatter` so a caller cannot mistake
  one for the other.
- Report the localized category name AND `categoryBic` (the `OST_*` code).
  French Revit names the viewport category "Fenêtres " — same label as windows,
  with a trailing space.
- `ToolResponseShaper` must never shape a failure payload, never drop a list
  item, and never recompute a counter from a trimmed list.
- Add or update xUnit coverage for every behavior change.

## Verification

    dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release
    dotnet build .\RiveTT.sln -c Release
    .\build.ps1

The last command builds every Revit target and compiles the per-user installer
into `dist/`. Installation is through `dist/RiveTT-Setup-<version>.exe`, which
requests `asInvoker` and never prompts for elevation.

`build.ps1` is UTF-8 WITH BOM and must stay so: Windows PowerShell 5.1 reads a
BOM-less script as Windows-1252, and the multi-byte characters then decode into
curly quotes that it honours as string delimiters.

Behavior that only a live Revit session can prove (geometry, transactions,
Revit error messages) must still be re-tested manually against a real model —
in both target versions when the change touches anything gated by
`REVIT2027_OR_GREATER` — and the outcome recorded in the commit or pull request that
makes the change. `docs/developpement/CHANGELOG_0.3.0.md` §6 lists the points still
open for live verification; add new ones there as they come up.

`Nice3point.Revit.Api.*` is a compile-only stub — no real `RevitAPI.dll` ships
in the package, so `dotnet test` alone cannot exercise anything that touches
`Autodesk.Revit.DB`/`.UI` types. `RevitApiBootstrap.cs` (in `RiveTT.Tests`)
finds a local Revit install (`C:\Program Files\Autodesk\Revit 2026|2027` by
default, override with the `REVIT_INSTALL_DIR` env var — set it to a directory
without `RevitAPI.dll` to force this off) and redirects assembly resolution to
the real DLLs there. This makes the `Document`/`Application`-typed tests run
for real instead of skipping, on any machine (this one included) that has
Revit installed; `RevitAPIUI.dll` still fails its own native init even then, so
UI-touching tests (`UIApplication`/`UIDocument`, activating a document) stay
skipped everywhere. On a machine without Revit — another dev's machine, GitHub
Actions — detection finds nothing and every one of these tests reports a clean
Skip via `[RequiresRevitDbApiFact]`/`[RequiresRevitApiFact]`, never a Fail. Mark
new Revit-typed tests with one of the two attributes instead of `[Fact]` so
this holds.

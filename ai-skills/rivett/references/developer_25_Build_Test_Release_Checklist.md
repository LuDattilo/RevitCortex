# Build, test, and package checklist

## Pre-commit

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

Confirm that the complete test tree is compiled; do not disable default
`Compile` items to obtain a green build.

## Package

    .\build.ps1

Builds every supported Revit target and compiles the installer. Everything
generated goes to the ignored `dist/` directory:

    dist/2026/plugin/   add-in built against Revit 2026.5
    dist/2027/plugin/   add-in built against Revit 2027
    dist/server/        RiveTT.Server.exe, self-contained, shared by both
    dist/RiveTT-Setup-<version>.exe

The server has no Revit API reference, so it is built once and shared. It is
self-contained on purpose: framework-dependent it would need the .NET 10 runtime
under Program Files, and installing that is the one thing that would demand admin.

`-SkipInstaller` builds the binaries without Inno Setup; `-RevitVersion 2027`
builds a single target (the resulting installer then serves that target only).

## Install

    .\dist\RiveTT-Setup-<version>.exe

Per-user, no elevation, no UAC prompt. Revit does NOT have to be closed for an
update — locked files are parked as `.old-<stamp>` and replaced. It must be closed
to uninstall.

## Release checks

- Revit 2026.5+ and 2027 / .NET 10 / x64 only.
- Generated plugin/server binaries and addin manifest are not committed; `dist/`
  is ignored in full.
- The installer manifest is `asInvoker`. Anything that makes it request elevation
  is a regression, not a detail.
- `build.ps1` stays UTF-8 WITH BOM. Without it PowerShell 5.1 reads the file as
  Windows-1252 and multi-byte characters decode into curly quotes, which it honours
  as string delimiters — that silently stripped `$LASTEXITCODE` out of a guard.
- `docs/utilisation/USER_GUIDE.md` reflects changed user-facing tools.
- `send_code_to_revit` sandbox tests pass.
- No references to the removed TCP, TypeScript, licensing, telemetry, updater,
  Power BI, or multi-version deployment stacks remain in active code.

---
name: rivett
description: Use when working with RiveTT/RiveTT operations, Revit 2027 model automation, MCP tool workflows, or the C# plugin/server. Covers safe writes, send_code_to_revit escalation, IFC, tool development, audit security, and .NET 10 build checks.
---

# RiveTT skill router

Load only the references needed for the current task.

## Always-on rules

1. For Revit model work, read `operator_01_Session_Start_Locale.md` first.
2. For writes, read `operator_03_Destructive_Operations_DryRun.md`; preview with
   `dryRun: true` whenever the tool supports it.
3. `send_code_to_revit` is a last resort; read
   `operator_10_SendCodeToRevit_Escalation.md` before proposing it.
4. C# development targets Revit 2027, .NET 10, and x64 only.
5. Keep named-pipe isolation, audit logging, structured errors, and the Roslyn
   sandbox intact.
6. `execution.toolReadOnly` classifies the tool that answered — it is NOT a
   session lock. `execution.writesAllowed` IS the session lock: every Revit
   session starts read-only, and only a human unlocks it from the RiveTT
   ribbon panel (Add-Ins tab). On `PermissionDenied` with
   `writesAllowed: false`, stop and ask for the unlock — no tool, and no
   `dryRun`, gets past it. `execution.cached: true` means the answer came from
   the cache.
7. Parameter names resolve in English or in the document language. A name that
   resolves to nothing comes back in `unresolvedParameterNames` (or
   `skippedFields[].reason`), never as an empty value — treat an empty column
   without such a report as real data.
8. Numeric parameter values carry `unit` and `internalValue`. Never read a bare
   number as project units.
9. Prefer `categoryBic` (`OST_*`) over the localized category label: Revit FR
   names the viewport category "Fenêtres ", like windows.
10. System types (walls, floors, railings, stairs, title blocks) are not
    loadable families: enumerate with `list_system_types`, duplicate with
    `duplicate_system_type`.

## Routing

| Request | References |
|---|---|
| Session/model discovery | `operator_01`, `operator_02` |
| Parameter changes | `operator_01`, `operator_03`, `operator_04` |
| Health, warnings, clashes | `operator_01`, `operator_05` |
| Views and annotations | `operator_01`, `operator_06` |
| IFC | `operator_01`, `operator_07` |
| Script escalation | `operator_10` |
| New C# tool | `developer_20`, `developer_21`, `developer_23`, `developer_24`, `developer_25` |
| Build or test failure | `developer_25` |
| Security/audit | `developer_24` |

`index_40_Tool_Signature_Index.md` explains where the canonical MCP signatures
live. Do not rely on the removed generated schema catalog.

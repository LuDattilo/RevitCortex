# Bulk Functional Test Results — 2026-04-16

**Target:** Revit 2025 (net8) — Snowdon Towers Sample Architectural (37,848 elements)
**Branch:** `fix/post-v1-review`
**Model:** Snowdon Towers Sample Architectural_luigi.dattilo7VWCL.rvt

## Test Matrix — All PASS

| # | Tool | Result | Notes |
|---|------|--------|-------|
| 1 | `get_project_info` | ✅ PASS | 18 levels, 6 Revit links, 3 phases |
| 2 | `analyze_model_statistics` (compact) | ✅ PASS | 37,848 elements / 1,877 types / 286 families |
| 3 | `ai_element_filter` OST_Walls | ✅ PASS | 1,132 walls; returned 3 with full metadata |
| 4 | `get_element_parameters` wall 619340 | ✅ PASS | 39 parameters returned |
| 5 | `check_model_health` | ✅ PASS | Score 80/100 grade B, 49 warnings |
| 6 | `audit_families` OST_Doors | ✅ PASS | 10 door families, 144 instances |
| 7 | `workflow_model_audit` | ✅ PASS | 11.3s on 37k elements — acceptable |
| 8 | `export_to_excel` OST_Doors | ✅ PASS | .xlsx 17KB created on Desktop |
| 9 | **`send_code_to_revit` (flag OFF)** | ✅ **PASS** | **PermissionDenied — consent gate works** |
| 10 | `send_code_to_revit` "return document.Title;" (flag ON) | ✅ PASS | Returned `Snowdon Towers Sample Architectural_...` |
| 11 | `send_code_to_revit` with `System.IO.File.ReadAllText(...)` | ✅ PASS | **PermissionDenied** — sandbox blocks file I/O |
| 12 | `send_code_to_revit` with `// System.IO` + `// File.ReadAllText` comments + `return 42;` | ✅ PASS | Returned 42 — **comment strip works** |
| 13 | `send_code_to_revit` with `Type.GetType("System.IO.File")` | ✅ PASS | **PermissionDenied** — reflection bypass blocked |

## Audit log verification

`~/.revitcortex/audit.jsonl` confirmed populated with structured entries:
- Success/failure status recorded
- Error codes (`PermissionDenied`) logged
- Code snippets truncated to 200 chars (as designed)
- Timestamps UTC ISO-8601

## Performance observations

- Read-only tools (get_project_info, ai_element_filter, get_element_parameters, audit_families): instant (< 1s)
- `analyze_model_statistics` compact: fast
- `workflow_model_audit` full: **11.3s** on 37k elements — within budget (<30s threshold)
- `export_to_excel` 50 doors with 44 instance params: instant (< 2s)

No bottlenecks detected on Snowdon Towers.

## Bugs / Crashes

**None.** All 13 test tools behaved as expected. Revit did not crash, no stale transactions, no journal errors.

## Verdict

All Critical and Important fixes from the code review verified live on Revit 2025:
- ✅ `EnableCodeExecution` gate enforced at tool boundary (Critical #1)
- ✅ Sandbox catches both direct I/O (`System.IO.File`) and reflection bypass (`Type.GetType`) (Critical #2)
- ✅ Comment-strip works (V2 improvement) — no false positive on commented-out patterns
- ✅ Audit log populated for every code-exec attempt (security/accountability)
- ✅ Installer Defender opt-in — tested via syntax check, behavior confirmed during earlier install run

**Ready to push and merge.**

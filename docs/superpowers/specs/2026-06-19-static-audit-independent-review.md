# RevitCortex - Independent Review of Static Audit Findings

**Date:** 2026-06-19  
**Reviewer:** Codex  
**Source audit reviewed:** `docs/superpowers/specs/2026-06-19-static-audit-findings.md`  
**Scope:** independent static review of representative source files and repo-wide pattern scans. No code changes were made.

---

## Executive assessment

The static audit is substantially credible. Its central claim, that transaction handling is inconsistent across the application, is confirmed by direct source review.

The issue is not just style. Revit transactions that call `Commit()` without installing a failures preprocessor and without checking the returned `TransactionStatus` can fail in two dangerous ways:

1. A warning raised during commit can open a modal Revit dialog on the UI thread and stall the MCP bridge until a user intervenes.
2. Revit can roll back the transaction while the tool still returns `Ok`, leaving the model unchanged while the caller believes the operation succeeded.

The correct pattern already exists in `src/RevitCortex.Tools/Utilities/TransactionFailureHandling.cs` and is used by some hardened tools, but adoption is incomplete.

---

## Evidence reviewed

### Transaction handling

A repo-wide static scan outside tests found approximately:

| Metric | Count |
|---|---:|
| Files containing transactions | 117 |
| `new Transaction(...)` sites | 227 |
| `new TransactionGroup(...)` sites | 8 |
| `TransactionFailureHandling.SuppressWarnings(...)` calls | 17 |
| Explicit `TransactionStatus.Committed` checks | 16 |
| Bare `tx.Commit();`-style lines | 208 |

These are text-level counts, not a full semantic proof. They are still strong evidence that the audit's systemic transaction finding is real and broader than a few isolated tools.

Confirmed contrast:

- Hardened pattern exists in `src/RevitCortex.Tools/Elements/SetElementParametersTool.cs`: calls `SuppressWarnings(tx)` and fails if `tx.Commit() != TransactionStatus.Committed`.
- Unhardened pattern exists in `src/RevitCortex.Tools/Views/CreateViewTool.cs`: starts a transaction and calls `tx.Commit()` without suppression or status validation.
- High-blast-radius code execution path in `src/RevitCortex.Tools/CodeExecution/RoslynExecutor.cs` and `CodeDomExecutor.cs` uses transactions/transaction groups without equivalent validation.

### Test coverage shape

Existing source tests lock the helper and a small set of adopted tools:

- `src/RevitCortex.Tests/Tools/TransactionFailureHandlingSourceTests.cs`
- `src/RevitCortex.Tests/Tools/IfcRebuildTransactionGroupSourceTests.cs`
- `src/RevitCortex.Tests/Tools/DryRunUniformitySourceTests.cs`

These tests explain why the known hardened files stayed correct, but they do not enforce the pattern repo-wide. The current regression protection is localized.

---

## Confirmed high-priority findings

### P1 - Systemic transaction safety debt

**Status:** Confirmed.

Many write tools call `Commit()` without:

- `TransactionFailureHandling.SuppressWarnings(tx)`
- checking `Commit() != TransactionStatus.Committed`
- returning `CortexResult.Fail(CortexErrorCode.TransactionFailed, ...)` on rollback

This affects the same folders named by the source audit, and the independent scan found additional candidates outside that list, including other `Project`, `Rebar`, `PowerBiLive`, and element creation tools.

**Interpretation:** The source audit should be treated as a confirmed minimum backlog, not a complete inventory.

### P1 - CodeExecution transaction handling

**Status:** Confirmed.

`send_code_to_revit` has the highest blast radius because it executes user-provided C# through `RoslynExecutor` / `CodeDomExecutor`. Both use transactions or transaction groups without failure suppression or commit/assimilate validation.

**Recommendation:** Fix this first.

### P1 - Rebar dryRun contract is missing

**Status:** Confirmed.

The source audit's claim that these Rebar files skip `ToolHelpers.GetDryRun` is accurate:

- `src/RevitCortex.Tools/Rebar/RebarCreationTools.cs`
- `src/RevitCortex.Tools/Rebar/RebarSystemTools.cs`
- `src/RevitCortex.Tools/Rebar/FabricReinforcementTools.cs`

The files open transactions after confirmation and do not read `dryRun`. Therefore `{ "dryRun": true }` cannot provide a preview. This is more serious than a consistency issue because callers may assume preview-first behavior from the broader tool contract.

---

## Confirmed distinct bugs

### P1/P2 - PowerBI override registry can drift from view state

**Status:** Confirmed.

`PbiActionEventHandler.ExecuteReset` clears `_registry` before validating commit. If commit rolls back or throws after registry clear, registry and actual view overrides can diverge.

Related transaction sites in the same handler also use unchecked commits for isolate, color override, reset, and create-view flows.

### P2 - NullRef in `SectionBoxFromSelectionTool`

**Status:** Confirmed.

When `duplicateView=false`, the tool uses:

```csharp
targetView = doc.ActiveView as View3D
    ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
        .FirstOrDefault(v => !v.IsTemplate)!;
```

If no non-template 3D view exists, `targetView` is null and `targetView.SetSectionBox(...)` throws instead of returning a structured `CortexResult` failure.

### P2 - `LocaleDetector` null guard

**Status:** Confirmed.

`LocaleDetector.Detect` uses `param.Definition.Name` without a null guard. Elsewhere the codebase commonly uses `Definition?.Name`. This should return the default locale instead of risking a NullRef.

### P2 - Orphan detail lines in `CreateDimensionsTool`

**Status:** Plausible and code-supported.

The point-to-point dimension branch creates two detail curves as references. Cleanup exists only when `NewDimension(...)` returns null. If an exception occurs after the detail curves are created but before cleanup, the temporary detail curves can remain.

### P2 - `WorkflowRoomDocumentationTool` unbounded `createdViews`

**Status:** Confirmed.

`failures` is capped with `Take(50)`, but `createdViews` is returned unbounded. Large room models can produce unnecessarily large MCP payloads.

### P2 - PowerBI element export silently drops rows

**Status:** Confirmed.

`PowerBiElementExporter.BuildRow` catches all exceptions and returns null. The caller skips null rows, and `PbiPublishElementsTool` does not expose attempted-vs-exported diagnostics. This can hide partial export failures.

### P2 - `ifc_validate_request` reports valid after header-read failure

**Status:** Confirmed.

`IfcValidateRequestTool` catches header-read failures and still returns `valid = true`. It may be acceptable to tolerate a missing schema, but corrupt/unreadable files should not be reported as cleanly valid without diagnostics.

---

## Findings to downgrade or clarify

### ManualResetEvent disposal in `ToolExecutionHandler`

**Audit severity:** P1  
**Independent severity:** P3 or low P2

The `ManualResetEvent` is not disposed, so the finding is technically real. However, `ToolExecutionHandler` appears to be created once during plugin startup, not repeatedly. This is cleanup worth doing, but it does not appear comparable to transaction rollback or modal-dialog risks.

### Settings port validation

**Audit severity:** P2  
**Independent severity:** P2, wording softened

`GeneralSettingsPage` validates range only, not whether the port is already in use. `SocketService.Start()` can throw from `TcpListener.Start()`. However, `ToggleConnection` catches exceptions and returns `Result.Failed`, so "silent startup failure" may be too strong depending on Revit's command failure UX. The underlying robustness issue is real.

### `DocumentCapabilities.SharedParameterNames`

**Audit severity:** P3  
**Independent severity:** P3 cleanup

Confirmed as a dead or unused field from source scan. No behavioral urgency.

### `find_untagged_elements` duplicate category payload

**Audit severity:** P3 benign  
**Independent status:** Confirmed benign

The server wrapper intentionally sends both `categories` and `category` for bridge compatibility. Do not fix unless the bridge contract changes.

---

## Additional observations beyond the source audit

The source audit is not exhaustive. Independent scanning found transaction candidates outside the listed folders/lines, including:

- `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs`
- multiple `src/RevitCortex.Tools/Project/*` tools
- `src/RevitCortex.Tools/Rebar/RebarAdvancedTools.cs`
- `src/RevitCortex.Tools/Rebar/RebarSettingsTools.cs`
- several element creation tools not highlighted in the source report

This means a fix strategy based only on the listed 128 findings will likely leave the same defect class behind.

---

## Recommended remediation plan

1. **Fix `CodeExecution` first.** Harden `RoslynExecutor` and `CodeDomExecutor` transaction and transaction-group paths.
2. **Introduce or standardize a transaction helper.** A helper should start the transaction, suppress warnings, execute the body, validate commit status, and return a structured `CortexResult` failure on rollback.
3. **Sweep write tools by folder.** Prioritize `Parameters`, `Views`, `Annotations`, `Sheets`, `LinkedFiles`, `Rebar`, `Project`, and `PowerBiLive`.
4. **Add repo-wide source tests.** Tests should fail on new bare `Commit()` sites in write-tool folders unless explicitly allowlisted.
5. **Fix dryRun gaps in Rebar.** Bring Rebar mutators in line with `ToolHelpers.GetDryRun(input)` and preview-first behavior.
6. **Fix confirmed P2 bugs.** Start with `SectionBoxFromSelectionTool`, `LocaleDetector`, `PowerBiElementExporter`, `IfcValidateRequestTool`, and `WorkflowRoomDocumentationTool`.
7. **Then handle cleanup items.** Dispose `ToolExecutionHandler` resources, remove dead fields, and improve port conflict UX.

---

## Verification requirements for implementation

Per project instructions, after any C# file change run both:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

For tests:

```powershell
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Do not use a green R25 build as evidence that R24 is safe.

---

## Final judgment

The source audit is directionally sound and identifies real operational risk. The highest-value fix is not to patch individual call sites manually forever, but to make safe transaction handling the default path and enforce it with source-level tests. The original report should be used as a starting backlog, then expanded with an automated scan to avoid leaving unlisted bare commits in place.

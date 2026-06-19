# Code Signing for Distribution — Design Spec

**Status:** Deferred (future implementation)
**Date:** 2026-06-19
**Trigger:** Windows 11 Smart App Control (SAC) in enforce mode blocks the unsigned
`RevitCortex.Server.dll`, breaking the MCP handshake. Self-signed certificates do
NOT satisfy SAC — it requires a chain up to a Microsoft Trusted Root. This spec
captures the production-grade fix to enable selling / distributing RevitCortex to
machines we do not control.

---

## Problem Statement

The release package ships unsigned PE binaries:

- `release/server/RevitCortex.Server.exe` + all its self-contained `.dll`s (win-x64)
- `release/plugin/Rxx/*.dll` (the Revit add-in assemblies, R23–R27)

On a Windows 11 machine with Smart App Control in **enforce** mode (`On`), loading
the unsigned server DLL fails with:

```
System.IO.FileLoadException: Could not load file or assembly
'…\.revitcortex\server\RevitCortex.Server.dll'.
An Application Control policy has blocked this file. (0x800711C7)
```

CodeIntegrity/Operational event: file "did not meet the Enterprise signing level
requirements or violated code integrity policy".

Relying on SAC's cloud reputation is not viable for new or privately-distributed
builds. **Signing every PE the OS can load, with a trusted-root certificate, is the
only robust fix.** This is a hard requirement the day RevitCortex is installed on
machines we do not own (paying customers, colleagues, clients).

### Non-goals

- This spec does NOT cover the local dev unblock (disabling SAC). That is a
  permanent, per-machine, irreversible action and is out of scope for a
  distributable product.
- This spec does NOT cover WDAC/Intune enterprise policies that a customer's IT
  department may push independently of SAC. Those are the customer's responsibility;
  a trusted-root signature is the prerequisite that lets such policies allow our code.

---

## Chosen Approach: Microsoft Trusted Signing

Two viable certificate sources exist; we commit to **Microsoft Trusted Signing**
(formerly Azure Code Signing).

### Why Trusted Signing over an OV/EV CA certificate

| Factor | Trusted Signing | OV/EV CA cert (DigiCert/Sectigo/…) |
|---|---|---|
| Cost | ~10 USD/month (Basic SKU) | OV ~250–400 €/yr, EV ~400–600 €/yr |
| Hardware token | None — keys live in Azure | Mandatory HSM/USB token (since Jun 2023) |
| CI / build automation | Native (`signtool` + Azure dlib) | Token PIN per signature — awkward in CI |
| SAC trusted-root chain | Yes (designed for SmartScreen/SAC) | Yes |
| Cert lifetime | Short-lived (~3 days), auto-rotated | 1–3 year cert on a physical token |

Trusted Signing wins on cost, on no-physical-token, and on clean automation inside
our existing `build-release.ps1` / `release.ps1` pipeline. EV's only edge —
immediate SmartScreen reputation — does not justify the token-management overhead
for our distribution scale.

### Prerequisites (one-time, blocking on lead time)

1. Azure subscription (GPA Ingegneria Srl — likely already exists).
2. Create a **Trusted Signing Account** resource in Azure.
3. Create a **Certificate Profile** of type **Public Trust** (software distributed
   to machines we do not control).
4. **Organization identity validation** by Microsoft (Dun & Bradstreet / legal
   registries). Lead time ~1–5 business days; org should have ≥3 years verifiable
   history or supply extra documentation. **This is the critical-path item — start
   it well before any release that needs signing.**
5. Assign the `Trusted Signing Certificate Profile Signer` role to the build
   identity (service principal for CI, or the developer account for local builds).

---

## Architecture

The signing step is a new stage inserted into the existing release pipeline. No
change to the plugin/server source code is required — signing operates on built
artifacts.

```
build-release.ps1
  [1/4] build plugin (R23–R27)      ──┐
  [2/4] build MCP server (self-cont.) ─┤
  [3/4] copy support files            │
  >>> NEW [3.5/4] SIGN all PE files <<<┘   ← inserted here, before ZIP
  [4/4] create ZIP
```

### Component: `Sign-ReleaseArtifacts.ps1` (new, under repo root or `distribution/lib/`)

Single-purpose, independently testable helper.

- **What it does:** Enumerates every `.exe` and `.dll` under `release/`, then signs
  each with `signtool` using the Trusted Signing dlib.
- **How it is used:** Called by `build-release.ps1` after step [3/4] and before the
  ZIP is created. Guarded by a `-Sign` switch (or auto-detected from the presence of
  Trusted Signing config) so unsigned local dev builds still work.
- **What it depends on:** `signtool.exe` (Windows SDK), the Azure dlib
  (`Azure.CodeSigning.Dlib`), and a `metadata.json` describing the Trusted Signing
  endpoint + account + certificate profile. Azure auth via service principal env
  vars (CI) or `az login` (local).

#### Signing command shape (per file)

```powershell
signtool sign `
  /v /fd SHA256 `
  /tr "http://timestamp.acs.microsoft.com" /td SHA256 `
  /dlib "<path>\Azure.CodeSigning.Dlib.dll" `
  /dmdf "<path>\metadata.json" `
  "<file>"
```

`metadata.json` carries `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`.

### Files that MUST be signed

- `release/server/RevitCortex.Server.exe`
- **all** `release/server/*.dll` (self-contained runtime DLLs included — SAC loads them)
- **all** `release/plugin/R23/*.dll` … `release/plugin/R27/*.dll`

Timestamping (`/tr`) is mandatory so signatures stay valid after the short-lived
Trusted Signing certificate rotates.

---

## Data Flow

1. CI/dev triggers `release.ps1 -Version X.Y.Z`.
2. `release.ps1` calls `build-release.ps1 -Version X.Y.Z` (add `-Sign` once signing
   is live).
3. `build-release.ps1` builds artifacts, copies support files, then invokes
   `Sign-ReleaseArtifacts.ps1` over `release/`.
4. The signing helper authenticates to Azure Trusted Signing and signs each PE
   in place.
5. `build-release.ps1` zips the now-signed `release/` tree.
6. `release.ps1` publishes the ZIP to the GitHub release and updates the manifest
   (unchanged).
7. End user downloads, installs; SAC validates the trusted-root chain and allows
   the server to load.

---

## Error Handling

- **Missing signtool / dlib / metadata:** `Sign-ReleaseArtifacts.ps1` fails fast
  with a clear message naming the missing dependency. Build does NOT silently ship
  unsigned binaries when `-Sign` is requested.
- **Azure auth failure:** surface the Azure error; do not retry blindly. Distinguish
  "not logged in" from "role not assigned".
- **A file fails to sign:** abort the whole build (an unsigned DLL in an otherwise
  signed package still gets blocked by SAC — partial signing is worse than none).
- **Verification gate:** after signing, run `signtool verify /pa /all <file>` over
  every PE and fail the build if any file is unsigned or its chain does not verify.
  This is the stop-loss that guarantees we never publish a half-signed package.
- **Local dev builds without `-Sign`:** behave exactly as today (unsigned), so the
  signing dependency never blocks day-to-day development.

---

## Testing Strategy

- **Unit-ish (PowerShell):** test that `Sign-ReleaseArtifacts.ps1` enumerates the
  correct file set (every `.exe`/`.dll` under `release/`, none missed) given a fake
  release tree. No real Azure call.
- **Integration (manual, gated):** run a real signed build against a Trusted Signing
  test profile; assert `signtool verify /pa /all` passes on every artifact.
- **End-to-end (the real acceptance test):** install the signed package on a Windows
  11 machine with SAC in **enforce** mode and confirm the MCP handshake (`say_hello`)
  succeeds where the unsigned build failed with `0x800711C7`.

---

## Cost & Operational Notes

- Recurring: ~10 USD/month for the Trusted Signing Basic SKU.
- Certificate rotation is automatic and invisible — timestamping makes already-signed
  binaries stay valid.
- The Azure identity validation is the only slow, manual, blocking step. Treat it as
  a procurement lead-time item, not an engineering task.
- Keep signing **out** of the local dev loop (`-Sign` opt-in) so the 5-target build
  matrix (R23–R27) stays fast for development.

---

## Open Questions (resolve before implementation)

1. Does GPA already have an Azure subscription with billing we can attach a Trusted
   Signing account to?
2. CI vs local signing — will releases be cut from a developer machine (current
   `release.ps1` flow) or moved to GitHub Actions? This decides service-principal vs
   interactive auth.
3. Is the original blocking policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` actually
   SAC, or a separate WDAC/Intune policy? A trusted-root signature fixes SAC; an
   enterprise WDAC allowlist may still need the customer's IT to trust our publisher.
   Confirm before promising customers a turnkey install.

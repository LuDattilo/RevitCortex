# Complete Structural Steel API — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a complete RevitCortex MCP surface for Revit structural steel fabrication workflows — discovery/capabilities, generic & typed connections, connection-type/approval administration, fabrication metadata/materials/warnings, solid & instance-void cuts, and provider/extension reporting — as 57 tools across Revit 2023→2027.

**Architecture:** A new first-class `StructuralSteel` tool category. Each tool is an `ICortexTool` in `src/RevitCortex.Tools/StructuralSteel/`, auto-registered by reflection (`CortexRouter.RegisterToolsFromAssembly` — zero manual wiring). Each gets a thin server-side MCP wrapper (`[McpServerTool]` static method in `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`) forwarding via `revit.ExecuteAsync`. Shared logic (element/handler/type resolution, mm⇄ft, connection-input & cut-spec & enum parsing, GUID parsing, version-gated adapters, provider/capability detection) lives in `StructuralSteelToolHelpers`. Provider-dependent and version-gated features return a structured `Fail` (provider-unavailable / min-or-max-version) rather than crashing or faking success.

**Tech Stack:** C# multi-target (net48 for R23/R24, net8.0-windows for R25/R26, net10.0-windows for R27), `Autodesk.Revit.DB.Steel` + `Autodesk.Revit.DB.Structure` (StructuralConnection*) + `Autodesk.Revit.DB` (SolidSolidCutUtils, InstanceVoidCutUtils), Newtonsoft.Json, ModelContextProtocol SDK, xUnit. No `ImplicitUsings` — explicit usings in every file. No `record`/`init`/`Index`/`Range`/`GetValueOrDefault` (net48 breaks).

---

## Design source

Implements the approved spec `docs/superpowers/specs/2026-05-30-structural-steel-api-complete-design.md`. Read it first.

## ⚠️ API VERIFICATION MANDATE (read before every task)

The Revit steel API claims in the spec (method families, signatures, the R27 `AddElementsToCustomConnection` change, provider/registry types) are **NOT to be trusted** — they MUST be verified by reflecting the actual Nice3point ref assemblies before use. This is the dominant lesson from the just-completed rebar feature, where ~half the plan's API assumptions were wrong (e.g. `RebarCouplerType`/`RebarSpliceType`/`RebarCrankType` did not exist as element classes, `Rebar.SetHostId` was 2-arg `(Document, ElementId)`, `BarTerminationsData` used settable properties not a `SetHook` method, `ConvertRebarInSystemToRebars` was static, `ReinforcementUtils` had no propagation method).

**Every implementer task below instructs: reflect first, then code.** Ref DLLs are at:
`%USERPROFILE%\.nuget\packages\nice3point.revit.api.revitapi\<ver>\ref\{net48|net8.0|net10.0}\RevitAPI.dll`
for versions **2023.1.90 / 2024.3.40 / 2025.4.50 / 2026.4.10 / 2027.0.20**. Use a throwaway `MetadataLoadContext` probe (a temp console project outside the repo, removed afterward) to dump the real constructors/methods/properties/enums of every steel type a task touches, OR run the multi-target builds and let the compiler errors drive corrections. When an API differs from the spec: **adapt only the offending call site, preserve the tool's name/inputs/return contract, and surface genuine API absence as a structured `Fail`/`MinVersionError`/provider-unavailable error.** Never guess repeatedly; never fake success; never silently drop a provided input (surface it in a `warnings[]` array).

## Non-negotiable project rules (apply to EVERY task)

1. Every plugin tool returns `CortexResult<object>` — never throws to the caller, never a raw string.
2. Every model-changing tool calls `session.RequestConfirmation("<verb>", count)` BEFORE opening its `Transaction`; on `false` return `CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user")`.
3. All ids validated BEFORE the transaction opens.
4. MCP inputs/outputs use **millimetres** and **degrees**; Revit feet/radians never user-facing. `1 ft = 304.8 mm`.
5. Language-independent identifiers only: element ids, `OST_*`, type ids, enum names as strings, connection/fabrication **GUIDs as strings**, failure ids. Never read/write by localized display name.
6. Read tools MUST start with `get_`/`list_`/`analyze_`/`check_`. Write tools MUST NOT.
7. After editing any C# file build BOTH `Debug R25` and `Debug R24`. A green R25 build does NOT prove R24 compiles. (RevitCortex.Tools errors can be masked by a green Plugin build — build the Tools csproj or run the test project.)
8. `ElementId`: use `ToolHelpers.ToElementId(long)` and `ToolHelpers.GetElementIdValue(...)` (they wrap the R2023 `int` vs R2024+ `long` difference). Never hand-roll `new ElementId(...)`.
9. **No raw `IntPtr` parameter buffers from MCP input** (spec decision). Detailed/custom connection buffer APIs are read-summary only.
10. **No silent drops:** a provided-but-unsupported field → `warnings[]` or structured `Fail`, never ignored.

## Build / test commands

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R27" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
node server/generate-tool-schemas-csharp.mjs
```
Scope a test class with `--filter "FullyQualifiedName~SteelHelpersParsingTests"`. (R27 needs the .NET 10 SDK; a `NETSDK1045` failure is an environment gap, not a code error — note and continue.)

## Reference: the proven patterns from the rebar feature (mirror these exactly)

The rebar feature (just merged-ready on branch `feat/rebar-api`) established the exact conventions this plan reuses. Read these real files for the live pattern:
- `src/RevitCortex.Tools/Rebar/RebarToolHelpers.cs` — the helper shape `StructuralSteelToolHelpers` mirrors (pure parsers + `Require*` resolvers returning `(T?, CortexResult<object>?)`, `MinVersionError`).
- `src/RevitCortex.Tools/Rebar/RebarDiscoveryTools.cs` — read-tool skeleton.
- `src/RevitCortex.Tools/Rebar/RebarCreationTools.cs` — write-tool skeleton (RequireDocument→validate→RequestConfirmation→`using var tx`→commit/rollback) + the `#if REVIT2026_OR_GREATER` version-fork style.
- `src/RevitCortex.Server/Tools/RebarTools.cs` — `[McpServerToolType]` wrapper pattern.
- `src/RevitCortex.Tests/Rebar/RebarHelpersParsingTests.cs`, `RebarServerContractTests.cs`, `RebarServerForwardingSourceTests.cs` — the 3 no-Revit test patterns.

**Hard test constraint (from rebar):** a plain xUnit `[Fact]` that invokes any method whose body references `Autodesk.Revit.DB.*` throws `FileNotFoundException: RevitAPI` outside a Revit install (reference-only NuGets). So unit tests cover ONLY: pure parsers with no Revit type in their body, reflection over server-wrapper signatures, and source-text assertions. Everything else is covered by the manual Revit smoke checklist. Do NOT write `[Fact]`s that instantiate steel tools and call `Execute`, or that invoke helper methods returning/taking Revit types.

---

## Standard skeletons (every tool follows one of these)

Read tool:
```csharp
public CortexResult<object> Execute(JObject input, CortexSession session)
{
    var (doc, error) = ToolHelpers.RequireDocument(session);
    if (error != null) return error;
    try { /* collect, shape (maxResults/summaryOnly), return Ok */ }
    catch (Exception ex) { return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"...: {ex.Message}"); }
}
```

Write tool:
```csharp
public CortexResult<object> Execute(JObject input, CortexSession session)
{
    var (doc, error) = ToolHelpers.RequireDocument(session);
    if (error != null) return error;
    // validate ids / parse inputs  -> Fail(InvalidInput/ElementNotFound) on bad input, BEFORE tx
    // (capability/provider/version gate -> structured Fail if unavailable)
    if (input["dryRun"]?.Value<bool?>() == true) return CortexResult<object>.Ok(new { dryRun = true, /* preview */ });
    if (!session.RequestConfirmation("<verb>", count))
        return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
    using var tx = new Transaction(doc!, "RevitCortex: <Action>");
    tx.Start();
    try { /* mutate */ tx.Commit(); return CortexResult<object>.Ok(new { ... }); }
    catch (Exception ex) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"...: {ex.Message}"); }
}
```

---

## File Structure

### New plugin tool files (`src/RevitCortex.Tools/StructuralSteel/`)

| File | Responsibility |
|------|---------------|
| `StructuralSteelToolHelpers.cs` | Shared static helpers: doc/element/handler/type/approval resolution, mm⇄ft, connection-input & cut-spec & enum & GUID parsing, `RequireSteelCandidate`, provider/capability detection, version-gated custom-connection adapter, `MinVersionError`, `ProviderUnavailableError`. |
| `StructuralSteelDiscoveryTools.cs` | Module 1 (15 read tools): `get_structural_steel_api_capabilities`, `list_steel_connection_handlers`, `list_steel_connection_types`, `list_steel_connection_handler_types`, `list_steel_approval_types`, `list_steel_connection_providers`, `get_steel_connection_data`, `get_steel_connection_type_data`, `get_steel_connection_settings`, `get_steel_element_properties`, `get_steel_external_id_map`, `get_steel_material_links`, `get_steel_element_warnings`, `get_steel_cut_data`, `analyze_structural_steel_model`. |
| `StructuralSteelConnectionTools.cs` | Module 2 (9 write tools): `create_steel_connection`, `create_generic_steel_connection`, `modify_steel_connection_inputs`, `set_steel_connection_type`, `set_steel_connection_approval`, `set_steel_connection_status`, `set_steel_connection_disconnected`, `set_steel_connection_default_order`, `delete_steel_connection`. |
| `StructuralSteelConnectionTypeTools.cs` | Module 3 (6 write + 3 read): `create_steel_connection_handler_type`, `create_default_steel_connection_handler_type`, `create_steel_structural_connection_type`, `set_steel_connection_type_family_symbol`, `manage_steel_approval_type`, `manage_custom_steel_connection_type` (R27-gated); `get_steel_connection_input_points`, `get_steel_connection_applicability`, `get_steel_connection_validation`. |
| `StructuralSteelFabricationTools.cs` | Module 4 (9 write + 4 read): `add_steel_fabrication_info`, `attach_steel_fabrication_link`, `remove_steel_fabrication_link`, `register_steel_material`, `post_steel_warning`, `remove_steel_warning`, `clear_steel_warnings`, `flush_steel_warnings`, `mark_steel_element_changed`; `get_steel_fabrication_unique_id`, `get_steel_revit_element_by_fabrication_id`, `get_steel_external_material`, `get_steel_warning_counts`. |
| `StructuralSteelCutTools.cs` | Module 5 (5 write + 3 read): `add_steel_solid_cut`, `remove_steel_solid_cut`, `set_steel_solid_cut_face_splitting`, `add_steel_instance_void_cut`, `remove_steel_instance_void_cut`; `get_solid_cut_relationships`, `get_instance_void_cut_relationships`, `check_steel_cut_eligibility`. |
| `StructuralSteelProviderTools.cs` | Module 6 (3 read tools): `get_structural_connection_provider_registry`, `get_structural_connection_provider_data`, `get_structural_connection_validation_info`. |

### New server wrapper file

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Server/Tools/StructuralSteelTools.cs` | `[McpServerToolType]` static class, one `[McpServerTool]` per plugin tool, forwarding via `revit.ExecuteAsync`. |

### New test files (`src/RevitCortex.Tests/StructuralSteel/`)

| File | Responsibility |
|------|---------------|
| `SteelHelpersParsingTests.cs` | No-Revit unit tests for the pure parsers in `StructuralSteelToolHelpers` (connection-input spec, cut spec, enum parse, GUID parse, mm⇄ft) — extracted Revit-free. |
| `StructuralSteelServerContractTests.cs` | Reflection over `StructuralSteelTools.cs` method signatures/descriptions. |
| `StructuralSteelServerForwardingSourceTests.cs` | Source-text + reflection: every wrapper forwards via its own declared name; names map to loadable plugin tools; unique+snake_case. (Mirror `RebarServerForwardingSourceTests` — including the robust subset-direction and avoiding strict counts, per the rebar ReflectionTypeLoadException lesson.) |

### Modified files

| File | Change |
|------|--------|
| `src/RevitCortex.Plugin/CortexRouter.cs` | (Read prefixes already include `get_`/`list_`/`analyze_`/`check_`.) **Verify only** — no change unless a new read prefix is introduced (it is not). |
| `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` | Bump `ToolCount_MatchesExpected` threshold as tools land (baseline before steel: ≥195 with rebar — but steel branches off `main`, so the steel-branch baseline is the pre-rebar number; see Task 1 Step 0). |
| `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs` | Add `[InlineData]` rows for representative steel read + write tools. |
| `docs/USER_GUIDE.md` | New "Structural Steel" section. |
| `tool-schemas.txt` | Regenerate via `node server/generate-tool-schemas-csharp.mjs`. |
| `WORKFLOWS.md` | Steel workflows + operational warning. |

**Branch note:** this feature lives on `feat/structural-steel`, branched from `main` (NOT from the rebar branch). So the registration-count baseline on this branch is `main`'s count (the rebar tools are NOT present here). **Task 1 Step 0 establishes the actual baseline by reading the current `ToolCount_MatchesExpected` value on this branch**, and every subsequent bump is relative to that. Do not assume 195.

---

## Implementation order (8 steps, each builds + tests before the next)

1. **Step 1** — Shared helpers + Module 1 discovery (Tasks 1–3): helpers + parser tests, 15 read tools, server wrappers + contract test, registration/read-only coverage.
2. **Step 2** — Connection read tools are in Module 1; **Module 2 generic+typed connection creation & mutation** (Task 4).
3. **Step 3** — folded into Task 4 (creation + safe input mutation are one coherent file).
4. **Step 4** — **Module 3 connection-type & approval administration** (Task 5).
5. **Step 5** — **Module 4 fabrication metadata, materials, warnings** (Task 6).
6. **Step 6** — **Module 5 solid & instance-void cuts** (Task 7).
7. **Step 7** — **Module 6 provider/capability reporting** (Task 8).
8. **Step 8** — forwarding source test, full multi-target build + schema regen + final count, docs (Tasks 9–11).

---

## Task 1: StructuralSteelToolHelpers + parser tests

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelToolHelpers.cs`
- Test: `src/RevitCortex.Tests/StructuralSteel/SteelHelpersParsingTests.cs`

The helper splits into **pure parsers** (no Revit type in the body — unit-testable) and **Revit-dependent resolvers** (need a `Document`; bodies reflection-verified against the real API). We TDD the pure half. The pure parsers below are concrete and final; the resolver SIGNATURES are fixed (later tasks call them verbatim) but their BODIES must be confirmed against the reflected API (the `Require*`/`Resolve*` patterns mirror `RebarToolHelpers`).

- [ ] **Step 0: Establish the registration-count baseline on THIS branch**

This branch (`feat/structural-steel`) is off `main`, which does NOT contain the rebar tools. Read the current value:
Run: `Select-String -Path src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs -Pattern "AllToolTypes.Count >="`
Record the number (call it `BASE`). Every count bump in later tasks is `BASE + <cumulative steel tools>`. (If `main` has the documented 133 baseline, BASE = 133; verify, don't assume.)

- [ ] **Step 1: Write the failing parser tests**

```csharp
using System;
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.StructuralSteel;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

public class SteelHelpersParsingTests
{
    [Fact]
    public void ToMm_And_FromMm_RoundTrip()
    {
        Assert.Equal(304.8, StructuralSteelToolHelpers.ToMm(1.0), 6);
        Assert.Equal(1.0, StructuralSteelToolHelpers.FromMm(304.8), 6);
    }

    [Fact]
    public void ParseGuid_Valid_And_Invalid()
    {
        var ok = StructuralSteelToolHelpers.ParseGuid("00000000-0000-0000-0000-000000000000", "fabricationGuid", out var err1);
        Assert.Null(err1);
        Assert.Equal(Guid.Empty, ok);

        StructuralSteelToolHelpers.ParseGuid("not-a-guid", "fabricationGuid", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("fabricationGuid", err2);
    }

    [Fact]
    public void ParseEnum_Valid_And_Invalid()
    {
        var ok = StructuralSteelToolHelpers.ParseEnum<SteelInputActionProbe>("AddElementIds", "action", out var err1);
        Assert.Null(err1);
        Assert.Equal(SteelInputActionProbe.AddElementIds, ok);

        StructuralSteelToolHelpers.ParseEnum<SteelInputActionProbe>("nope", "action", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("action", err2);
        Assert.Contains("AddElementIds", err2); // lists valid values
    }

    [Fact]
    public void ParseConnectionInputAction_MapsSnakeCase()
    {
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.AddElementIds,
            StructuralSteelToolHelpers.ParseConnectionInputAction("add_element_ids", out var e1)); Assert.Null(e1);
        Assert.Equal(StructuralSteelToolHelpers.ConnectionInputAction.RemoveReferences,
            StructuralSteelToolHelpers.ParseConnectionInputAction("remove_references", out var e2)); Assert.Null(e2);
        StructuralSteelToolHelpers.ParseConnectionInputAction("banana", out var e3);
        Assert.NotNull(e3);
        Assert.Contains("action", e3);
    }

    [Fact]
    public void ParseLongArray_ReadsNumbers_AndRejectsNonNumeric()
    {
        var ids = StructuralSteelToolHelpers.ParseLongArray((JArray)JArray.Parse("[12345, 12346]"), "elementIds", out var err);
        Assert.Null(err);
        Assert.Equal(new long[] { 12345, 12346 }, ids);

        StructuralSteelToolHelpers.ParseLongArray((JArray)JArray.Parse("[\"x\"]"), "elementIds", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("elementIds", err2);
    }
}

// Revit-free enum to exercise the generic parser.
public enum SteelInputActionProbe { AddElementIds, RemoveElementIds }
```

- [ ] **Step 2: Run, verify FAIL** — `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~SteelHelpersParsingTests"` → FAIL (`StructuralSteelToolHelpers` missing).

- [ ] **Step 3: Create `StructuralSteelToolHelpers.cs`**

The pure parsers below are final. For the Revit-dependent resolvers, KEEP THE SIGNATURES EXACTLY (later tasks call them) and fill the bodies after reflecting the real API — the bodies shown are the intended shape; correct any API member that reflection shows differs.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>
/// Shared helpers for all StructuralSteel tools. Pure parsers carry no Document dependency and are
/// unit-tested in SteelHelpersParsingTests. Revit-dependent resolvers require a Document; their bodies
/// are verified against the reflected Nice3point ref API (see the plan's verification mandate).
/// </summary>
public static class StructuralSteelToolHelpers
{
    public const double MmPerFoot = 304.8;
    public static double ToMm(double feet) => feet * MmPerFoot;
    public static double FromMm(double mm) => mm / MmPerFoot;

    // ── Pure: enum parse ─────────────────────────────────────────────────────
    public static TEnum ParseEnum<TEnum>(string? value, string fieldName, out string? error)
        where TEnum : struct, Enum
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"'{fieldName}' is required. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
            return default;
        }
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        error = $"Invalid '{fieldName}' = '{value}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}";
        return default;
    }

    // ── Pure: GUID parse ─────────────────────────────────────────────────────
    public static Guid ParseGuid(string? value, string fieldName, out string? error)
    {
        error = null;
        if (Guid.TryParse(value, out var g)) return g;
        error = $"Invalid '{fieldName}' = '{value}'. Expected a GUID like 00000000-0000-0000-0000-000000000000.";
        return Guid.Empty;
    }

    // ── Pure: long-array parse (element ids) ─────────────────────────────────
    public static long[] ParseLongArray(JArray arr, string fieldName, out string? error)
    {
        error = null;
        var outList = new List<long>();
        foreach (var t in arr)
        {
            var v = t.Type == JTokenType.Integer ? t.Value<long?>() : null;
            if (v == null) { error = $"'{fieldName}' must be an array of integer element ids."; return outList.ToArray(); }
            outList.Add(v.Value);
        }
        return outList.ToArray();
    }

    // ── Pure: connection-input action ────────────────────────────────────────
    public enum ConnectionInputAction { AddElementIds, RemoveElementIds, AddReferences, RemoveReferences }

    public static ConnectionInputAction ParseConnectionInputAction(string? value, out string? error)
    {
        error = null;
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "add_element_ids": return ConnectionInputAction.AddElementIds;
            case "remove_element_ids": return ConnectionInputAction.RemoveElementIds;
            case "add_references": return ConnectionInputAction.AddReferences;
            case "remove_references": return ConnectionInputAction.RemoveReferences;
            default:
                error = "Invalid 'action'. Valid: add_element_ids, remove_element_ids, add_references, remove_references";
                return ConnectionInputAction.AddElementIds;
        }
    }

    // ── Pure: XYZ (mm) ───────────────────────────────────────────────────────
    public static XYZ ParseXyzMm(JToken token)
    {
        var x = token["x"]?.Value<double?>() ?? 0;
        var y = token["y"]?.Value<double?>() ?? 0;
        var z = token["z"]?.Value<double?>() ?? 0;
        return new XYZ(FromMm(x), FromMm(y), FromMm(z));
    }

    public static JObject XyzToDtoMm(XYZ p) => new JObject { ["x"] = ToMm(p.X), ["y"] = ToMm(p.Y), ["z"] = ToMm(p.Z) };

    // ── Revit-dependent resolvers (signatures FIXED; bodies reflection-verified) ──
    public static (Element? element, CortexResult<object>? error) RequireElement(Document doc, long? id)
    {
        if (id == null || id <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "an element id is required"));
        var e = doc.GetElement(ToolHelpers.ToElementId(id.Value));
        if (e == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {id}"));
        return (e, null);
    }

    /// <summary>Resolve a StructuralConnectionHandler by id (verify the type name + that it derives from Element).</summary>
    public static (StructuralConnectionHandler? handler, CortexResult<object>? error) RequireConnectionHandler(Document doc, long? id)
    {
        if (id == null || id <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "connectionId is required"));
        var h = doc.GetElement(ToolHelpers.ToElementId(id.Value)) as StructuralConnectionHandler;
        if (h == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No structural connection handler with id {id}",
                suggestion: "Use list_steel_connection_handlers to find connection ids"));
        return (h, null);
    }

    /// <summary>Resolve element ids from a JArray into a validated IList&lt;ElementId&gt; (skips/echoes invalid in 'skipped').</summary>
    public static (IList<ElementId> ids, IList<long> skipped) ResolveElementIds(Document doc, JArray arr)
    {
        var ids = new List<ElementId>(); var skipped = new List<long>();
        foreach (var t in arr.Where(x => x.Type == JTokenType.Integer))
        {
            var raw = t.Value<long>();
            var eid = ToolHelpers.ToElementId(raw);
            if (doc.GetElement(eid) != null) ids.Add(eid); else skipped.Add(raw);
        }
        return (ids, skipped);
    }

    /// <summary>Standard "needs a newer/older Revit" structured error.</summary>
    public static CortexResult<object> MinVersionError(string feature, int minYear)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires Revit {minYear} or newer; the active target does not support it.",
            suggestion: $"Open the model in Revit {minYear}+ to use this feature.");

    /// <summary>Standard "needs an installed structural connection provider" error.</summary>
    public static CortexResult<object> ProviderUnavailableError(string feature)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires an installed structural steel connection provider (e.g. Autodesk Steel Connections, IDEA StatiCa) and steel-compatible content.",
            suggestion: "Use create_generic_steel_connection when provider availability is unknown, or check get_structural_steel_api_capabilities.");
}
```

> **Reflection note for the implementer:** confirm the exact type name and namespace of `StructuralConnectionHandler` (spec says `Autodesk.Revit.DB.Structure`), that it derives from `Element` (so `as StructuralConnectionHandler` works after `GetElement`), and the `SteelElementProperties` / provider-registry types you'll need in later tasks. Also confirm `RevitCortex.Tools.StructuralSteel` does NOT shadow a Revit type (unlike `RevitCortex.Tools.Rebar` which shadowed `Rebar`) — `StructuralSteel` is unlikely to collide, but if any bare type name fails to resolve, fully-qualify it. Add the resolver bodies for `RequireSteelCandidate`, `RequireConnectionHandlerType`, `ResolveStructuralConnectionType`, `ResolveApprovalType`, `ResolveReferences`, `ParseConnectionInputPoints`, `ParseConnectionInputPointInfo`, `ParseCutOptions`, and the provider/capability detector as later tasks need them — keeping each signature stable once introduced.

- [ ] **Step 4: Run parser tests, verify PASS** — same filter command → 5 tests PASS.

- [ ] **Step 5: Build R25 + R24 (Tools)** — both succeed (R24 net48 confirms no `record`/`init`/`Range`).

- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelToolHelpers.cs src/RevitCortex.Tests/StructuralSteel/SteelHelpersParsingTests.cs
git commit -m "feat(steel): add StructuralSteelToolHelpers shared utilities + parser tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Module 1 — Discovery & inventory (15 read tools)

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelDiscoveryTools.cs`

All 15 are read-only (`get_`/`list_`/`analyze_` prefix → auto read-only via `CortexRouter.IsReadOnlyTool`), `IsDynamic => false`, `Category => "StructuralSteel"`, no transaction. **Reflect the steel API first** (the type names/methods below are from the spec and MUST be confirmed). Implement in increments; build R24+R25 after each. No `[Fact]` tests for these (need a live Document).

This task fully codes 3 representative tools; implement the remaining 12 with the same skeleton + the contracts given.

- [ ] **Step 1: Create the file with `get_structural_steel_api_capabilities` (FULL — the gate everything consults)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>Reports which version-gated + provider-dependent steel features the running Revit supports.</summary>
public class GetStructuralSteelApiCapabilitiesTool : ICortexTool
{
    public string Name => "get_structural_steel_api_capabilities";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report which structural steel features the running Revit supports: SteelElementProperties, structural connections, cut utils, custom-connection mutation (gone in R27), and whether any structural connection provider is installed.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        int year =
#if REVIT2027_OR_GREATER
            2027;
#elif REVIT2026_OR_GREATER
            2026;
#elif REVIT2025_OR_GREATER
            2025;
#elif REVIT2024_OR_GREATER
            2024;
#else
            2023;
#endif
        // Provider presence is a runtime fact (a document helps but is optional). Detect defensively:
        bool providerInstalled = false;
        try { providerInstalled = StructuralSteelToolHelpers.AnyConnectionProviderInstalled(); }
        catch { /* registry may be empty/unavailable */ }

        return CortexResult<object>.Ok(new
        {
            revitYear = year,
            supportsSteelElementProperties = true,   // R23-R27 per spec — confirm by reflection
            supportsStructuralConnections = true,
            supportsSolidSolidCutUtils = true,
            supportsInstanceVoidCutUtils = true,
            supportsCustomConnectionMutation = year <= 2026,  // AddElementsToCustomConnection gone in R27
            connectionProviderInstalled = providerInstalled,
            note = providerInstalled
                ? "A structural connection provider appears available; typed connections may work."
                : "No structural connection provider detected; prefer create_generic_steel_connection."
        });
    }
}
```

> Add `StructuralSteelToolHelpers.AnyConnectionProviderInstalled()` (returns bool) in this task — reflect `StructuralConnectionsProviderRegistry`/`IStructuralConnectionsProvider` for the real registry-query method (e.g. an enumeration of registered providers). If the registry API can't be safely queried, make it return false and document that the flag is best-effort.

- [ ] **Step 2: Build R25 + R24 + R26 (Tools).** R26 confirms the `supportsCustomConnectionMutation` boundary compiles; R27 build in a later step validates `year=2027`.

- [ ] **Step 3: Append `list_steel_connection_handlers` (FULL — the collector read pattern) + `get_steel_element_properties` (FULL — the SteelElementProperties wrapper)**

```csharp
/// <summary>Lists structural connection handlers (id, type id/name, connected element count). Capped.</summary>
public class ListSteelConnectionHandlersTool : ICortexTool
{
    public string Name => "list_steel_connection_handlers";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List structural connection handlers: id, type id/name, connected element count, failed state. Use maxResults (default 100) and summaryOnly for counts-first.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var max = input["maxResults"]?.Value<int?>() ?? 100;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            var all = new FilteredElementCollector(doc!).OfClass(typeof(StructuralConnectionHandler))
                .Cast<StructuralConnectionHandler>().ToList();
            if (summaryOnly)
                return CortexResult<object>.Ok(new { count = all.Count, summaryOnly = true });

            var items = all.Take(max).Select(h =>
            {
                var connected = h.GetConnectedElementIds()?.Count ?? 0;
                return new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(h),
                    ["typeId"] = ToolHelpers.GetElementIdValue(h.GetTypeId()),
                    ["connectedElementCount"] = connected
                };
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                count = all.Count,
                returnedCount = items.Count,
                truncated = all.Count > max,
                handlers = items
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list connection handlers: {ex.Message}");
        }
    }
}

/// <summary>Reads SteelElementProperties summary for an element (fabrication id, external ids, material links).</summary>
public class GetSteelElementPropertiesTool : ICortexTool
{
    public string Name => "get_steel_element_properties";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read steel fabrication properties of an element: whether it has SteelElementProperties, fabrication unique id, external ids, linked material ids. Use summaryOnly for flags+counts only.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (elem, eerr) = StructuralSteelToolHelpers.RequireElement(doc!, input["elementId"]?.Value<long?>());
        if (eerr != null) return eerr;
        var summaryOnly = input["summaryOnly"]?.Value<bool?>() ?? false;
        try
        {
            // SteelElementProperties.GetSteelElementProperties(Element) -> SteelElementProperties? (confirm signature)
            var props = SteelElementProperties.GetSteelElementProperties(elem!);
            if (props == null)
                return CortexResult<object>.Ok(new { elementId = ToolHelpers.GetElementIdValue(elem!), hasSteelProperties = false });

            var fabId = props.GetFabricationUniqueID();           // confirm name/return
            var externalIds = props.GetAllExternalIds() ?? new List<string>();   // confirm
            var materialIds = props.GetAllRevitMaterialsIds()?.Select(i => ToolHelpers.GetElementIdValue(i)).ToList()
                              ?? new List<long>();
            if (summaryOnly)
                return CortexResult<object>.Ok(new
                {
                    elementId = ToolHelpers.GetElementIdValue(elem!),
                    hasSteelProperties = true,
                    externalIdCount = externalIds.Count,
                    materialLinkCount = materialIds.Count
                });
            return CortexResult<object>.Ok(new
            {
                elementId = ToolHelpers.GetElementIdValue(elem!),
                hasSteelProperties = true,
                fabricationUniqueId = fabId,
                externalIds,
                materialLinkIds = materialIds
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read steel element properties: {ex.Message}");
        }
    }
}
```

> Reflect `SteelElementProperties` for the EXACT names/returns of `GetSteelElementProperties`, `GetFabricationUniqueID`, `GetAllExternalIds`, `GetAllRevitMaterialsIds`. Adapt only those lines if they differ; keep the tool contract.

- [ ] **Step 4: Build R25 + R24 (Tools).** Both succeed.

- [ ] **Step 5: Append the remaining 12 read tools** (standard read skeleton; every one supports `maxResults`+`summaryOnly` where it returns a list, with truthful `count`). Contracts:

- **`list_steel_connection_types`** — `StructuralConnectionType.GetAllStructuralConnectionTypeIds(doc)` → id + name + family symbol id.
- **`list_steel_connection_handler_types`** — collect `StructuralConnectionHandlerType` → id + name.
- **`list_steel_approval_types`** — collect `StructuralConnectionApprovalType` → id + name.
- **`list_steel_connection_providers`** — query `StructuralConnectionsProviderRegistry` → provider ids/names (read-only; if registry can't be queried, return `{ count: 0, note: "..." }`).
- **`get_steel_connection_data`** — inputs `connectionId`; resolve handler; return type id/name, `GetConnectedElementIds`, `GetOrigin` (mm), failed state (`GetFailed`), approval/code-checking/disconnect/override flags (each read defensively in try/catch, since provider-dependent).
- **`get_steel_connection_type_data`** — inputs `connectionTypeId`; family symbol id, input-points info summary (NOT raw IntPtr buffers).
- **`get_steel_connection_settings`** — `StructuralConnectionSettings` summary (reflect for the getter).
- **`get_steel_external_id_map`** — inputs `elementId`; `GetAllExternalIds`/`GetExternalId` mapping; `maxResults`+`summaryOnly`.
- **`get_steel_material_links`** — inputs `elementId`; `GetAllRevitMaterialsIds` + external material info.
- **`get_steel_element_warnings`** — inputs optional `elementId`; `GetCurrWarnings`/`GetElemsWithWarnings`/`CountOfAsyncWarnings`; `maxResults`+`summaryOnly`; report queued vs current.
- **`get_steel_cut_data`** — inputs `elementId`; combine `SolidSolidCutUtils.GetCuttingSolids`/`GetSolidsBeingCut` + `InstanceVoidCutUtils.GetCuttingVoidInstances`/`GetElementsBeingCut` for that element.
- **`analyze_structural_steel_model`** — document-wide summary: counts of connection handlers, connection types, elements-with-steel-properties, elements-with-warnings, cut relationships. `summaryOnly` returns only the counts; otherwise capped sample arrays via `maxResults`. Truthful counters.

Build R25 + R24 + R26 after writing these.

- [ ] **Step 6: Run the full no-Revit suite** — `dotnet test ... -c "Debug R25"` → all pass (baseline `BASE_TESTS` + 5 new parser tests). No new failures.

- [ ] **Step 7: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelDiscoveryTools.cs src/RevitCortex.Tools/StructuralSteel/StructuralSteelToolHelpers.cs
git commit -m "feat(steel): add Module 1 discovery + capability tools

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Module 1 server wrappers + contract test + registration/read-only coverage

**Files:**
- Create: `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- Create: `src/RevitCortex.Tests/StructuralSteel/StructuralSteelServerContractTests.cs`
- Modify: `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs`, `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs`

- [ ] **Step 1: Write the failing contract test** (mirror `RebarServerContractTests` — reflection over `StructuralSteelTools`):

```csharp
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

public class StructuralSteelServerContractTests
{
    private static MethodInfo M(string name) =>
        Assert.Single(typeof(StructuralSteelTools).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == name);

    [Fact]
    public void Capabilities_HasRevitAndCtOnly()
    {
        var m = M(nameof(StructuralSteelTools.GetStructuralSteelApiCapabilities));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n), n => Assert.Equal("ct", n));
        Assert.NotNull(m.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void ListHandlers_ExposesMaxResultsAndSummaryOnly()
    {
        var m = M(nameof(StructuralSteelTools.ListSteelConnectionHandlers));
        var names = m.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("maxResults", names);
        Assert.Contains("summaryOnly", names);
    }

    [Fact]
    public void GetSteelElementProperties_ExposesElementId()
    {
        var m = M(nameof(StructuralSteelTools.GetSteelElementProperties));
        Assert.Contains("elementId", m.GetParameters().Select(p => p.Name));
    }
}
```

- [ ] **Step 2: Run, verify FAIL** (StructuralSteelTools missing).

- [ ] **Step 3: Create `StructuralSteelTools.cs` with the 15 Module-1 wrappers.** Pattern (mirror `RebarTools.cs`): `[McpServerToolType]` static class; each `[McpServerTool(Name="...")]` forwards via `revit.ExecuteAsync("<same name>", p, ct)`. Read-list tools expose `int? maxResults` + `bool? summaryOnly`. Example for two:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class StructuralSteelTools
{
    [McpServerTool(Name = "get_structural_steel_api_capabilities"), Description("Report which structural steel features the running Revit supports (SteelElementProperties, connections, cut utils, R27 custom-connection change, provider presence).")]
    public static async Task<string> GetStructuralSteelApiCapabilities(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_structural_steel_api_capabilities", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_steel_connection_handlers"), Description("List structural connection handlers (id, type, connected element count). maxResults default 100; summaryOnly for counts.")]
    public static async Task<string> ListSteelConnectionHandlers(
        RevitConnectionManager revit,
        [Description("Max handlers to return. Default 100")] int? maxResults = null,
        [Description("Return only counts, no per-handler array. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("list_steel_connection_handlers", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_steel_element_properties"), Description("Read steel fabrication properties of an element (fabrication id, external ids, material links). summaryOnly for flags+counts.")]
    public static async Task<string> GetSteelElementProperties(
        RevitConnectionManager revit,
        [Description("Element id")] long elementId,
        [Description("Return only flags + counts. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementId"] = elementId };
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("get_steel_element_properties", p, ct)).ToString();
    }

    // ... remaining 12 Module-1 wrappers, same pattern; names MUST equal the plugin tool names.
}
```

Remaining Module-1 wrappers to add: `list_steel_connection_types`, `list_steel_connection_handler_types`, `list_steel_approval_types`, `list_steel_connection_providers`, `get_steel_connection_data`(connectionId), `get_steel_connection_type_data`(connectionTypeId), `get_steel_connection_settings`, `get_steel_external_id_map`(elementId, maxResults, summaryOnly), `get_steel_material_links`(elementId), `get_steel_element_warnings`(elementId?, maxResults, summaryOnly), `get_steel_cut_data`(elementId), `analyze_structural_steel_model`(maxResults, summaryOnly).

- [ ] **Step 4: Run contract test, verify PASS (3 tests). Build the server project (0 errors).**

- [ ] **Step 5: Update registration count + read-only rows.** In `ToolRegistrationTests.cs` bump the threshold from `BASE` to `BASE + 15` (Module 1 adds 15 tools), updating the comment. In `ReadOnlyModeTests.cs` add:
```csharp
    [InlineData("list_steel_connection_handlers", true)]
    [InlineData("get_steel_element_properties", true)]
    [InlineData("get_structural_steel_api_capabilities", true)]
    [InlineData("create_generic_steel_connection", false)]
    [InlineData("add_steel_solid_cut", false)]
    [InlineData("delete_steel_connection", false)]
```
Run both test classes → PASS (the R25 Tools build makes the 15 tools visible to the reflection count).

- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/StructuralSteel/StructuralSteelServerContractTests.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs
git commit -m "feat(steel): add Module 1 server wrappers + contract/registration/read-only tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

**End of Step 1 (Module 1).** 15 read tools + wrappers build on R24/R25/R26; suite green.

---

## Task 4: Module 2 — Connection creation & input mutation (8 write tools)

> **API VERIFIED 2026-05-30 (reflection over RevitAPI.dll R25/R26).** The plan
> originally listed 9 tools; two assumed non-existent API:
> - `set_steel_connection_disconnected` — **REMOVED**: `StructuralConnectionHandler`
>   has NO "disconnected" property. Its only settable instance props are
>   `ApprovalTypeId` (ElementId), `CodeCheckingStatus`
>   (enum: NotCalculated/OkChecked/CheckingFailed), `OverrideTypeParams` (bool),
>   `SingleElementEndIndex` (int). No disconnect concept exists.
> - `set_steel_connection_type` — **RE-SCOPED**: there is no type setter on the
>   handler. Reimplemented as *change type by recreation*: read the connected
>   element ids (+ existing input points), delete the old handler, and
>   `Create(doc, ids, newTypeId)` — preserving the connected elements.
>
> Confirmed signatures (R25): `CreateGenericConnection(Document, IList<ElementId>)`;
> `Create(Document, IList<ElementId> ids, ElementId typeId)`,
> `Create(Document, IList<ElementId> ids, String typeName)`,
> `Create(Document, IList<ElementId> ids, ElementId typeId, IList<ConnectionInputPoint> additionalInputPoints)`;
> instance `AddElementIds(IList<ElementId>)`, `RemoveElementIds(IList<ElementId>)`,
> `GetConnectedElementIds()`, `AddReferences(Document, IList<Reference>)`,
> `RemoveReferences(IList<Reference>)`, `GetInputPoints()`, `SetDefaultElementOrder()`.
> **Net result: 8 write tools, registration BASE+15 → BASE+23.**

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTools.cs`
- Modify: `src/RevitCortex.Server/Tools/StructuralSteelTools.cs` (+8 wrappers), `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` (`BASE+15` → `BASE+23`)

All 8 are write tools (validate → confirm → tx → rollback). `create_generic_steel_connection` is the always-works baseline; typed `create_steel_connection` and approval/status tools gate on provider availability → `ProviderUnavailableError(...)`.

- [ ] **Step 1: Create the file with `create_generic_steel_connection` (FULL — the safe baseline)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>
/// Creates a GENERIC structural connection between ≥2 elements (no provider required — the safe baseline).
/// Input: elementIds[] (≥2), optional connectionName, dryRun.
/// </summary>
public class CreateGenericSteelConnectionTool : ICortexTool
{
    public string Name => "create_generic_steel_connection";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a generic structural connection between two or more elements (works without an installed connection provider). Provide elementIds (>=2); optional connectionName. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        if (input["elementIds"] is not JArray arr || arr.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (array of >=2 element ids) is required");

        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count < 2)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "Fewer than 2 valid elements resolved for the connection",
                context: skipped.Count > 0 ? new Dictionary<string, object> { ["skipped"] = skipped } : null);

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, wouldConnect = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(), skipped });

        if (!session.RequestConfirmation("create generic steel connection", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Generic Steel Connection");
        tx.Start();
        try
        {
            // StructuralConnectionHandler.CreateGenericConnection(Document, ICollection<ElementId>) -> handler (confirm signature)
            var handler = StructuralConnectionHandler.CreateGenericConnection(doc!, ids);
            if (handler == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection handler");
            }
            var name = input["connectionName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                try { handler.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(name); } catch { /* naming is best-effort */ }
            }
            var id = ToolHelpers.GetElementIdValue(handler);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created generic steel connection {id} between {ids.Count} element(s)",
                connectionId = id,
                connectedElementIds = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                skipped
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create generic connection: {ex.Message}");
        }
    }
}
```

> Confirm `CreateGenericConnection`'s exact signature/return and the right naming parameter (the comment param may differ). Confirm `CortexResult<object>.Fail` accepts a `context:` dict (it does in the rebar code — `Fail(code, message, suggestion:, context:)`).

- [ ] **Step 2: Build R25 + R24 (Tools).** Both succeed.

- [ ] **Step 3: Append the other 8 Module-2 tools** (standard write skeleton). Contracts:

- **`create_steel_connection`** (typed, provider-gated): inputs `elementIds[]`, `connectionHandlerTypeId|connectionHandlerTypeName`, optional `inputPoints[]` ({id,x,y,z} mm), `dryRun`. Resolve the handler type; if none / no provider → `ProviderUnavailableError("Typed steel connections")`. Call `StructuralConnectionHandler.Create(doc, ids, typeId)` (confirm signature). Return `connectionId`, connected ids.
- **`modify_steel_connection_inputs`** (action-based): inputs `connectionId`, `action` (`add_element_ids`|`remove_element_ids`|`add_references`|`remove_references`), `elementIds[]` (for id actions) or reference descriptors (for reference actions — element id + reference hint / selected subelement, NEVER raw Reference strings). Use `ParseConnectionInputAction`. For id actions call `AddElementIds`/`RemoveElementIds`; for reference actions resolve via `ResolveReferences` then `AddReferences`/`RemoveReferences`. Return accepted/skipped.
- **`set_steel_connection_type`** (provider-gated, change-by-recreation): inputs `connectionId`, `connectionHandlerTypeId|connectionHandlerTypeName`, optional `dryRun`. Read `handler.GetConnectedElementIds()` and `handler.GetInputPoints()`; resolve the new type id (provider-gate if no provider / type not found → `ProviderUnavailableError`); `RequestConfirmation("change steel connection type", ids.Count)`; in one tx `doc.Delete(handler.Id)` then `StructuralConnectionHandler.Create(doc, ids, newTypeId, inputPoints)`; return the new `connectionId` + connected ids. NOTE: this is recreation, not an in-place setter (no type setter exists on the handler).
- **`set_steel_connection_approval`** (provider-gated): inputs `connectionId`, `approvalTypeId|approvalTypeName`. Set `handler.ApprovalTypeId = <id>` (resolve approval type id; verify name via `StructuralConnectionApprovalType.IsValidApprovalTypeName`). Provider-gate.
- **`set_steel_connection_status`** (provider-gated): inputs `connectionId`, `status` (enum string: `NotCalculated`|`OkChecked`|`CheckingFailed`). Set `handler.CodeCheckingStatus = <enum>`. Provider-gate.
- **`set_steel_connection_default_order`**: inputs `connectionId`. Call `handler.SetDefaultElementOrder()`.
- **`delete_steel_connection`** (DESTRUCTIVE): inputs `connectionId`, `dryRun`. `RequestConfirmation("delete steel connection", 1)`; `doc.Delete(handler.Id)`. Return deleted id.

(`set_steel_connection_disconnected` removed — no such API; see the box at the top of Task 4.)

Build R25 + R24 + R26 after writing these.

- [ ] **Step 4: Add the 8 Module-2 server wrappers** to `StructuralSteelTools.cs` (names match plugin names; complex inputs `elementIds`/`inputPoints` as JSON strings → `JArray.Parse`; scalars direct; write tools expose `dryRun` where the plugin reads it). Build server.

- [ ] **Step 5: Bump registration threshold `BASE+15` (148) → `BASE+23` (156); run it. PASS.**

- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTools.cs src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(steel): add Module 2 connection creation + input mutation

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Module 3 — Connection type & approval administration (6 write + 3 read)

> **API VERIFIED 2026-05-30 (reflection R26 + R27).** Real signatures (differ from the
> draft below — use these):
> - `StructuralConnectionType.Create(Document, StructuralConnectionApplyTo applyTo, String name, ElementId familySymbolId)` — needs `applyTo` + `name`, NOT `Create(doc, symId)`.
> - `StructuralConnectionType.ValidFamilySymbolId(Document, StructuralConnectionApplyTo applyTo, ElementId)` — needs `applyTo`, NOT `(doc, symId)`.
> - `StructuralConnectionType`: instance `GetFamilySymbolId()`, `SetFamilySymbolId(ElementId)`; prop `ApplyTo {get;}`; static `GetAllStructuralConnectionTypeIds(doc, out ICollection<ElementId>)`.
> - `enum StructuralConnectionApplyTo`: `BeamsAndBraces`, `ColumnTop`, `ColumnBase`, `Connection`.
> - `StructuralConnectionHandlerType.Create(doc, name, Guid, familyName[, categoryId][, IList<ConnectionInputPointInfo>])` (3 overloads); `CreateDefaultStructuralConnectionHandlerType(doc)`; `IsTypeNameValidForCustomConnection(doc, name)`; `UpdateCustomConnectionType(handler, addRefs, removeRefs)`.
> - **R26→R27 gating CONFIRMED:** `AddElementsToCustomConnection` and `RemoveMainSubelementsFromCustomConnection` exist on R23-R26 but are **REMOVED in R27**. `UpdateCustomConnectionType` **survives in R27**. So `manage_custom_steel_connection_type` uses `#if !REVIT2027_OR_GREATER` for the add/remove-subelements actions and routes the generic add/remove-references action through `UpdateCustomConnectionType` on ALL versions (no total Fail on R27).
> - `StructuralConnectionApprovalType`: only `Create(doc, name)`, `IsValidApprovalTypeName(doc, name)`, `GetAllStructuralConnectionApprovalTypes(doc, out ...)` — **NO rename, NO delete**. So `manage_steel_approval_type` supports `create` + `list`; `rename`/`delete` return a structured Fail.
> - `ConnectionValidationInfo`: `GetWarning(int)`, `ManyWarnings()`, `IsValidWarningIndex(int)`. `ConnectionValidationWarning`: props `Reason` (`ConnectionWarning`), `Resolution` (`ConnectionResolution`), `GetParts()`. (Reflect how to OBTAIN a `ConnectionValidationInfo` from a handler — likely a validate method on the handler or StructuralConnectionTestUtil; if no public producer exists, `get_steel_connection_validation` returns a documented "not available via public API" Fail.)

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTypeTools.cs`
- Modify: `StructuralSteelTools.cs` (+9 wrappers), `ToolRegistrationTests.cs` (`BASE+23` (156) → `BASE+32` (165))

`manage_custom_steel_connection_type` is **R27-gated** (add/remove-subelements absent in R27) and IntPtr-free (documented-member ops only).

- [ ] **Step 1: Create the file with `create_steel_structural_connection_type` (FULL — a clean typed create)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>Creates a StructuralConnectionType bound to a family symbol.</summary>
public class CreateSteelStructuralConnectionTypeTool : ICortexTool
{
    public string Name => "create_steel_structural_connection_type";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a structural connection type bound to a family symbol. Provide familySymbolId (a valid connection family symbol). Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var symbolId = input["familySymbolId"]?.Value<long?>();
        if (symbolId == null || symbolId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "familySymbolId is required");
        var symId = ToolHelpers.ToElementId(symbolId.Value);
        // StructuralConnectionType.ValidFamilySymbolId(doc, symId) (confirm signature)
        if (!StructuralConnectionType.ValidFamilySymbolId(doc!, symId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Family symbol {symbolId} is not valid for a structural connection type");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, familySymbolId = symbolId });

        if (!session.RequestConfirmation("create steel connection type", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Steel Connection Type");
        tx.Start();
        try
        {
            var ct = StructuralConnectionType.Create(doc!, symId);   // confirm signature/return
            if (ct == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no connection type"); }
            var id = ToolHelpers.GetElementIdValue(ct);
            tx.Commit();
            return CortexResult<object>.Ok(new { message = $"Created structural connection type {id}", connectionTypeId = id, familySymbolId = symbolId });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create connection type: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24.** Both succeed.

- [ ] **Step 3: Append the other 8 Module-3 tools.** Contracts:

- **`create_steel_connection_handler_type`** (write): inputs name + optional source params; `StructuralConnectionHandlerType.Create(...)` (confirm). Provider-gate if it requires one.
- **`create_default_steel_connection_handler_type`** (write): `StructuralConnectionHandlerType.CreateDefaultStructuralConnectionHandlerType(doc)`. Return type id.
- **`set_steel_connection_type_family_symbol`** (write): inputs `connectionTypeId`, `familySymbolId`; validate via `ValidFamilySymbolId`; `SetFamilySymbolId`.
- **`manage_steel_approval_type`** (write): action-based (`create`/`rename`/`delete` as the API allows) over `StructuralConnectionApprovalType`. Reflect for the real surface; unsupported actions → structured Fail.
- **`manage_custom_steel_connection_type`** (write, **R27-gated**): `#if !REVIT2027_OR_GREATER` use `AddElementsToCustomConnection`/`RemoveMainSubelementsFromCustomConnection`/`UpdateCustomConnectionType` for documented member ops; `#else` (R27) → `MinVersionError`-style "custom connection mutation not available in Revit 2027" (use a MAX-version variant of the helper or a plain `Fail(InvalidInput, ...)` naming 2026 as the last supporting version). NO IntPtr buffers.
- **`get_steel_connection_input_points`** (read): inputs `connectionTypeId` or `connectionId`; `GetInputPointsInfo`/`GetInputPoints` summarized as {id, x,y,z mm} — NOT raw buffers.
- **`get_steel_connection_applicability`** (read): inputs `connectionTypeId`, `elementIds[]`; report whether the type applies (reflect for the applicability/validation query).
- **`get_steel_connection_validation`** (read): inputs `connectionId`; `ConnectionValidationInfo`/`ConnectionValidationWarning` summary.

Build R25 + R24 + R26 + **R27** (R27 exercises the `manage_custom_steel_connection_type` `#else`).

- [ ] **Step 4: Add 9 Module-3 wrappers. Build server.**
- [ ] **Step 5: Bump threshold 156 → 165 (BASE+32; Module 3 adds 9); run. PASS.**
- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTypeTools.cs src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(steel): add Module 3 connection type + approval admin (R27-gated custom)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Module 4 — Fabrication metadata (5 tools — RE-SCOPED from 13)

> **API VERIFIED 2026-05-30 (reflection R25).** `Autodesk.Revit.DB.Steel.SteelElementProperties`
> (in RevitAPI.dll) exposes ONLY these public members:
> - STATIC `IList<ElementId> AddFabricationInformationForRevitElements(Document, IList<ElementId>)`
> - STATIC `Guid GetFabricationUniqueID(Document, Reference)`
> - STATIC `Reference GetReference(Document, Guid)`
> - STATIC `SteelElementProperties GetSteelElementProperties(Element)`
> - INSTANCE prop `Guid UniqueID {get;set;}`, `bool IsValidObject {get;}`
>
> **The plan's other 8 assumed members DO NOT EXIST** and their tools are CUT:
> `AddToElement`, `GetExternalId`, `GetRevitId`, `RegisterMaterial`, `RemoveLink`,
> `ClearWarnings`, `CountOfAsyncWarnings`, `GetCurrWarnings`, `PostWarning`,
> `RemoveWarning`, `FlushWarnings`, `SetChanged` — none are on `SteelElementProperties`
> (nor is there any public steel warning-queue / external-material / fabrication-link API).
> Module 4 is therefore **5 tools** built on the 6 real members, NOT 13.
>
> The 5 tools:
> - **`add_steel_fabrication_info`** (write): `elementIds[]` → `AddFabricationInformationForRevitElements(doc, ids)`; returns the ids that received fabrication info (the method returns an `IList<ElementId>`). confirm/tx/rollback + dryRun.
> - **`get_steel_element_fabrication_properties`** (read): `elementId` → `GetSteelElementProperties(elem)`; return `{ hasFabricationProperties (props!=null && IsValidObject), uniqueId (Guid string or null) }`.
> - **`set_steel_fabrication_unique_id`** (write): `elementId`, `uniqueId` (Guid, ParseGuid) → `GetSteelElementProperties(elem).UniqueID = guid` in a tx. Fail if the element has no steel properties.
> - **`get_steel_fabrication_unique_id`** (read): `elementId` → resolve props, return `UniqueID`. (Reference-based `GetFabricationUniqueID(doc, Reference)` is not exposed because we can't fabricate a Reference from JSON; use the element-props path.)
> - **`get_steel_reference_by_fabrication_id`** (read): `fabricationGuid` (ParseGuid) → `GetReference(doc, guid)`; return the referenced element id (`reference.ElementId`) or a not-found note.

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelFabricationTools.cs`
- Modify: `StructuralSteelTools.cs` (+5 wrappers), `ToolRegistrationTests.cs` (165 → 170)

Wrap `SteelElementProperties` (verified members only — no warning/material/link/changed API exists).

- [ ] **Step 1: Create the file with `add_steel_fabrication_info` (FULL)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Steel;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>Adds steel fabrication information to one or more Revit elements (SteelElementProperties).</summary>
public class AddSteelFabricationInfoTool : ICortexTool
{
    public string Name => "add_steel_fabrication_info";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add steel fabrication information to Revit elements so they participate in steel detailing. Provide elementIds. Supports dryRun.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        if (input["elementIds"] is not JArray arr || arr.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds (non-empty array) is required");
        var (ids, skipped) = StructuralSteelToolHelpers.ResolveElementIds(doc!, arr);
        if (ids.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No valid elements resolved");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, wouldAddTo = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(), skipped });

        if (!session.RequestConfirmation("add steel fabrication info", ids.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Add Steel Fabrication Info");
        tx.Start();
        try
        {
            // SteelElementProperties.AddFabricationInformationForRevitElements(Document, ICollection<ElementId>) (confirm)
            SteelElementProperties.AddFabricationInformationForRevitElements(doc!, ids);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Added steel fabrication info to {ids.Count} element(s)",
                elementIds = ids.Select(i => ToolHelpers.GetElementIdValue(i)).ToList(),
                skipped
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to add fabrication info: {ex.Message}");
        }
    }
}
```

> Confirm the `Autodesk.Revit.DB.Steel` namespace + `AddFabricationInformationForRevitElements` signature. If it's per-element (`AddToElement(Element)`) instead of batch, loop over `ids`.

- [ ] **Step 2: Build R25 + R24.**
- [ ] **Step 3: Append the other 12 tools.** Contracts (all wrap `SteelElementProperties`; resolve the element, get/create its props, mutate inside tx for writes):
  - **`attach_steel_fabrication_link`** (write): `elementIds[]` + `fabricationGuid` (ParseGuid) → link via the documented API.
  - **`remove_steel_fabrication_link`** (write): `elementId` → `RemoveLink(...)`.
  - **`register_steel_material`** (write): `elementId` + `materialId` (+ optional external id) → `RegisterMaterial(...)`.
  - **`post_steel_warning`** (write): `elementId` + `message` → `PostWarning(...)`.
  - **`remove_steel_warning`** (write): `elementId` + warning id → `RemoveWarning(...)`.
  - **`clear_steel_warnings`** (write): `elementId?` → `ClearWarnings(...)`.
  - **`flush_steel_warnings`** (write): → `FlushWarnings(...)`.
  - **`mark_steel_element_changed`** (write): `elementId` → `SetChanged(...)`.
  - **`get_steel_fabrication_unique_id`** (read): `elementId` → `GetFabricationUniqueID()` (GUID string).
  - **`get_steel_revit_element_by_fabrication_id`** (read): `fabricationGuid` → `GetRevitId(...)` → element id.
  - **`get_steel_external_material`** (read): `elementId` → external material info.
  - **`get_steel_warning_counts`** (read): `elementId?` → `CountOfAsyncWarnings`/current-warning counts (counts-first; this is the `summaryOnly`-style read).

  Each warning write tool: note that warnings may be asynchronous — return queued vs current counts so the caller isn't misled. Build R25 + R24 + R26.

- [ ] **Step 4: Add 5 wrappers. Build server.**
- [ ] **Step 5: Bump threshold 165 → 170 (Module 4 adds 5); run. PASS.**
- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelFabricationTools.cs src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(steel): add Module 4 fabrication metadata, materials, warnings

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Module 5 — Solid & instance-void cuts (5 write + 3 read)

> **API VERIFIED 2026-05-30 (reflection R25). Both util classes are SOLID — all methods real.**
> `Autodesk.Revit.DB.SolidSolidCutUtils` (STATIC):
> - `void AddCutBetweenSolids(Document, Element solidToBeCut, Element cuttingSolid, bool splitFacesOfCuttingSolid)` and 3-arg `(Document, solidToBeCut, cuttingSolid)`. **ARG ORDER: (cuttee, cutter)** — the draft below passes them REVERSED. "cutElementId cuts targetElementId" ⇒ `AddCutBetweenSolids(doc, target, cutter, splitFaces)` (target = solidToBeCut, cutter = cuttingSolid).
> - `bool CanElementCutElement(Element cuttingElement, Element cutElement, out CutFailureReason reason)` — (cutter, cuttee). So `CanElementCutElement(cutter, target, out reason)` is correct.
> - `bool CutExistsBetweenElements(Element first, Element second, out bool firstCutsSecond)`
> - `ICollection<ElementId> GetCuttingSolids(Element)`, `ICollection<ElementId> GetSolidsBeingCut(Element)`
> - `bool IsAllowedForSolidCut(Element)`, `bool IsElementFromAppropriateContext(Element)`
> - `void RemoveCutBetweenSolids(Document, Element first, Element second)`
> - `void SplitFacesOfCuttingSolid(Element first, Element second, bool split)`
> `Autodesk.Revit.DB.InstanceVoidCutUtils` (STATIC):
> - `void AddInstanceVoidCut(Document, Element element, Element cuttingInstance)` — **(cuttee, voidInstance)**. So `AddInstanceVoidCut(doc, target, voidInstance)`.
> - `bool CanBeCutWithVoid(Element)` — arg is the CUTTEE (the element to be cut).
> - `ICollection<ElementId> GetCuttingVoidInstances(Element element)`, `ICollection<ElementId> GetElementsBeingCut(Element cuttingInstance)`
> - `bool InstanceVoidCutExists(Element element, Element cuttingInstance)`, `bool IsVoidInstanceCuttingElement(Element)`
> - `void RemoveInstanceVoidCut(Document, Element element, Element cuttingInstance)`
> `CutFailureReason` is an enum (report `reason.ToString()` when a pair is ineligible).

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelCutTools.cs`
- Modify: `StructuralSteelTools.cs` (+8 wrappers), `ToolRegistrationTests.cs` (170 → 178)

These wrap `SolidSolidCutUtils` + `InstanceVoidCutUtils` (generic Revit APIs — results MUST state the cut is generic geometry, not steel-specific).

- [ ] **Step 1: Create the file with `add_steel_solid_cut` (FULL) + `check_steel_cut_eligibility` (FULL read)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.StructuralSteel;

/// <summary>Adds a solid-solid cut: cutElement cuts targetElement (SolidSolidCutUtils). Generic Revit geometry op.</summary>
public class AddSteelSolidCutTool : ICortexTool
{
    public string Name => "add_steel_solid_cut";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Add a solid cut so one element cuts another (SolidSolidCutUtils). Provide cutElementId and targetElementId. Optional splitFaces. Supports dryRun. Note: this is a generic Revit cut, not steel-specific.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;
        var splitFaces = input["splitFaces"]?.Value<bool?>() ?? false;

        // Eligibility BEFORE tx: SolidSolidCutUtils.CanElementCutElement(...) (confirm signature/overloads)
        bool canCut;
        try { canCut = SolidSolidCutUtils.CanElementCutElement(cutter!, target!, out _); }
        catch { try { canCut = SolidSolidCutUtils.IsAllowedForSolidCut(cutter!) && SolidSolidCutUtils.IsAllowedForSolidCut(target!); } catch { canCut = false; } }
        if (!canCut)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {ToolHelpers.GetElementIdValue(cutter!)} cannot cut {ToolHelpers.GetElementIdValue(target!)}",
                suggestion: "Use check_steel_cut_eligibility to test a pair first.");

        if (input["dryRun"]?.Value<bool?>() == true)
            return CortexResult<object>.Ok(new { dryRun = true, cutElementId = ToolHelpers.GetElementIdValue(cutter!), targetElementId = ToolHelpers.GetElementIdValue(target!), eligible = true });

        if (!session.RequestConfirmation("add solid cut", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Add Solid Cut");
        tx.Start();
        try
        {
            SolidSolidCutUtils.AddCutBetweenSolids(doc!, cutter!, target!, splitFaces);   // confirm signature
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = "Solid cut added (generic Revit geometry cut, not steel-specific)",
                cutElementId = ToolHelpers.GetElementIdValue(cutter!),
                targetElementId = ToolHelpers.GetElementIdValue(target!),
                splitFaces
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to add solid cut: {ex.Message}");
        }
    }
}

/// <summary>Reports whether one element may cut another (solid and/or void), without mutating.</summary>
public class CheckSteelCutEligibilityTool : ICortexTool
{
    public string Name => "check_steel_cut_eligibility";
    public string Category => "StructuralSteel";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Check whether one element can cut another via solid cut and/or instance void cut. Provide cutElementId and targetElementId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (cutter, e1) = StructuralSteelToolHelpers.RequireElement(doc!, input["cutElementId"]?.Value<long?>());
        if (e1 != null) return e1;
        var (target, e2) = StructuralSteelToolHelpers.RequireElement(doc!, input["targetElementId"]?.Value<long?>());
        if (e2 != null) return e2;
        bool solid = false, voidCut = false;
        try { solid = SolidSolidCutUtils.CanElementCutElement(cutter!, target!, out _); } catch { }
        try { voidCut = InstanceVoidCutUtils.CanBeCutWithVoid(target!); } catch { }   // confirm which arg is the cuttee
        return CortexResult<object>.Ok(new
        {
            cutElementId = ToolHelpers.GetElementIdValue(cutter!),
            targetElementId = ToolHelpers.GetElementIdValue(target!),
            solidCutEligible = solid,
            instanceVoidCutEligible = voidCut
        });
    }
}
```

> `CanElementCutElement` may have an `out failureReason`/different overload across versions — reflect and adapt the eligibility calls. Keep the contract.

- [ ] **Step 2: Build R25 + R24.**
- [ ] **Step 3: Append the other 6 Module-5 tools.** Contracts:
  - **`remove_steel_solid_cut`** (write): `cutElementId`,`targetElementId` → `SolidSolidCutUtils.RemoveCutBetweenSolids(doc, cutter, target)`.
  - **`set_steel_solid_cut_face_splitting`** (write): `cutElementId`,`targetElementId`,`split` (bool) → `SplitFacesOfCuttingSolid(...)`.
  - **`add_steel_instance_void_cut`** (write): `voidInstanceId`,`targetElementId` → eligibility via `CanBeCutWithVoid`, then `InstanceVoidCutUtils.AddInstanceVoidCut(doc, target, voidInstance)` (confirm arg order). DESTRUCTIVE-ish → confirm.
  - **`remove_steel_instance_void_cut`** (write): `voidInstanceId`,`targetElementId` → `RemoveInstanceVoidCut(...)`.
  - **`get_solid_cut_relationships`** (read): `elementId` → `GetCuttingSolids`/`GetSolidsBeingCut`; `maxResults`+`summaryOnly`.
  - **`get_instance_void_cut_relationships`** (read): `elementId` → `GetCuttingVoidInstances`/`GetElementsBeingCut`; `maxResults`+`summaryOnly`.

  Build R25 + R24 + R26.

- [ ] **Step 4: Add 8 wrappers. Build server.**
- [ ] **Step 5: Bump threshold 170 → 178 (Module 5 adds 8); run. PASS.**
- [ ] **Step 6: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelCutTools.cs src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(steel): add Module 5 solid + instance void cuts

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Module 6 — Provider & extension reporting (3 read tools)

> **API VERIFIED 2026-05-30 (reflection R25). The provider infra is NOT publicly queryable** — these
> tools are honest "not available via public API" reporters, exactly as the contracts already allow:
> - `StructuralConnectionsProviderRegistry`: only `Dispose()` + `IsValidObject` — NO public query method, NO public ctor. Cannot enumerate providers.
> - `StructuralConnectionsProviderData`: only `Dispose()` + `IsValidObject` — opaque, provider-filled via callback, not readable by us.
> - `IStructuralConnectionsProvider`: provider-implemented interface (`GetAvailableConnectionTypes`, `GetTypeInfo`, ...), not a queryable list.
> - `ConnectionValidationInfo`: has a public `.ctor()` + `GetWarning(int)`, `ManyWarnings()`, `IsValidWarningIndex(int)` — BUT no public method produces a populated instance from a placed handler (Revit fills it internally during provider validation). `ConnectionValidationWarning`: props `Reason` (`ConnectionWarning` enum: Unknown/Alignment/Size/Shape/Connectivity), `Resolution` (`ConnectionResolution` enum), `GetParts()`.
>
> So all 3 tools return `Ok` with `{ available:false, note:"..." }` (or, for validation, the handler's `CodeCheckingStatus` + the empty `ManyWarnings()` of a fresh info object) — never throw, never fabricate. This mirrors `StructuralSteelToolHelpers.AnyConnectionProviderInstalled()` which already returns false for the same reason.

**Files:**
- Create: `src/RevitCortex.Tools/StructuralSteel/StructuralSteelProviderTools.cs`
- Modify: `StructuralSteelTools.cs` (+3 wrappers), `ToolRegistrationTests.cs` (178 → 181)

All read-only. RevitCortex does NOT compile provider implementations from MCP input — these are discovery/reporting only.

- [ ] **Step 1: Create the file with all 3 tools.** Contracts:
  - **`get_structural_connection_provider_registry`** (read): enumerate registered providers via `StructuralConnectionsProviderRegistry` → ids/names/availability. If the registry can't be enumerated safely, return `{ count: 0, available: false, note: "..." }` (never throw).
  - **`get_structural_connection_provider_data`** (read): inputs a provider id/key → that provider's reported metadata/capabilities.
  - **`get_structural_connection_validation_info`** (read): inputs `connectionId` → `ConnectionValidationInfo`/`ConnectionValidationWarning` detail (the deeper read behind `get_steel_connection_validation`).

  Each follows the read skeleton; wrap registry/provider calls in try/catch → structured Fail or empty-with-note (provider infra is often absent).

- [ ] **Step 2: Build R25 + R24 + R26.**
- [ ] **Step 3: Add 3 wrappers. Build server.**
- [ ] **Step 4: Bump threshold 178 → 181 (Module 6 adds 3); run. PASS.**
- [ ] **Step 5: Commit**
```powershell
git add src/RevitCortex.Tools/StructuralSteel/StructuralSteelProviderTools.cs src/RevitCortex.Server/Tools/StructuralSteelTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(steel): add Module 6 provider + validation reporting

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

**End of Steps 2–7.** All 6 modules implemented: 57 tools + 57 wrappers.

---

## Task 9: Server forwarding source test

**Files:**
- Create: `src/RevitCortex.Tests/StructuralSteel/StructuralSteelServerForwardingSourceTests.cs`

Mirror `src/RevitCortex.Tests/Rebar/RebarServerForwardingSourceTests.cs` exactly (read it first). It combines source-text forwarding facts with reflection over wrapper names. **Critical lesson from rebar:** outside a Revit install, `Assembly.GetTypes()` on the Tools assembly throws `ReflectionTypeLoadException` and returns a NON-DETERMINISTIC loadable subset. So do NOT assert exact wrapper↔plugin counts; assert only the robust directions.

- [ ] **Step 1: Write the test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RevitCortex.Core.Tools;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.StructuralSteel;

public class StructuralSteelServerForwardingSourceTests
{
    private static string ReadSteelTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", "StructuralSteelTools.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void CreateGenericConnection_ForwardsElementIds()
    {
        var src = ReadSteelTools();
        Assert.Contains("create_generic_steel_connection", src);
        Assert.Contains("[\"elementIds\"]", src);
    }

    [Fact]
    public void AddSolidCut_ForwardsCutAndTarget()
    {
        var src = ReadSteelTools();
        Assert.Contains("[\"cutElementId\"] = cutElementId", src);
        Assert.Contains("[\"targetElementId\"] = targetElementId", src);
    }

    [Fact]
    public void EveryWrapper_ForwardsViaItsOwnDeclaredName()
    {
        var src = ReadSteelTools();
        var methods = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToList();
        Assert.NotEmpty(methods);
        foreach (var m in methods)
        {
            var name = m.GetCustomAttribute<McpServerToolAttribute>()!.Name;
            Assert.False(string.IsNullOrEmpty(name), $"{m.Name} has an empty McpServerTool name");
            Assert.True(src.Contains($"ExecuteAsync(\"{name}\""),
                $"Wrapper '{m.Name}' declares '{name}' but does not forward to ExecuteAsync(\"{name}\", ...)");
        }
    }

    [Fact]
    public void AllWrapperNames_AreUniqueAndSnakeCase()
    {
        var names = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        Assert.NotEmpty(names);
        var dups = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, $"Duplicate wrapper names: {string.Join(", ", dups)}");
        foreach (var n in names) Assert.Matches("^[a-z][a-z0-9_]*$", n);
    }

    [Fact]
    public void LoadablePluginSteelTools_AllHaveAWrapper()
    {
        var wrappers = typeof(StructuralSteelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        Assembly toolsAsm = typeof(RevitCortex.Tools.Meta.SayHelloTool).Assembly;
        IEnumerable<Type> types;
        try { types = toolsAsm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null)!; }

        var loadable = new List<string>();
        foreach (var t in types.Where(t => t.IsClass && !t.IsAbstract && typeof(ICortexTool).IsAssignableFrom(t)))
        {
            ICortexTool inst;
            try { inst = (ICortexTool)Activator.CreateInstance(t)!; } catch { continue; }
            if (inst.Category == "StructuralSteel") loadable.Add(inst.Name);
        }
        var missing = loadable.Where(p => !wrappers.Contains(p)).ToList();
        Assert.True(missing.Count == 0,
            $"These loadable steel plugin tools have no server wrapper: {string.Join(", ", missing)}");
    }
}
```

- [ ] **Step 2: Run; fix any wrapper key/name mismatch in `StructuralSteelTools.cs`.** `dotnet test ... --filter "FullyQualifiedName~StructuralSteelServerForwardingSourceTests"` → PASS. (If `EveryWrapper_ForwardsViaItsOwnDeclaredName` fails, a wrapper's `[McpServerTool] Name` ≠ its `ExecuteAsync` string — fix it. If `AddSolidCut`/`CreateGenericConnection` source asserts fail, the actual wrapper keys differ from the plan's examples — align the assert to the real keys OR the keys to the plugin reads, whichever is wrong.)

- [ ] **Step 3: Commit**
```powershell
git add src/RevitCortex.Tests/StructuralSteel/StructuralSteelServerForwardingSourceTests.cs
git commit -m "test(steel): assert wrappers forward correct keys + names map to plugin tools

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Full multi-target build, schema regen, final count

**Files:**
- Modify: `tool-schemas.txt` (regenerated), `ToolRegistrationTests.cs` (final count verified)

- [ ] **Step 1: Build all 5 plugin targets**
```powershell
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Expected: R23/R24/R25/R26 → 0 errors. R27 → 0 errors iff the .NET 10 SDK is present; `NETSDK1045` otherwise is an environment gap (record + continue). ALSO build the Tools csproj explicitly for R24 + R26 + R27 (R27 exercises the `manage_custom_steel_connection_type` `#else`):
```powershell
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R27" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 2: Build the server** — `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj` → 0 errors.

- [ ] **Step 3: Regenerate `tool-schemas.txt`** — `node server/generate-tool-schemas-csharp.mjs`. Confirm the 57 steel tools appear:
```powershell
Select-String -Path tool-schemas.txt -Pattern "steel|structural_connection|solid_cut|void_cut|fabrication" | Measure-Object
```
Expected: ≥57.

- [ ] **Step 4: Verify final count** — `ToolCount_MatchesExpected` reads `>= BASE + 57`. Confirm the value set incrementally in Tasks 3/4/5/6/7/8 lands at `BASE+57`.

- [ ] **Step 5: Run the FULL no-Revit suite** — `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`. Expected: all pass; only `[RequiresRevitApiFact]` skips. Record the numbers (baseline + 5 parser + 3 contract + 5 forwarding + new InlineData rows).

- [ ] **Step 6: Commit**
```powershell
git add tool-schemas.txt src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "chore(steel): regenerate tool-schemas, finalize tool count

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Documentation + manual smoke-test checklist

**Files:**
- Modify: `docs/USER_GUIDE.md`, `WORKFLOWS.md`

- [ ] **Step 1: Add a "Structural Steel" section to `docs/USER_GUIDE.md`.** Read the file first to match its structure (it's Italian — match the convention; tool names/OST/enum/GUID stay verbatim). Include: intro (57 tools, category `StructuralSteel`, mm/degrees, check `get_structural_steel_api_capabilities` first, prefer `create_generic_steel_connection` when providers unknown); a per-module tool table; 3–4 worked examples using the EXACT param names from `tool-schemas.txt` — at minimum (a) capabilities + `list_steel_connection_handlers`, (b) `create_generic_steel_connection` with `elementIds`, (c) `get_steel_element_properties`, (d) `add_steel_solid_cut`. **Input-shape note (verify against tool-schemas.txt before writing — the rebar docs initially shipped WRONG shapes):** array params (`elementIds`) are JSON arrays of integers; point/vector params (`inputPoints`, coordinates) are JSON objects `{"x":..,"y":..,"z":..}` in mm, NOT flat arrays. A "Known limitations" subsection: provider-dependent typed connections, IntPtr buffers excluded, custom-connection mutation unavailable in R27, cuts are generic Revit geometry.

- [ ] **Step 2: Append a "Structural Steel Workflows" section to `WORKFLOWS.md`** (match its session format): discover steel setup; create a generic connection; inspect inputs/failed state; attach + inspect fabrication info; manage warnings; create + inspect cuts. Operational warning: detailed connections depend on installed providers + steel-compatible families → prefer `create_generic_steel_connection` when provider availability is unknown; cut tools are generic Revit geometry (results say whether steel-specific).

- [ ] **Step 3: Verify docs accuracy** — every tool name backticked must be in the 57-list / tool-schemas.txt; example param names + shapes match tool-schemas.txt; limitations accurate (not overstated). No `.cs`/`tool-schemas.txt` edits in this task.

- [ ] **Step 4: Commit**
```powershell
git add docs/USER_GUIDE.md WORKFLOWS.md
git commit -m "docs(steel): add structural steel section + workflows

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Manual smoke tests in Revit (record results; needs a steel model + ideally a connection provider).** Deploy all 5 targets via `deploy.ps1`, restart Revit, Cortex Switch on, then exercise the spec's checklist: capabilities; list handler/connection types; `create_generic_steel_connection` between two valid members; read connection inputs/origin/failed/approval; add+remove an input element; add fabrication info + read/resolve fabrication unique id; register a material link + read back; create+remove a solid cut between eligible elements; create+remove an instance void cut (where a void family exists); read-only mode blocks a write tool; cancelled TaskDialog → `Cancelled`. Typed-connection / approval / custom-connection tools: confirm they return the structured "provider unavailable" / R27 error when the provider/version isn't there.

---

## Self-review checklist (run before handing off)

- [ ] **Spec coverage:** every tool in the spec's Modules 1–6 maps to a task — M1→T2/T3 (15), M2→T4 (9), M3→T5 (9), M4→T6 (13), M5→T7 (8), M6→T8 (3) = **57**. Provider/extension infra is read-only reporting only (spec "Module 6" + "no compiled providers from MCP"). IntPtr buffers excluded (spec decision). `summaryOnly`+`maxResults` on heavy reads (spec decision #3).
- [ ] **No placeholders:** the fully-coded tools (helpers, capabilities, list_handlers, get_steel_element_properties, create_generic_steel_connection, create_steel_structural_connection_type, add_steel_fabrication_info, add_steel_solid_cut, check_steel_cut_eligibility) have complete bodies. Remaining tools have explicit input/method/return contracts naming only spec-listed APIs, each tagged with the reflect-verify mandate.
- [ ] **Type consistency:** `StructuralSteelToolHelpers` member names (`ToMm`/`FromMm`/`ParseEnum`/`ParseGuid`/`ParseLongArray`/`ParseConnectionInputAction`/`ParseXyzMm`/`RequireElement`/`RequireConnectionHandler`/`ResolveElementIds`/`MinVersionError`/`ProviderUnavailableError`/`AnyConnectionProviderInstalled`) are used identically across tasks. Real existing helpers: `ToolHelpers.ToElementId`/`GetElementIdValue`/`RequireDocument`.
- [ ] **Version/provider gating:** R27 `manage_custom_steel_connection_type` has a compiling `#else`; typed-connection/approval tools gate on provider availability → `ProviderUnavailableError`; gated tools verified by R23/R24/R27 builds.
- [ ] **Read-only correctness:** all read tools use `get_`/`list_`/`analyze_`/`check_`; all write tools (`create_`/`set_`/`modify_`/`manage_`/`add_`/`remove_`/`delete_`/`attach_`/`register_`/`post_`/`clear_`/`flush_`/`mark_`) do NOT → blocked in read-only mode. Confirmed by Task 3 InlineData.
- [ ] **Branch baseline:** count bumps are relative to `BASE` (read in Task 1 Step 0), NOT an assumed 195 (this branch is off `main`, no rebar).


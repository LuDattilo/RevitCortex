# Complete Rebar API — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a complete, practical RevitCortex MCP surface for Revit reinforcement (rebar) workflows — discovery/inventory, shape-driven & free-form bars, area/path/fabric systems, couplers/splices/constraints, settings/numbering/rounding/bending details — across Revit 2023→2027.

**Architecture:** A new first-class `Rebar` tool category. Each tool is an `ICortexTool` in `src/RevitCortex.Tools/Rebar/`, auto-registered by reflection (`CortexRouter.RegisterToolsFromAssembly`). Each gets a thin server-side MCP wrapper (`[McpServerTool]` static method in `src/RevitCortex.Server/Tools/RebarTools.cs`) that builds a `JObject` and forwards via `revit.ExecuteAsync`. Shared logic (id/type/shape/hook resolution, mm⇄ft conversion, curve & layout & termination parsing, version-gated helpers) lives in `RebarToolHelpers`. Newer-API features (2024+ bending details, 2025+ splices, 2026+ terminations/crank) are isolated behind `#if REVIT2024_OR_GREATER` / `REVIT2025_OR_GREATER` / `REVIT2026_OR_GREATER` and return a structured `InvalidInput` "needs Revit ≥ N" error on older targets.

**Tech Stack:** C# multi-target (net48 for R23/R24, net8.0-windows for R25/R26, net10.0-windows for R27), `Autodesk.Revit.DB.Structure`, Newtonsoft.Json, ModelContextProtocol SDK, xUnit. No `ImplicitUsings` — every file needs explicit `using` directives. No `record`/`init`/`Index`/`Range`/`GetValueOrDefault` (net48 breaks).

---

## Design source

This plan implements the approved spec: `docs/superpowers/specs/2026-05-30-rebar-api-complete-design.md`. Read it first. The API surface was verified against the Nice3point RevitAPI XML docs (2023.1.90 → 2027.0.20) and the Autodesk online reference for `Autodesk.Revit.DB.Structure`.

## Non-negotiable project rules (apply to EVERY task)

1. Every plugin tool returns `CortexResult<object>` — never throws to the caller, never returns a raw string.
2. Every model-changing tool calls `session.RequestConfirmation("<verb>", count)` BEFORE opening its `Transaction`; on `false` return `CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user")`.
3. All ids are validated BEFORE the transaction opens.
4. MCP inputs/outputs use **millimeters** and **degrees**; Revit-internal feet/radians are never the user-facing unit. Conversion: `1 ft = 304.8 mm`; `radians = degrees * Math.PI / 180`.
5. Language-independent identifiers only: element ids, `OST_*` categories, type ids, enum names as strings. Never read/write rebar params by localized display name.
6. Read-only tools MUST start with `get_`, `list_`, or `analyze_`. Write tools MUST NOT.
7. After editing any C# file, build BOTH `Debug R25` and `Debug R24` before moving on. A green R25 build does NOT prove R24 compiles. (`RevitCortex.Tools` errors can be masked by a green Plugin build — build the Tools csproj or run the test project.)
8. `ElementId`: use `ToolHelpers.ToElementId(long)` and `ToolHelpers.GetElementIdValue(...)` — they already wrap the R2023 `int` vs R2024+ `long` difference. Never hand-roll `new ElementId(...)` in rebar code.

## Build / test commands (used throughout)

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
node server/generate-tool-schemas-csharp.mjs
```

Scope a single test class with `--filter "FullyQualifiedName~RebarToolContractTests"`.

---

## File Structure

### New plugin tool files (`src/RevitCortex.Tools/Rebar/`)

| File | Responsibility |
|------|---------------|
| `RebarToolHelpers.cs` | Shared static helpers: doc/id/type/shape/hook/cover resolution, mm⇄ft, curve-spec & layout-spec & termination-spec parsing, curve→DTO, version-gated wrappers, `MinVersionError`. |
| `RebarDiscoveryTools.cs` | Module 1 read-only tools: `list_rebar_bar_types`, `list_rebar_hook_types`, `list_rebar_shapes`, `list_rebar_cover_types`, `list_rebar_splice_types`, `list_rebar_fabric_types`, `get_rebar_host_data`, `get_rebar_element_data`, `get_rebar_geometry`, `get_rebar_constraints`, `get_reinforcement_settings`, `get_rebar_api_capabilities`. |
| `RebarCreationTools.cs` | Module 2 bar tools: `create_rebar_from_curves`, `create_rebar_from_shape`, `create_free_form_rebar`, `set_rebar_layout`, `set_rebar_shape`, `set_rebar_hooks`, `set_rebar_terminations`, `set_rebar_host`, `set_rebar_visibility`, `move_rebar_in_set`, `include_exclude_rebar_bars`, `split_rebar`. |
| `RebarSystemTools.cs` | Module 3 area/path tools: `create_area_reinforcement`, `create_path_reinforcement`, `set_area_reinforcement_layers`, `set_path_reinforcement_options`, `convert_rebar_system_to_rebars`, `remove_rebar_system`, `get_area_reinforcement_data`, `get_path_reinforcement_data`. |
| `FabricReinforcementTools.cs` | Module 4 fabric tools: `create_fabric_area`, `create_fabric_sheet`, `place_fabric_sheet`, `set_fabric_sheet_bend_profile`, `remove_fabric_reinforcement_system`, `get_fabric_area_data`, `get_fabric_sheet_data`, `get_fabric_wire_data`. |
| `RebarAdvancedTools.cs` | Module 5 advanced tools: `manage_rebar_constraints`, `propagate_rebar`, `create_rebar_coupler`, `set_rebar_coupler_visibility`, `splice_rebar`, `unify_rebars`, `remove_rebar_splice`, `transfer_rebar_annotations`, `get_rebar_constraint_candidates`, `get_rebar_coupler_data`, `get_rebar_splice_data`, `get_rebar_splice_candidates`. |
| `RebarSettingsTools.cs` | Module 6 settings tools: `set_reinforcement_settings`, `manage_rebar_rounding`, `manage_fabric_rounding`, `manage_rebar_numbering`, `create_rebar_bending_detail`, `modify_rebar_bending_detail`, `get_rebar_rounding`, `get_fabric_rounding`, `get_rebar_numbering`, `get_rebar_bending_detail_data`. |

### New server wrapper file

| File | Responsibility |
|------|---------------|
| `src/RevitCortex.Server/Tools/RebarTools.cs` | `[McpServerToolType]` static class with one `[McpServerTool]` method per plugin tool, forwarding via `revit.ExecuteAsync`. |

### New test files (`src/RevitCortex.Tests/Rebar/`)

| File | Responsibility |
|------|---------------|
| `RebarHelpersParsingTests.cs` | No-Revit unit tests for `RebarToolHelpers` pure parsers (layout spec, termination spec, enum parse, mm⇄ft) — these are extracted as Revit-free static methods so they CAN be unit-tested. |
| `RebarServerContractTests.cs` | Reflection tests over `RebarTools.cs` method signatures/descriptions (mirrors `ServerToolContractTests`). |
| `RebarServerForwardingSourceTests.cs` | Source-text assertions that wrappers forward the right `JObject` keys (mirrors `ServerToolForwardingSourceTests`). |

### Modified files

| File | Change |
|------|--------|
| `src/RevitCortex.Plugin/CortexRouter.cs` | (Already covers `get_`, `list_`, `analyze_`.) No change needed unless a new read prefix is introduced — it is not. **Verify only.** |
| `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` | Bump `ToolCount_MatchesExpected` threshold from `133` to the new minimum as tools land. |
| `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs` | Add `[InlineData]` rows asserting representative rebar read tools are read-only and rebar write tools are not. |
| `docs/USER_GUIDE.md` | New "Rebar / Reinforcement" section. |
| `tool-schemas.txt` | Regenerate via `node server/generate-tool-schemas-csharp.mjs`. |
| `WORKFLOWS.md` | Reinforcement workflows + operational warning. |

**Note on units:** unlike the IFC plan there is **no TypeScript** layer — the current MCP server is C# (`RevitCortex.Server/Tools/*.cs`). All wrappers are C# `[McpServerTool]` methods.

---

## Implementation order (7 steps, each must build + test before the next)

1. **Step 1 — Shared helpers + discovery** (Tasks 1–4): `RebarToolHelpers`, `RebarDiscoveryTools`, server wrappers for Module 1, helper unit tests.
2. **Step 2 — Shape-driven & free-form bars** (Tasks 5–7).
3. **Step 3 — Area/path reinforcement** (Tasks 8–9).
4. **Step 4 — Fabric reinforcement** (Tasks 10–11).
5. **Step 5 — Constraints/propagation/couplers/splices** (Tasks 12–13).
6. **Step 6 — Settings/numbering/rounding/bending details** (Tasks 14–15).
7. **Step 7 — Docs, schema regen, full verification** (Tasks 16–18).

---

## Shared constants & conventions used in code blocks

```csharp
private const double MmPerFoot = 304.8;
```

Standard tool skeleton (every tool follows this):

```csharp
public CortexResult<object> Execute(JObject input, CortexSession session)
{
    var (doc, error) = ToolHelpers.RequireDocument(session);
    if (error != null) return error;
    // ... validate, (confirm if write), (transaction if write), return Ok/Fail
}
```

---

## Task 1: RebarToolHelpers — shared utilities (with Revit-free pure parsers)

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/RebarToolHelpers.cs`
- Test: `src/RevitCortex.Tests/Rebar/RebarHelpersParsingTests.cs`

The helper splits into two halves: **Revit-dependent** resolvers (need a `Document`, only run inside Revit) and **pure** parsers (`LayoutSpec`/enum/unit/curve-DTO — no `Document` needed). We TDD the pure half. `ParseXyzMm`/`ParseCurveSpecsMm` return Revit `XYZ`/`Curve` objects, so their tests are part of the in-Revit smoke checks, not the no-Revit unit suite.

- [ ] **Step 1: Write the failing parser tests**

```csharp
using Newtonsoft.Json.Linq;
using RevitCortex.Tools.Rebar;
using Xunit;

namespace RevitCortex.Tests.Rebar;

public class RebarHelpersParsingTests
{
    [Fact]
    public void ToMm_And_FromMm_RoundTrip()
    {
        Assert.Equal(304.8, RebarToolHelpers.ToMm(1.0), 6);
        Assert.Equal(1.0, RebarToolHelpers.FromMm(304.8), 6);
    }

    [Fact]
    public void ParseLayoutSpec_FixedNumber_Parses()
    {
        var json = JObject.Parse(@"{""rule"":""fixed_number"",""number"":10,""arrayLengthMm"":1500,
            ""barsOnNormalSide"":true,""includeFirstBar"":true,""includeLastBar"":false}");
        var spec = RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.Null(err);
        Assert.Equal(RebarToolHelpers.LayoutRuleKind.FixedNumber, spec.Rule);
        Assert.Equal(10, spec.Number);
        Assert.Equal(1500, spec.ArrayLengthMm, 6);
        Assert.True(spec.BarsOnNormalSide);
        Assert.True(spec.IncludeFirstBar);
        Assert.False(spec.IncludeLastBar);
    }

    [Fact]
    public void ParseLayoutSpec_UnknownRule_ReturnsError()
    {
        var json = JObject.Parse(@"{""rule"":""banana""}");
        RebarToolHelpers.ParseLayoutSpec(json, out var err);
        Assert.NotNull(err);
        Assert.Contains("rule", err);
    }

    [Fact]
    public void ParseEnum_Valid_And_Invalid()
    {
        var ok = RebarToolHelpers.ParseEnum<RebarLayoutKindProbe>("FixedNumber", "rule", out var err1);
        Assert.Null(err1);
        Assert.Equal(RebarLayoutKindProbe.FixedNumber, ok);

        RebarToolHelpers.ParseEnum<RebarLayoutKindProbe>("nope", "rule", out var err2);
        Assert.NotNull(err2);
        Assert.Contains("rule", err2);
        Assert.Contains("Single", err2); // error lists valid values
    }
}

// Revit-free enum used only to exercise the generic parser.
public enum RebarLayoutKindProbe { Single, FixedNumber }
```

- [ ] **Step 2: Run the tests, verify they fail**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~RebarHelpersParsingTests"`
Expected: FAIL — `RebarToolHelpers` does not exist.

- [ ] **Step 3: Create `RebarToolHelpers`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Shared helpers for all Rebar tools. Pure parsers (LayoutSpec/enum/unit/curve-DTO) carry no
/// Document dependency and are unit-tested in RebarHelpersParsingTests. Revit-dependent
/// resolvers require a Document and only run inside Revit.
/// </summary>
public static class RebarToolHelpers
{
    public const double MmPerFoot = 304.8;

    public static double ToMm(double feet) => feet * MmPerFoot;
    public static double FromMm(double mm) => mm / MmPerFoot;

    // ── Pure: enum parsing with helpful error ────────────────────────────────
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

    // ── Pure: layout spec ────────────────────────────────────────────────────
    public enum LayoutRuleKind { Single, FixedNumber, MaximumSpacing, NumberWithSpacing, MinimumClearSpacing }

    public class LayoutSpec
    {
        public LayoutRuleKind Rule;
        public int Number = 2;
        public double ArrayLengthMm;
        public double SpacingMm;
        public bool BarsOnNormalSide = true;
        public bool IncludeFirstBar = true;
        public bool IncludeLastBar = true;
    }

    public static LayoutSpec ParseLayoutSpec(JObject json, out string? error)
    {
        error = null;
        var spec = new LayoutSpec();
        var ruleStr = json["rule"]?.Value<string>();
        switch ((ruleStr ?? "").Trim().ToLowerInvariant())
        {
            case "single": spec.Rule = LayoutRuleKind.Single; break;
            case "fixed_number": spec.Rule = LayoutRuleKind.FixedNumber; break;
            case "maximum_spacing": spec.Rule = LayoutRuleKind.MaximumSpacing; break;
            case "number_with_spacing": spec.Rule = LayoutRuleKind.NumberWithSpacing; break;
            case "minimum_clear_spacing": spec.Rule = LayoutRuleKind.MinimumClearSpacing; break;
            default:
                error = "Invalid layout 'rule'. Valid: single, fixed_number, maximum_spacing, number_with_spacing, minimum_clear_spacing";
                return spec;
        }
        spec.Number = json["number"]?.Value<int?>() ?? 2;
        spec.ArrayLengthMm = json["arrayLengthMm"]?.Value<double?>() ?? 0;
        spec.SpacingMm = json["spacingMm"]?.Value<double?>() ?? 0;
        spec.BarsOnNormalSide = json["barsOnNormalSide"]?.Value<bool?>() ?? true;
        spec.IncludeFirstBar = json["includeFirstBar"]?.Value<bool?>() ?? true;
        spec.IncludeLastBar = json["includeLastBar"]?.Value<bool?>() ?? true;
        return spec;
    }

    // ── Pure: XYZ + curve specs (use Revit XYZ/Curve, math is unit-only) ──────
    public static XYZ ParseXyzMm(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(FromMm(x), FromMm(y), FromMm(z));
    }

    /// <summary>Parse a curve-spec array ([{type:line|arc, start, end, mid?}]) in mm into bound Curves.</summary>
    public static IList<Curve> ParseCurveSpecsMm(JArray specs, out string? error)
    {
        error = null;
        var curves = new List<Curve>();
        foreach (var item in specs.OfType<JObject>())
        {
            var type = (item["type"]?.Value<string>() ?? "line").Trim().ToLowerInvariant();
            try
            {
                if (type == "line")
                    curves.Add(Line.CreateBound(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!)));
                else if (type == "arc")
                    curves.Add(Arc.Create(ParseXyzMm(item["start"]!), ParseXyzMm(item["end"]!), ParseXyzMm(item["mid"]!)));
                else { error = $"Unknown curve type '{type}'. Use 'line' or 'arc'."; return curves; }
            }
            catch (Exception ex) { error = $"Invalid curve geometry: {ex.Message}"; return curves; }
        }
        if (curves.Count == 0) error = "No curves parsed from spec array.";
        return curves;
    }

    public static JObject XyzToDtoMm(XYZ p) => new JObject
    {
        ["x"] = ToMm(p.X), ["y"] = ToMm(p.Y), ["z"] = ToMm(p.Z)
    };

    public static JObject CurveToDtoMm(Curve c)
    {
        var dto = new JObject
        {
            ["type"] = c is Arc ? "arc" : (c is Line ? "line" : c.GetType().Name.ToLowerInvariant()),
            ["start"] = XyzToDtoMm(c.GetEndPoint(0)),
            ["end"] = XyzToDtoMm(c.GetEndPoint(1)),
            ["lengthMm"] = ToMm(c.Length)
        };
        if (c is Arc arc) dto["mid"] = XyzToDtoMm(arc.Evaluate(0.5, true));
        return dto;
    }

    // ── Revit-dependent resolvers ────────────────────────────────────────────
    public static (Rebar? rebar, CortexResult<object>? error) RequireRebar(Document doc, long? rebarId)
    {
        if (rebarId == null || rebarId <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "rebarId is required"));
        var rebar = doc.GetElement(ToolHelpers.ToElementId(rebarId.Value)) as Rebar;
        if (rebar == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"No Rebar element with id {rebarId}",
                suggestion: "Use get_rebar_host_data or list_* to find rebar ids"));
        return (rebar, null);
    }

    public static (Element? host, CortexResult<object>? error) RequireHost(Document doc, long? hostId)
    {
        if (hostId == null || hostId <= 0)
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostId is required"));
        var host = doc.GetElement(ToolHelpers.ToElementId(hostId.Value));
        if (host == null)
            return (null, CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {hostId}"));
        if (!RebarHostData.IsValidHost(host))
            return (null, CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Element {hostId} ({host.Category?.Name}) is not a valid rebar host",
                suggestion: "Host must be a structural concrete beam/column/wall/floor/foundation. Mark it structural or set a concrete material first."));
        return (host, null);
    }

    public static RebarBarType? ResolveRebarBarType(Document doc, long? typeId, string? typeName)
    {
        if (typeId.HasValue && typeId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(typeId.Value)) is RebarBarType byId) return byId;
        var all = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var byName = all.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return all.FirstOrDefault();
    }

    public static RebarHookType? ResolveRebarHookType(Document doc, long? hookId, string? hookName)
    {
        if (hookId.HasValue && hookId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(hookId.Value)) is RebarHookType byId) return byId;
        if (!string.IsNullOrWhiteSpace(hookName))
            return new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .FirstOrDefault(h => h.Name.Equals(hookName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public static RebarShape? ResolveRebarShape(Document doc, long? shapeId, string? shapeName)
    {
        if (shapeId.HasValue && shapeId > 0 &&
            doc.GetElement(ToolHelpers.ToElementId(shapeId.Value)) is RebarShape byId) return byId;
        if (!string.IsNullOrWhiteSpace(shapeName))
            return new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .FirstOrDefault(s => s.Name.Equals(shapeName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>Apply a parsed LayoutSpec to a shape-driven accessor (call inside a transaction).</summary>
    public static void ApplyLayout(RebarShapeDrivenAccessor acc, LayoutSpec s)
    {
        switch (s.Rule)
        {
            case LayoutRuleKind.Single:
                acc.SetLayoutAsSingle(); break;
            case LayoutRuleKind.FixedNumber:
                acc.SetLayoutAsFixedNumber(s.Number, FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.MaximumSpacing:
                acc.SetLayoutAsMaximumSpacing(FromMm(s.SpacingMm), FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.NumberWithSpacing:
                acc.SetLayoutAsNumberWithSpacing(s.Number, FromMm(s.SpacingMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
            case LayoutRuleKind.MinimumClearSpacing:
                acc.SetLayoutAsMinimumClearSpacing(FromMm(s.SpacingMm), FromMm(s.ArrayLengthMm), s.BarsOnNormalSide, s.IncludeFirstBar, s.IncludeLastBar); break;
        }
    }

    /// <summary>Standard "needs a newer Revit" structured error.</summary>
    public static CortexResult<object> MinVersionError(string feature, int minYear)
        => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            $"{feature} requires Revit {minYear} or newer; the active target does not support it.",
            suggestion: $"Open the model in Revit {minYear}+ to use this feature.");
}
```

- [ ] **Step 4: Run the parser tests, verify they pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~RebarHelpersParsingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Build R25 and R24 for the Tools project**

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: both succeed. (R24 = net48: confirms no `record`/`init`/`Range`/`GetValueOrDefault`.)

- [ ] **Step 6: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarToolHelpers.cs src/RevitCortex.Tests/Rebar/RebarHelpersParsingTests.cs
git commit -m "feat(rebar): add RebarToolHelpers shared utilities + parser tests"
```

---

## Task 2: Module 1 — Discovery & inventory tools

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/RebarDiscoveryTools.cs`

All 12 tools live in one file (each is small and read-only). Names use the `get_`/`list_` prefix → automatically classified read-only by `CortexRouter.IsReadOnlyTool`. All `IsDynamic => false`, `RequiresDocument => true`. No transaction (read-only). Each `Execute` follows: `RequireDocument` → collect/read → `Ok`. Wrap Revit calls in try/catch → `Fail(Unknown, ...)`.

This task implements the file in increments; each increment compiles. There are no no-Revit unit tests for these (they need a live `Document`); they are covered by the registration test (Task 4), the server contract test (Task 3), and manual smoke tests (Task 18).

- [ ] **Step 1: Create the file with `list_rebar_bar_types`, `list_rebar_hook_types`, `list_rebar_shapes`, `list_rebar_cover_types`**

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

namespace RevitCortex.Tools.Rebar;

/// <summary>Lists all RebarBarType elements (id, name, model/nominal diameter in mm).</summary>
public class ListRebarBarTypesTool : ICortexTool
{
    public string Name => "list_rebar_bar_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar bar types (id, name, model and nominal diameter in mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .Select(t => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(t),
                    ["name"] = t.Name,
                    ["modelDiameterMm"] = RebarToolHelpers.ToMm(t.BarModelDiameter),
                    ["nominalDiameterMm"] = RebarToolHelpers.ToMm(t.BarNominalDiameter)
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, barTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar bar types: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarHookType elements (id, name, hook angle in degrees).</summary>
public class ListRebarHookTypesTool : ICortexTool
{
    public string Name => "list_rebar_hook_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar hook types (id, name, hook angle in degrees).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .Select(h => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(h),
                    ["name"] = h.Name,
                    ["hookAngleDegrees"] = h.HookAngle * 180.0 / Math.PI,
                    ["style"] = h.Style.ToString()
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, hookTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar hook types: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarShape elements (id, name).</summary>
public class ListRebarShapesTool : ICortexTool
{
    public string Name => "list_rebar_shapes";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar shapes (id, name).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .Select(s => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(s),
                    ["name"] = s.Name
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, shapes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar shapes: {ex.Message}");
        }
    }
}

/// <summary>Lists all RebarCoverType elements (id, name, clear cover in mm).</summary>
public class ListRebarCoverTypesTool : ICortexTool
{
    public string Name => "list_rebar_cover_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List all rebar cover types (id, name, cover distance in mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>()
                .Select(c => new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(c),
                    ["name"] = c.Name,
                    ["coverDistanceMm"] = RebarToolHelpers.ToMm(c.CoverDistance)
                }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, coverTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list rebar cover types: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools)**

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: both succeed.

- [ ] **Step 3: Append `list_rebar_fabric_types` and `get_rebar_host_data` to the same file**

```csharp
/// <summary>Lists fabric reinforcement types (FabricSheetType + FabricAreaType).</summary>
public class ListRebarFabricTypesTool : ICortexTool
{
    public string Name => "list_rebar_fabric_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List fabric reinforcement types (fabric sheet types and fabric area types) with id and name.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var sheetTypes = new FilteredElementCollector(doc!).OfClass(typeof(FabricSheetType)).Cast<FabricSheetType>()
                .Select(t => new JObject { ["id"] = ToolHelpers.GetElementIdValue(t), ["name"] = t.Name }).ToList();
            var areaTypes = new FilteredElementCollector(doc!).OfClass(typeof(FabricAreaType)).Cast<FabricAreaType>()
                .Select(t => new JObject { ["id"] = ToolHelpers.GetElementIdValue(t), ["name"] = t.Name }).ToList();
            return CortexResult<object>.Ok(new
            {
                fabricSheetTypeCount = sheetTypes.Count,
                fabricSheetTypes = sheetTypes,
                fabricAreaTypeCount = areaTypes.Count,
                fabricAreaTypes = areaTypes
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list fabric types: {ex.Message}");
        }
    }
}

/// <summary>Reports reinforcement hosted in an element: validity, rebar/area/path/fabric counts, common cover.</summary>
public class GetRebarHostDataTool : ICortexTool
{
    public string Name => "get_rebar_host_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Report reinforcement hosted by an element: whether it is a valid host, and the ids of rebar, area, path and fabric reinforcement it contains, plus common cover.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var hostId = input["hostId"]?.Value<long?>();
        if (hostId == null || hostId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostId is required");
        var host = doc!.GetElement(ToolHelpers.ToElementId(hostId.Value));
        if (host == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No element with id {hostId}");

        try
        {
            var isValid = RebarHostData.IsValidHost(host);
            var data = RebarHostData.GetRebarHostData(host);
            if (data == null)
                return CortexResult<object>.Ok(new { hostId, isValidHost = isValid, hasHostData = false });

            List<long> Ids<T>(IEnumerable<T> els) where T : Element =>
                els.Select(e => ToolHelpers.GetElementIdValue(e)).ToList();

            var rebars = Ids(data.GetRebarsInHost());
            var areas = Ids(data.GetAreaReinforcementsInHost());
            var paths = Ids(data.GetPathReinforcementsInHost());
            var fabricSheets = Ids(data.GetFabricSheetsInHost());
            var fabricAreas = Ids(data.GetFabricAreasInHost());

            JObject? commonCover = null;
            try
            {
                var cover = data.GetCommonCoverType();
                if (cover != null)
                    commonCover = new JObject
                    {
                        ["id"] = ToolHelpers.GetElementIdValue(cover),
                        ["name"] = cover.Name,
                        ["coverDistanceMm"] = RebarToolHelpers.ToMm(cover.CoverDistance)
                    };
            }
            catch { /* faces may have mixed cover; leave null */ }

            return CortexResult<object>.Ok(new
            {
                hostId,
                hostCategory = host.Category?.Name,
                isValidHost = isValid,
                hasHostData = true,
                rebarCount = rebars.Count, rebarIds = rebars,
                areaReinforcementCount = areas.Count, areaReinforcementIds = areas,
                pathReinforcementCount = paths.Count, pathReinforcementIds = paths,
                fabricSheetCount = fabricSheets.Count, fabricSheetIds = fabricSheets,
                fabricAreaCount = fabricAreas.Count, fabricAreaIds = fabricAreas,
                commonCover
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read host data: {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Build R25 + R24 (Tools)** — same commands as Step 2. Expected: both succeed.

- [ ] **Step 5: Append `get_rebar_element_data` and `get_rebar_geometry`**

```csharp
/// <summary>Reads a single rebar's core data: type, host, layout rule, quantity, total length, volume.</summary>
public class GetRebarElementDataTool : ICortexTool
{
    public string Name => "get_rebar_element_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a single rebar's core data: bar type, host id, shape, layout rule, bar count, total length (mm) and volume.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        try
        {
            var typeId = rebar!.GetTypeId();
            var barType = doc!.GetElement(typeId) as RebarBarType;
            var hostId = rebar.GetHostId();
            var result = new JObject
            {
                ["rebarId"] = ToolHelpers.GetElementIdValue(rebar),
                ["barTypeId"] = ToolHelpers.GetElementIdValue(typeId),
                ["barTypeName"] = barType?.Name,
                ["barDiameterMm"] = barType != null ? RebarToolHelpers.ToMm(barType.BarNominalDiameter) : (double?)null,
                ["hostId"] = ToolHelpers.GetElementIdValue(hostId),
                ["isShapeDriven"] = rebar.IsRebarShapeDriven(),
                ["isFreeForm"] = rebar.IsRebarFreeForm(),
                ["layoutRule"] = rebar.LayoutRule.ToString(),
                ["numberOfBarPositions"] = rebar.NumberOfBarPositions,
                ["quantity"] = rebar.Quantity,
                ["totalLengthMm"] = RebarToolHelpers.ToMm(rebar.TotalLength),
                ["volumeCuMm"] = rebar.Volume * Math.Pow(RebarToolHelpers.MmPerFoot, 3),
                ["scheduleMark"] = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK)?.AsString()
            };
            if (rebar.IsRebarShapeDriven())
            {
                var shapeId = rebar.GetShapeId();
                result["shapeId"] = ToolHelpers.GetElementIdValue(shapeId);
                result["shapeName"] = (doc.GetElement(shapeId) as RebarShape)?.Name;
            }
            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar data: {ex.Message}");
        }
    }
}

/// <summary>Returns centerline curves (mm) for one bar position of a rebar. Opt-in detail.</summary>
public class GetRebarGeometryTool : ICortexTool
{
    public string Name => "get_rebar_geometry";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Return the centerline curves (mm) of a rebar at a given bar position index (default 0). Use suppressHooks/suppressBendRadius to simplify.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var barIndex = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var suppressHooks = input["suppressHooks"]?.Value<bool?>() ?? false;
        var suppressBend = input["suppressBendRadius"]?.Value<bool?>() ?? false;
        try
        {
            var curves = rebar!.GetCenterlineCurves(
                adjustForSelfIntersection: true,
                suppressHooks: suppressHooks,
                suppressBendRadius: suppressBend,
                multiplanarOption: MultiplanarOption.IncludeOnlyPlanarCurves,
                barPositionIndex: barIndex);
            var dtos = curves.Select(RebarToolHelpers.CurveToDtoMm).ToList();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                barPositionIndex = barIndex,
                numberOfBarPositions = rebar.NumberOfBarPositions,
                curveCount = dtos.Count,
                curves = dtos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar geometry: {ex.Message}");
        }
    }
}
```

- [ ] **Step 6: Build R25 + R24 (Tools)** — same commands. Expected: both succeed.

- [ ] **Step 7: Append `get_rebar_constraints`, `get_reinforcement_settings`, `list_rebar_splice_types`, `get_rebar_api_capabilities`**

`get_rebar_constraints` lists the constrained handles of a bar (read-only summary; full editing is in Module 5). `list_rebar_splice_types` is version-gated R2025+. `get_rebar_api_capabilities` reports which version-gated features the running Revit supports — this is the single place callers consult before invoking a gated write tool.

```csharp
/// <summary>Lists the constrained handles on a rebar (read-only summary).</summary>
public class GetRebarConstraintsTool : ICortexTool
{
    public string Name => "get_rebar_constraints";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List the constrained handles of a rebar and whether its constraints can be edited.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        try
        {
            var mgr = rebar!.GetRebarConstraintsManager();
            if (mgr == null)
                return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), constraintsAvailable = false });
            var handles = mgr.GetAllHandles();
            var handleDtos = handles.Select(h => new JObject
            {
                ["handleType"] = h.GetHandleType().ToString()
            }).ToList();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                constraintsAvailable = true,
                canBeEdited = rebar.ConstraintsCanBeEdited(),
                handleCount = handleDtos.Count,
                handles = handleDtos
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read rebar constraints: {ex.Message}");
        }
    }
}

/// <summary>Reads document-level reinforcement settings.</summary>
public class GetReinforcementSettingsTool : ICortexTool
{
    public string Name => "get_reinforcement_settings";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read document-level reinforcement settings (host structural rebar, presentation modes, shape-defines-hooks/terminations).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        try
        {
            var s = ReinforcementSettings.GetReinforcementSettings(doc!);
            var result = new JObject
            {
                ["hostStructuralRebar"] = s.HostStructuralRebar
            };
            // Property names differ across versions; read defensively.
            try { result["rebarShapeDefinesHooks"] = s.RebarShapeDefinesHooks; } catch { }
            try { result["rebarShapeDefinesEndTreatments"] = s.RebarShapeDefinesEndTreatments; } catch { }
            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read reinforcement settings: {ex.Message}");
        }
    }
}

/// <summary>Lists rebar splice types (Revit 2025+ only).</summary>
public class ListRebarSpliceTypesTool : ICortexTool
{
    public string Name => "list_rebar_splice_types";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List rebar splice types (Revit 2025+). Returns a version error on older targets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
#if REVIT2025_OR_GREATER
        try
        {
            // RebarSpliceType is the splice family type element.
            var items = new FilteredElementCollector(doc!).OfClass(typeof(RebarSpliceType)).Cast<ElementType>()
                .Select(t => new JObject { ["id"] = ToolHelpers.GetElementIdValue(t), ["name"] = t.Name }).ToList();
            return CortexResult<object>.Ok(new { count = items.Count, spliceTypes = items });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to list splice types: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar splices", 2025);
#endif
    }
}

/// <summary>Reports which version-gated reinforcement APIs the running Revit supports.</summary>
public class GetRebarApiCapabilitiesTool : ICortexTool
{
    public string Name => "get_rebar_api_capabilities";
    public string Category => "Rebar";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report which version-gated reinforcement features the running Revit supports (terminations/crank, splices, bending details), plus server-only APIs that are not runtime-scriptable.";

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
        return CortexResult<object>.Ok(new
        {
            revitYear = year,
            supportsBendingDetails = year >= 2024,
            supportsAlignedFreeForm = year >= 2024,
            supportsSplices = year >= 2025,
            supportsSurfaceConstraints = year >= 2025,
            supportsVaryingLengthBars = year >= 2025,
            supportsTerminationsApi = year >= 2026,   // BarTerminationsData / RebarTerminationOrientation
            supportsCrankApi = year >= 2026,
            supports3dPathDistribution = year >= 2027,
            // Server-extension APIs (IRebarUpdateServer / IRebarSpliceServer) are add-in lifecycle
            // infrastructure and are intentionally NOT scriptable from MCP input.
            serverExtensionApisScriptable = false
        });
    }
}
```

- [ ] **Step 8: Build R25 + R24 (Tools); also build R26 to exercise the termination/splice branches**

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: all three succeed. If `RebarSpliceType` is not the exact type name in the 2025 NuGet, the R26 build fails here — resolve by checking the actual splice type class in the `Autodesk.Revit.DB.Structure` 2025/2026 XML doc and adjusting only the `OfClass(typeof(...))` line. (Candidate alternative: there may be no public splice *type* element; if so, change this tool to return splice info from existing rebar via `RebarSpliceUtils` and document that in the tool description.)

- [ ] **Step 9: Run the full no-Revit test suite to confirm nothing regressed**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: all pass except the `[RequiresRevitApiFact]`-marked tests which **skip**. Baseline before this work: 221 passed / 1 skipped. The 4 new parser tests bring it to 225 passed / 1 skipped.

- [ ] **Step 10: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarDiscoveryTools.cs
git commit -m "feat(rebar): add Module 1 discovery and inventory tools"
```

---

## Task 3: Server wrappers for Module 1 + server contract test

**Files:**
- Create: `src/RevitCortex.Server/Tools/RebarTools.cs`
- Test: `src/RevitCortex.Tests/Rebar/RebarServerContractTests.cs`

The server wrapper is a `[McpServerToolType]` static class. Each method maps MCP args → `JObject` → `revit.ExecuteAsync(toolName, p, ct)`. Read tools that can be large accept `compact`/`summaryOnly` and pass through `ToolResponseShaper` only if we add a shaping rule — for Module 1 we keep payloads compact-by-construction, so no shaper rule is needed yet.

- [ ] **Step 1: Write the failing contract test**

```csharp
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Rebar;

public class RebarServerContractTests
{
    private static MethodInfo GetMethod(string name) =>
        Assert.Single(typeof(RebarTools).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == name);

    [Fact]
    public void ListRebarBarTypes_HasRevitAndCt()
    {
        var m = GetMethod(nameof(RebarTools.ListRebarBarTypes));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n),
            n => Assert.Equal("ct", n));
        Assert.Equal(typeof(RevitConnectionManager), m.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), m.GetParameters()[1].ParameterType);
        Assert.NotNull(m.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void GetRebarHostData_ExposesHostId()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarHostData));
        Assert.Contains("hostId", m.GetParameters().Select(p => p.Name));
        Assert.Equal(typeof(long), Assert.Single(m.GetParameters(), p => p.Name == "hostId").ParameterType);
    }

    [Fact]
    public void GetRebarGeometry_ExposesBarPositionIndex()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarGeometry));
        Assert.Contains("rebarId", m.GetParameters().Select(p => p.Name));
        Assert.Contains("barPositionIndex", m.GetParameters().Select(p => p.Name));
    }

    [Fact]
    public void GetRebarApiCapabilities_HasRevitAndCtOnly()
    {
        var m = GetMethod(nameof(RebarTools.GetRebarApiCapabilities));
        Assert.Collection(m.GetParameters().Select(p => p.Name),
            n => Assert.Equal("revit", n),
            n => Assert.Equal("ct", n));
    }
}
```

- [ ] **Step 2: Run, verify it fails** (RebarTools does not exist)

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~RebarServerContractTests"`
Expected: FAIL — compile error, `RebarTools` not found.

- [ ] **Step 3: Create `RebarTools.cs` with Module 1 wrappers**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class RebarTools
{
    // ── Module 1: discovery ──────────────────────────────────────────────────
    [McpServerTool(Name = "list_rebar_bar_types"), Description("List all rebar bar types (id, name, model and nominal diameter in mm).")]
    public static async Task<string> ListRebarBarTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_bar_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_hook_types"), Description("List all rebar hook types (id, name, hook angle in degrees).")]
    public static async Task<string> ListRebarHookTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_hook_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_shapes"), Description("List all rebar shapes (id, name).")]
    public static async Task<string> ListRebarShapes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_shapes", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_cover_types"), Description("List all rebar cover types (id, name, cover distance in mm).")]
    public static async Task<string> ListRebarCoverTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_cover_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_splice_types"), Description("List rebar splice types (Revit 2025+; returns a version error on older targets).")]
    public static async Task<string> ListRebarSpliceTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_splice_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_fabric_types"), Description("List fabric reinforcement types (fabric sheet types and fabric area types).")]
    public static async Task<string> ListRebarFabricTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_fabric_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_rebar_host_data"), Description("Report reinforcement hosted by an element: validity and the rebar/area/path/fabric it contains, plus common cover.")]
    public static async Task<string> GetRebarHostData(
        RevitConnectionManager revit,
        [Description("Host element id (beam/column/wall/floor/foundation)")] long hostId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_host_data", new JObject { ["hostId"] = hostId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_element_data"), Description("Read a single rebar's core data: bar type, host, shape, layout rule, bar count, total length (mm), volume.")]
    public static async Task<string> GetRebarElementData(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_element_data", new JObject { ["rebarId"] = rebarId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_geometry"), Description("Return the centerline curves (mm) of a rebar at a bar position index (default 0). Optionally suppress hooks/bend radius.")]
    public static async Task<string> GetRebarGeometry(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Bar position index. Default 0")] int? barPositionIndex = null,
        [Description("Suppress hook curves. Default false")] bool? suppressHooks = null,
        [Description("Suppress bend radius. Default false")] bool? suppressBendRadius = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (barPositionIndex != null) p["barPositionIndex"] = barPositionIndex;
        if (suppressHooks != null) p["suppressHooks"] = suppressHooks;
        if (suppressBendRadius != null) p["suppressBendRadius"] = suppressBendRadius;
        return (await revit.ExecuteAsync("get_rebar_geometry", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_constraints"), Description("List the constrained handles of a rebar and whether its constraints can be edited.")]
    public static async Task<string> GetRebarConstraints(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_constraints", new JObject { ["rebarId"] = rebarId }, ct)).ToString();

    [McpServerTool(Name = "get_reinforcement_settings"), Description("Read document-level reinforcement settings.")]
    public static async Task<string> GetReinforcementSettings(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_reinforcement_settings", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_rebar_api_capabilities"), Description("Report which version-gated reinforcement features the running Revit supports.")]
    public static async Task<string> GetRebarApiCapabilities(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_api_capabilities", new JObject(), ct)).ToString();
}
```

- [ ] **Step 4: Run the contract test, verify it passes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~RebarServerContractTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Build the server project**

Run: `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`
Expected: succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Rebar/RebarServerContractTests.cs
git commit -m "feat(rebar): add Module 1 server wrappers + contract tests"
```

---

## Task 4: Registration + read-only classification coverage

**Files:**
- Modify: `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs`
- Modify: `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs`

After Module 1, 12 new plugin tools exist. The registration count test must move, and we assert the read-only classification of the new prefixes.

- [ ] **Step 1: Update the tool-count threshold**

In `ToolRegistrationTests.cs`, the `ToolCount_MatchesExpected` test currently asserts `>= 133`. After Module 1 there are 12 more (≥145). Change both the number and the message:

```csharp
    [Fact]
    public void ToolCount_MatchesExpected()
    {
        // Update this number when adding new tools to catch accidental omissions
        Assert.True(AllToolTypes.Count >= 145,
            $"Expected at least 145 tools but found {AllToolTypes.Count}. " +
            $"If you removed tools intentionally, update this test.");
    }
```

(Each later module raises this again — Module 2: +12 ≥157, Module 3: +8 ≥165, Module 4: +8 ≥173, Module 5: +12 ≥185, Module 6: +10 ≥195. The final value is set in Task 12.)

- [ ] **Step 2: Add read-only classification rows**

In `ReadOnlyModeTests.cs`, add to the `IsReadOnlyTool_ClassifiesCorrectly` `[Theory]`:

```csharp
    [InlineData("list_rebar_bar_types", true)]
    [InlineData("get_rebar_host_data", true)]
    [InlineData("get_rebar_api_capabilities", true)]
    [InlineData("create_rebar_from_shape", false)]
    [InlineData("set_rebar_layout", false)]
    [InlineData("remove_rebar_system", false)]
```

- [ ] **Step 3: Run both test classes**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~ToolRegistrationTests|FullyQualifiedName~ReadOnlyModeTests"`
Expected: PASS. (Confirms 12 rebar tools registered and classified correctly.)

- [ ] **Step 4: Commit**

```powershell
git add src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs
git commit -m "test(rebar): cover Module 1 registration + read-only classification"
```

**End of Step 1.** At this point: 12 discovery tools build on R24/R25/R26, server wrappers build, all no-Revit tests pass.

---

## Step 2 — Shape-driven & free-form rebar (Module 2)

### Version fork (READ FIRST)

The 2026 SDK renamed the hook-orientation vocabulary to "terminations". Two tools embody this:
- `set_rebar_hooks` — works on ALL versions via `Rebar.SetHookTypeId(end, id)` (which stays valid 2023→2027) + the pre-2026 `SetHookOrientation`. On R2026+ orientation goes through `SetTerminationOrientation`.
- `set_rebar_terminations` — R2026+ only; full termination control (`BarTerminationsData`); returns `MinVersionError("Rebar terminations", 2026)` on older targets.

`create_rebar_from_shape` avoids the fork entirely (it uses `Rebar.CreateFromRebarShape`, stable across all years) — it is the recommended creation path and is implemented first.

## Task 5: Module 2 — rebar creation tools

**Files:**
- Modify: `src/RevitCortex.Tools/Rebar/` → Create `RebarCreationTools.cs`

- [ ] **Step 1: Create `RebarCreationTools.cs` with `create_rebar_from_shape`**

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

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates a shape-driven rebar in a host from a RebarShape (stable across all Revit versions).
/// Input (mm): hostId, shapeId|shapeName, barTypeId|barTypeName, origin{x,y,z}, xVec{x,y,z}, yVec{x,y,z},
/// optional layout{...}. Returns created rebar id + applied layout.
/// </summary>
public class CreateRebarFromShapeTool : ICortexTool
{
    public string Name => "create_rebar_from_shape";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a shape-driven rebar in a host from a rebar shape. Provide hostId, shapeId|shapeName, barTypeId|barTypeName, and origin/xVec/yVec (mm). Optional layout spec sets spacing/number.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        var shape = RebarToolHelpers.ResolveRebarShape(doc!, input["shapeId"]?.Value<long?>(), input["shapeName"]?.Value<string>());
        if (shape == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No rebar shape resolved", suggestion: "Use list_rebar_shapes to find a shapeId");

        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                "No rebar bar type resolved", suggestion: "Use list_rebar_bar_types to find a barTypeId");

        if (input["origin"] == null || input["xVec"] == null || input["yVec"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "origin, xVec and yVec are required (mm / direction vectors)");

        var origin = RebarToolHelpers.ParseXyzMm(input["origin"]!);
        var xVec = RebarToolHelpers.ParseXyzMm(input["xVec"]!).Normalize();
        var yVec = RebarToolHelpers.ParseXyzMm(input["yVec"]!).Normalize();

        RebarToolHelpers.LayoutSpec? layout = null;
        if (input["layout"] is JObject layoutObj)
        {
            layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
            if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);
        }

        if (!session.RequestConfirmation("create rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Rebar From Shape");
        tx.Start();
        try
        {
            var rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromRebarShape(
                doc!, shape, barType, host!, origin, xVec, yVec);
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit returned no rebar (shape/host/plane may be incompatible)");
            }

            string? appliedLayout = null;
            if (layout != null && rebar.IsRebarShapeDriven())
            {
                var acc = rebar.GetShapeDrivenAccessor();
                RebarToolHelpers.ApplyLayout(acc, layout);
                appliedLayout = layout.Rule.ToString();
            }

            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created rebar {id} in host {ToolHelpers.GetElementIdValue(host!)}",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                barTypeId = ToolHelpers.GetElementIdValue(barType),
                barTypeName = barType.Name,
                shapeId = ToolHelpers.GetElementIdValue(shape),
                appliedLayout
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to create rebar: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools)**

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: both succeed.

- [ ] **Step 3: Append `create_rebar_from_curves`**

This tool branches on the 2026 termination API. On R2023–R2025 it uses the legacy hook-orientation overload; on R2026+ it uses `BarTerminationsData`. Add to `RebarCreationTools.cs`:

```csharp
/// <summary>
/// Creates a rebar from explicit curves (mm) in a host. Curves must be coplanar; 'normal' is the
/// plane normal. Hooks optional via startHookId/endHookId. Version-aware: uses BarTerminationsData
/// on Revit 2026+, the legacy hook overload on 2023-2025.
/// </summary>
public class CreateRebarFromCurvesTool : ICortexTool
{
    public string Name => "create_rebar_from_curves";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a rebar from explicit coplanar curves (mm) in a host. Provide hostId, barTypeId|barTypeName, curves[], normal{x,y,z}, style (Standard|StirrupTie), optional startHookId/endHookId and layout.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved",
                suggestion: "Use list_rebar_bar_types to find a barTypeId");

        if (input["curves"] is not JArray curvesArr)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "curves array is required");
        var curves = RebarToolHelpers.ParseCurveSpecsMm(curvesArr, out var cerr);
        if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);

        if (input["normal"] == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "normal{x,y,z} is required");
        var normal = RebarToolHelpers.ParseXyzMm(input["normal"]!).Normalize();

        var style = RebarToolHelpers.ParseEnum<RebarStyle>(input["style"]?.Value<string>() ?? "Standard", "style", out var serr);
        if (serr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, serr);

        var startHook = RebarToolHelpers.ResolveRebarHookType(doc!, input["startHookId"]?.Value<long?>(), input["startHookName"]?.Value<string>());
        var endHook = RebarToolHelpers.ResolveRebarHookType(doc!, input["endHookId"]?.Value<long?>(), input["endHookName"]?.Value<string>());

        RebarToolHelpers.LayoutSpec? layout = null;
        if (input["layout"] is JObject layoutObj)
        {
            layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
            if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);
        }

        if (!session.RequestConfirmation("create rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Rebar From Curves");
        tx.Start();
        try
        {
            Autodesk.Revit.DB.Structure.Rebar? rebar;
#if REVIT2026_OR_GREATER
            var terminations = new BarTerminationsData(barType);
            if (startHook != null) terminations.SetHook(0, startHook.Id);
            if (endHook != null) terminations.SetHook(1, endHook.Id);
            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                doc!, style, barType, host!, normal, curves, terminations,
                useExistingShapeIfPossible: true, createNewShape: true);
#else
            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                doc!, style, barType, startHook, endHook, host!, normal, curves,
                RebarHookOrientation.Right, RebarHookOrientation.Right,
                useExistingShapeIfPossible: true, createNewShape: true);
#endif
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    "Revit returned no rebar (curves may be non-coplanar or invalid for the host)");
            }

            string? appliedLayout = null;
            if (layout != null && rebar.IsRebarShapeDriven())
            {
                RebarToolHelpers.ApplyLayout(rebar.GetShapeDrivenAccessor(), layout);
                appliedLayout = layout.Rule.ToString();
            }

            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created rebar {id} from {curves.Count} curve(s)",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                barTypeId = ToolHelpers.GetElementIdValue(barType),
                appliedLayout
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Failed to create rebar from curves: {ex.Message}");
        }
    }
}
```

> **Verification note for the implementer:** the exact member names on `BarTerminationsData` (constructor args and `SetHook`) must be confirmed against the 2026 `Autodesk.Revit.DB.Structure` XML doc before relying on this branch. If the 2026 API exposes a different shape (e.g. a struct with settable `StartHookType`/`EndHookType` properties), adjust ONLY the code inside the `#if REVIT2026_OR_GREATER` block — the rest of the tool and its contract are unaffected. The R26 build (Step 6) is the gate that catches a wrong guess here.

- [ ] **Step 4: Append `create_free_form_rebar`**

```csharp
/// <summary>
/// Creates an unconstrained free-form rebar from one or more curve loops (mm) in a host.
/// Does NOT accept arbitrary server code — only the unconstrained curve-loop path.
/// </summary>
public class CreateFreeFormRebarTool : ICortexTool
{
    public string Name => "create_free_form_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create an unconstrained free-form rebar from curve loops (mm) in a host. Provide hostId, barTypeId|barTypeName, style, and loops: an array of loops, each an array of curve specs.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved");
        if (input["loops"] is not JArray loopsArr || loopsArr.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "loops (array of curve-spec arrays) is required");

        var loops = new List<CurveLoop>();
        foreach (var loopTok in loopsArr.OfType<JArray>())
        {
            var curves = RebarToolHelpers.ParseCurveSpecsMm(loopTok, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
            var cl = new CurveLoop();
            foreach (var c in curves) cl.Append(c);
            loops.Add(cl);
        }

        var style = RebarToolHelpers.ParseEnum<RebarStyle>(input["style"]?.Value<string>() ?? "Standard", "style", out var serr);
        if (serr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, serr);

        if (!session.RequestConfirmation("create free-form rebar", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Create Free-Form Rebar");
        tx.Start();
        try
        {
            var rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFreeForm(doc!, barType, host!, loops, style);
            // 2026+ returns a result object; 2023-2025 may use an out-param overload. The
            // (Document, RebarBarType, Element, IList<CurveLoop>, RebarStyle) overload returning Rebar
            // is the common path 2024+. For R2023, see the version note below.
            if (rebar == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no free-form rebar");
            }
            var id = ToolHelpers.GetElementIdValue(rebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created free-form rebar {id} from {loops.Count} loop(s)",
                rebarId = id,
                hostId = ToolHelpers.GetElementIdValue(host!)
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create free-form rebar: {ex.Message}");
        }
    }
}
```

> **Version note:** the `CreateFreeForm(doc, barType, host, IList<CurveLoop>, RebarStyle)` overload returning `Rebar` is the documented 2024+ signature. On **R2023** the overload uses an `out RebarFreeFormValidationResult` parameter and returns the rebar differently; on **R2026+** it returns a `RebarFreeFormCreationResult`. Wrap the call site in `#if`:
> - `#if REVIT2026_OR_GREATER` → call the result-object overload, read `.GetRebar()` (confirm member name in the 2026 XML doc).
> - `#elif REVIT2024_OR_GREATER` → the `IList<CurveLoop>,RebarStyle` overload shown above.
> - `#else` (R2023) → the `out RebarFreeFormValidationResult` overload.
> The R23, R24, R26 builds (Step 6 + Task 17) catch wrong guesses. Keep the public contract identical across branches.

- [ ] **Step 5: Build R25 + R24 (Tools)** — same commands as Step 2. Expected: both succeed.

- [ ] **Step 6: Build R23 + R26 + R27 (Tools) to validate every `#if` branch**

```powershell
dotnet build -c "Debug R23" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R27" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: all succeed. (R23 needs net48 SDK; R27 needs .NET 10 SDK — if the machine lacks SDK 10 this fails with `NETSDK1045`, which is an environment gap, not a code error. Note it and proceed; the release machine builds R27.)

- [ ] **Step 7: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarCreationTools.cs
git commit -m "feat(rebar): add Module 2 creation tools (from shape, from curves, free-form)"
```

---

## Task 6: Module 2 — rebar mutator tools (layout, shape, hooks, terminations, host, visibility, move, include/exclude, split)

**Files:**
- Modify: `src/RevitCortex.Tools/Rebar/RebarCreationTools.cs` (append the mutators)

All are write tools (no read prefix). All call `RequireRebar` then `RequestConfirmation` then transaction. Each is small; implement them in two increments, building R24/R25 after each.

- [ ] **Step 1: Append `set_rebar_layout`, `set_rebar_shape`, `set_rebar_host`, `set_rebar_visibility`**

```csharp
/// <summary>Re-applies a layout rule to an existing shape-driven rebar.</summary>
public class SetRebarLayoutTool : ICortexTool
{
    public string Name => "set_rebar_layout";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the distribution layout of a shape-driven rebar. Provide rebarId and a layout spec (rule: single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Layout can only be set on shape-driven rebar");
        if (input["layout"] is not JObject layoutObj)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "layout object is required");
        var layout = RebarToolHelpers.ParseLayoutSpec(layoutObj, out var lerr);
        if (lerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, lerr);

        if (!session.RequestConfirmation("set rebar layout", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Layout");
        tx.Start();
        try
        {
            RebarToolHelpers.ApplyLayout(rebar.GetShapeDrivenAccessor(), layout);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                rebarId = ToolHelpers.GetElementIdValue(rebar),
                appliedLayout = layout.Rule.ToString(),
                numberOfBarPositions = rebar.NumberOfBarPositions
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set layout: {ex.Message}");
        }
    }
}

/// <summary>Changes the RebarShape of a shape-driven rebar.</summary>
public class SetRebarShapeTool : ICortexTool
{
    public string Name => "set_rebar_shape";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Change the shape of a shape-driven rebar. Provide rebarId and shapeId|shapeName.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Shape can only be set on shape-driven rebar");
        var shape = RebarToolHelpers.ResolveRebarShape(doc!, input["shapeId"]?.Value<long?>(), input["shapeName"]?.Value<string>());
        if (shape == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar shape resolved");

        if (!session.RequestConfirmation("set rebar shape", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Shape");
        tx.Start();
        try
        {
            rebar.GetShapeDrivenAccessor().SetRebarShapeId(shape.Id);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), shapeId = ToolHelpers.GetElementIdValue(shape) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set shape: {ex.Message}");
        }
    }
}

/// <summary>Reassigns a rebar to a new valid host.</summary>
public class SetRebarHostTool : ICortexTool
{
    public string Name => "set_rebar_host";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Reassign a rebar to a new host. Provide rebarId and newHostId (must be a valid rebar host).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["newHostId"]?.Value<long?>());
        if (herr != null) return herr;

        if (!session.RequestConfirmation("reassign rebar host", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Host");
        tx.Start();
        try
        {
            rebar!.SetHostId(host!.Id);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), hostId = ToolHelpers.GetElementIdValue(host) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set host: {ex.Message}");
        }
    }
}

/// <summary>Sets unobscured/solid presentation of a rebar in a view (post-2024 API).</summary>
public class SetRebarVisibilityTool : ICortexTool
{
    public string Name => "set_rebar_visibility";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set rebar view presentation. Provide rebarId, viewId, and unobscured (show in front of host). Uses SetUnobscuredInView (SetSolidInView was removed in Revit 2024).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required");
        var view = doc!.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No view with id {viewId}");
        var unobscured = input["unobscured"]?.Value<bool?>() ?? true;

        if (!session.RequestConfirmation("set rebar visibility", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Rebar Visibility");
        tx.Start();
        try
        {
            rebar!.SetUnobscuredInView(view, unobscured);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), viewId, unobscured });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set visibility: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools)** — same commands. Expected: both succeed.

- [ ] **Step 3: Append `set_rebar_hooks`, `set_rebar_terminations`, `move_rebar_in_set`, `include_exclude_rebar_bars`, `split_rebar`**

```csharp
/// <summary>Sets the hook type at one or both ends of a rebar (works on all Revit versions).</summary>
public class SetRebarHooksTool : ICortexTool
{
    public string Name => "set_rebar_hooks";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the hook type at rebar ends. Provide rebarId, optional startHookId/endHookId (omit or pass 0 to clear an end's hook). Works on all Revit versions.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;

        bool hasStart = input["startHookId"] != null;
        bool hasEnd = input["endHookId"] != null;
        if (!hasStart && !hasEnd)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Provide startHookId and/or endHookId (0 to clear)");

        if (!session.RequestConfirmation("set rebar hooks", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Hooks");
        tx.Start();
        try
        {
            if (hasStart)
            {
                var sid = input["startHookId"]!.Value<long>();
                rebar!.SetHookTypeId(0, sid > 0 ? ToolHelpers.ToElementId(sid) : ElementId.InvalidElementId);
            }
            if (hasEnd)
            {
                var eid = input["endHookId"]!.Value<long>();
                rebar!.SetHookTypeId(1, eid > 0 ? ToolHelpers.ToElementId(eid) : ElementId.InvalidElementId);
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar) });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set hooks: {ex.Message}");
        }
    }
}

/// <summary>Sets full termination data on a rebar end (Revit 2026+ only).</summary>
public class SetRebarTerminationsTool : ICortexTool
{
    public string Name => "set_rebar_terminations";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set rebar end terminations (hook + orientation/rotation). Revit 2026+ only; returns a version error on older targets. Provide rebarId, end (0|1), orientation, rotationDegrees.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
#if REVIT2026_OR_GREATER
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var end = input["end"]?.Value<int?>() ?? 0;
        var orientation = RebarToolHelpers.ParseEnum<RebarTerminationOrientation>(
            input["orientation"]?.Value<string>() ?? "Right", "orientation", out var oerr);
        if (oerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, oerr);
        var rotDeg = input["rotationDegrees"]?.Value<double?>() ?? 0.0;

        if (!session.RequestConfirmation("set rebar terminations", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Rebar Terminations");
        tx.Start();
        try
        {
            rebar!.SetTerminationOrientation(end, orientation);
            rebar.SetTerminationRotationAngle(end, rotDeg * Math.PI / 180.0);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), end, orientation = orientation.ToString() });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set terminations: {ex.Message}");
        }
#else
        return RebarToolHelpers.MinVersionError("Rebar terminations", 2026);
#endif
    }
}

/// <summary>Moves a single bar within a shape-driven set.</summary>
public class MoveRebarInSetTool : ICortexTool
{
    public string Name => "move_rebar_in_set";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Move a single bar within a rebar set by a translation vector (mm). Provide rebarId, barPositionIndex, translation{x,y,z}. Pass reset:true to clear a prior move.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var idx = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var reset = input["reset"]?.Value<bool?>() ?? false;

        if (!session.RequestConfirmation("move bar in set", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Move Bar In Set");
        tx.Start();
        try
        {
            if (reset)
            {
                rebar!.ResetMovedBarTransform(idx);
            }
            else
            {
                if (input["translation"] == null)
                    { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "translation{x,y,z} required unless reset:true"); }
                var v = RebarToolHelpers.ParseXyzMm(input["translation"]!);
                rebar!.MoveBarInSet(idx, Transform.CreateTranslation(v));
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), barPositionIndex = idx, reset });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to move bar: {ex.Message}");
        }
    }
}

/// <summary>Shows/hides a single bar of a set in a view.</summary>
public class IncludeExcludeRebarBarsTool : ICortexTool
{
    public string Name => "include_exclude_rebar_bars";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Show or hide a single bar of a rebar set in a view. Provide rebarId, viewId, barPositionIndex, hidden (true=hide).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        var viewId = input["viewId"]?.Value<long?>();
        if (viewId == null || viewId <= 0) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewId is required");
        var view = doc!.GetElement(ToolHelpers.ToElementId(viewId.Value)) as View;
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No view with id {viewId}");
        var idx = input["barPositionIndex"]?.Value<int?>() ?? 0;
        var hidden = input["hidden"]?.Value<bool?>() ?? true;

        if (!session.RequestConfirmation("change bar visibility", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Include/Exclude Bar");
        tx.Start();
        try
        {
            rebar!.SetBarHiddenStatus(view, idx, hidden);
            tx.Commit();
            return CortexResult<object>.Ok(new { rebarId = ToolHelpers.GetElementIdValue(rebar), viewId, barPositionIndex = idx, hidden });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to change bar visibility: {ex.Message}");
        }
    }
}

/// <summary>
/// "Splits" a rebar set by reducing the original to the first N positions and creating a
/// duplicate set for the remaining positions, so each piece can be edited independently.
/// Implemented via ElementTransformUtils copy + layout adjustment (the API has no single Split call).
/// </summary>
public class SplitRebarTool : ICortexTool
{
    public string Name => "split_rebar";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Split a shape-driven rebar set into two sets at a given bar position. Provide rebarId and splitAtPosition (1..count-1). Returns the original and new rebar ids.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (rebar, rerr) = RebarToolHelpers.RequireRebar(doc!, input["rebarId"]?.Value<long?>());
        if (rerr != null) return rerr;
        if (!rebar!.IsRebarShapeDriven())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "split_rebar requires a shape-driven set");
        var total = rebar.NumberOfBarPositions;
        var splitAt = input["splitAtPosition"]?.Value<int?>() ?? 0;
        if (splitAt < 1 || splitAt >= total)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"splitAtPosition must be between 1 and {total - 1} (set has {total} positions)");

        if (!session.RequestConfirmation("split rebar set", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Split Rebar");
        tx.Start();
        try
        {
            var acc = rebar.GetShapeDrivenAccessor();
            var arrayLen = acc.ArrayLength;
            var spacing = total > 1 ? arrayLen / (total - 1) : arrayLen;

            // Duplicate the set.
            var copied = ElementTransformUtils.CopyElement(doc!, rebar.Id, XYZ.Zero);
            var newRebar = doc!.GetElement(copied.First()) as Autodesk.Revit.DB.Structure.Rebar;
            if (newRebar == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Copy did not yield a rebar"); }

            // Original keeps first `splitAt` positions; new keeps the rest, shifted along the normal.
            acc.SetLayoutAsFixedNumber(splitAt, spacing * (splitAt - 1 <= 0 ? 1 : splitAt - 1), acc.BarsOnNormalSide, true, true);

            var newAcc = newRebar.GetShapeDrivenAccessor();
            var remaining = total - splitAt;
            // Shift the new set so it begins where the original ends.
            var shift = newRebar.Normal.Normalize().Multiply(spacing * splitAt);
            ElementTransformUtils.MoveElement(doc, newRebar.Id, shift);
            newAcc.SetLayoutAsFixedNumber(remaining, spacing * (remaining - 1 <= 0 ? 1 : remaining - 1), newAcc.BarsOnNormalSide, true, true);

            var origId = ToolHelpers.GetElementIdValue(rebar);
            var newId = ToolHelpers.GetElementIdValue(newRebar);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Split rebar {origId}: original keeps {splitAt} positions, new {newId} keeps {remaining}",
                originalRebarId = origId,
                newRebarId = newId,
                splitAtPosition = splitAt
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to split rebar: {ex.Message}");
        }
    }
}
```

> **Implementer note on `split_rebar`:** the Revit API has no atomic "split set" call, so this composes copy + layout. The spacing math assumes a uniform fixed-number layout; if the source uses spacing-based layout, `ArrayLength` still gives total length so the derived `spacing` is valid. Confirm `MoveBarInSet`, `ResetMovedBarTransform`, `SetBarHiddenStatus`, and `RebarStyle`/`RebarTerminationOrientation` enum member names against the relevant XML docs during the R24/R26 builds.

- [ ] **Step 4: Build R25 + R24 + R26 (Tools)**

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```
Expected: all succeed.

- [ ] **Step 5: Add server wrappers for all 12 Module 2 tools to `RebarTools.cs`**

Append to `RebarTools.cs` (inside the class). Pattern: write tools forward each scalar; complex inputs (`origin`, `xVec`, `yVec`, `normal`, `translation`, `curves`, `loops`, `layout`) arrive as JSON strings and are parsed with `JObject.Parse`/`JArray.Parse`. Example for the two representative tools — replicate the pattern for the rest:

```csharp
    // ── Module 2: creation & mutation ────────────────────────────────────────
    [McpServerTool(Name = "create_rebar_from_shape"), Description("Create a shape-driven rebar in a host from a rebar shape. origin/xVec/yVec are JSON {x,y,z} in mm. Optional layout JSON.")]
    public static async Task<string> CreateRebarFromShape(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Origin point JSON {x,y,z} in mm")] string origin,
        [Description("Local X direction JSON {x,y,z}")] string xVec,
        [Description("Local Y direction JSON {x,y,z}")] string yVec,
        [Description("Rebar shape id")] long? shapeId = null,
        [Description("Rebar shape name (used if shapeId omitted)")] string? shapeName = null,
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        [Description("Layout JSON {rule, number?, arrayLengthMm?, spacingMm?, barsOnNormalSide?, includeFirstBar?, includeLastBar?}")] string? layout = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["hostId"] = hostId,
            ["origin"] = JObject.Parse(origin),
            ["xVec"] = JObject.Parse(xVec),
            ["yVec"] = JObject.Parse(yVec)
        };
        if (shapeId != null) p["shapeId"] = shapeId;
        if (shapeName != null) p["shapeName"] = shapeName;
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        if (layout != null) p["layout"] = JObject.Parse(layout);
        return (await revit.ExecuteAsync("create_rebar_from_shape", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_layout"), Description("Set the distribution layout of a shape-driven rebar. layout is JSON {rule, number?, arrayLengthMm?, spacingMm?, ...}.")]
    public static async Task<string> SetRebarLayout(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Layout JSON {rule: single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing, ...}")] string layout,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["layout"] = JObject.Parse(layout) };
        return (await revit.ExecuteAsync("set_rebar_layout", p, ct)).ToString();
    }
```

Remaining Module 2 wrappers to add (one method each, following the same forwarding rules — exact param lists mirror each plugin tool's `input[...]` reads):
`create_rebar_from_curves` (hostId, curves[json], normal[json], style, startHookId?, endHookId?, barTypeId?/Name?, layout?[json]),
`create_free_form_rebar` (hostId, loops[json], style, barTypeId?/Name?),
`set_rebar_shape` (rebarId, shapeId?/shapeName?),
`set_rebar_hooks` (rebarId, startHookId?, endHookId?),
`set_rebar_terminations` (rebarId, end?, orientation?, rotationDegrees?),
`set_rebar_host` (rebarId, newHostId),
`set_rebar_visibility` (rebarId, viewId, unobscured?),
`move_rebar_in_set` (rebarId, barPositionIndex?, translation?[json], reset?),
`include_exclude_rebar_bars` (rebarId, viewId, barPositionIndex?, hidden?),
`split_rebar` (rebarId, splitAtPosition).

- [ ] **Step 6: Build server project**

Run: `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`
Expected: succeeds.

- [ ] **Step 7: Bump tool-count test + add read-only rows**

In `ToolRegistrationTests.cs` raise the threshold from `145` to `157`. In `ReadOnlyModeTests.cs` the rows added in Task 4 already cover Module 2 write tools. Run:
`dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~ToolRegistrationTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarCreationTools.cs src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(rebar): add Module 2 mutators + server wrappers"
```

**End of Step 2.**

---

## Step 3 — Area & path reinforcement (Module 3)

All tools live in a new file `src/RevitCortex.Tools/Rebar/RebarSystemTools.cs`. By now the implementer has 5 fully-worked write tools; Module 3+ tools reuse the identical skeleton (`RequireDocument` → `RequireHost`/`RequireRebar` → resolve types → `RequestConfirmation` → `Transaction` → `Ok`/`Fail`). Each task below gives the FULL code for the one non-obvious tool and exact API signatures + contracts for the rest.

### Reference API signatures (from the verified catalog)

```text
AreaReinforcement.Create(Document, Element host, XYZ majorDirection,
    ElementId areaReinforcementTypeId, ElementId rebarBarTypeId, ElementId rebarHookTypeId)
AreaReinforcement.Create(Document, Element host, IList<Curve> curves, XYZ majorDirection,
    ElementId areaReinforcementTypeId, ElementId rebarBarTypeId, ElementId rebarHookTypeId)
  // pass ElementId.InvalidElementId for "no hook". Direction is read-only after creation.
AreaReinforcement.GetRebarInSystemIds() -> IList<ElementId>
AreaReinforcement.GetBoundaryCurveIds() -> IList<ElementId>
AreaReinforcement.RemoveAreaReinforcementSystem(Document, AreaReinforcement)  // static
AreaReinforcement.ConvertRebarInSystemToRebars() // members become standalone Rebar

PathReinforcement.Create(Document, Element host, IList<Curve> curves, bool flip,
    ElementId pathReinforcementTypeId, ElementId rebarBarTypeId,
    ElementId startHookTypeId, ElementId endHookTypeId)
PathReinforcement.GetRebarInSystemIds() / GetCurveIds() / RemovePathReinforcementSystem(...) / ConvertRebarInSystemToRebars()

// Type defaults: first AreaReinforcementType / PathReinforcementType in the doc, resolved like RebarBarType.
```

## Task 7: Module 3 — area/path creation + read tools

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/RebarSystemTools.cs`

- [ ] **Step 1: Create the file with `create_area_reinforcement` (FULL code)**

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

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates an area reinforcement system on a host. Either covers the host boundary (no curves) or
/// uses an explicit boundary (curves[] in mm). Major direction is mandatory and fixed at creation.
/// </summary>
public class CreateAreaReinforcementTool : ICortexTool
{
    public string Name => "create_area_reinforcement";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create an area reinforcement system on a host (wall/floor/foundation). Provide hostId, majorDirection{x,y,z}, barTypeId|barTypeName; optional curves[] (mm) for an explicit boundary, areaTypeId, hookTypeId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        var barType = RebarToolHelpers.ResolveRebarBarType(doc!, input["barTypeId"]?.Value<long?>(), input["barTypeName"]?.Value<string>());
        if (barType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No rebar bar type resolved");
        if (input["majorDirection"] == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "majorDirection{x,y,z} is required");
        var major = RebarToolHelpers.ParseXyzMm(input["majorDirection"]!).Normalize();

        var areaType = doc!.GetElement(ToolHelpers.ToElementId(input["areaTypeId"]?.Value<long?>() ?? -1)) as AreaReinforcementType
            ?? new FilteredElementCollector(doc).OfClass(typeof(AreaReinforcementType)).Cast<AreaReinforcementType>().FirstOrDefault();
        if (areaType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No AreaReinforcementType in document");

        var hookId = ElementId.InvalidElementId;
        var hook = RebarToolHelpers.ResolveRebarHookType(doc, input["hookTypeId"]?.Value<long?>(), input["hookTypeName"]?.Value<string>());
        if (hook != null) hookId = hook.Id;

        IList<Curve>? curves = null;
        if (input["curves"] is JArray ca)
        {
            curves = RebarToolHelpers.ParseCurveSpecsMm(ca, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
        }

        if (!session.RequestConfirmation("create area reinforcement", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Area Reinforcement");
        tx.Start();
        try
        {
            AreaReinforcement area = curves != null
                ? AreaReinforcement.Create(doc, host!, curves, major, areaType.Id, barType.Id, hookId)
                : AreaReinforcement.Create(doc, host!, major, areaType.Id, barType.Id, hookId);
            if (area == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no area reinforcement"); }
            var memberIds = area.GetRebarInSystemIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var id = ToolHelpers.GetElementIdValue(area);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created area reinforcement {id} with {memberIds.Count} bar system member(s)",
                areaReinforcementId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                memberCount = memberIds.Count,
                memberIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create area reinforcement: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools).** Expected: both succeed.

- [ ] **Step 3: Append the remaining Module 3 tools to the same file**

Implement each with the standard skeleton. Contracts:

- **`create_path_reinforcement`** (write): inputs `hostId`, `curves[]` (mm, required), `flip` (bool, default false), `barTypeId|barTypeName`, optional `pathTypeId`, `startHookId`, `endHookId`. Resolve `PathReinforcementType` default like the area type. Call `PathReinforcement.Create(doc, host, curves, flip, pathType.Id, barType.Id, startHookId, endHookId)`. Return `pathReinforcementId`, `memberIds` from `GetRebarInSystemIds()`.
- **`set_area_reinforcement_layers`** (write): inputs `areaReinforcementId`, `layer` (`top_major|top_minor|bottom_major|bottom_minor`), `active` (bool). Resolve the `AreaReinforcement`, map the layer string to `AreaReinforcementLayerType` (verify member names in XML doc), call `SetLayerActive(layerType, active)` inside a transaction.
- **`set_path_reinforcement_options`** (write): inputs `pathReinforcementId`, optional `additionalTopCoverOffsetMm`, `additionalBottomCoverOffsetMm`, `primaryBarLengthMm` — set the corresponding writable parameters/properties. (Use only properties confirmed present; skip any that are read-only and note in the response `warnings`.)
- **`convert_rebar_system_to_rebars`** (write, DESTRUCTIVE): inputs `systemId` (an area OR path reinforcement id). Resolve to `AreaReinforcement` or `PathReinforcement`, `RequestConfirmation("convert reinforcement to rebars", memberCount)`, call `ConvertRebarInSystemToRebars()`, return the resulting standalone rebar ids.
- **`remove_rebar_system`** (write, DESTRUCTIVE): inputs `systemId`. Resolve to area/path, `RequestConfirmation("remove reinforcement system", 1)`, call the matching `RemoveAreaReinforcementSystem`/`RemovePathReinforcementSystem` static, return `{removed:true}`.
- **`get_area_reinforcement_data`** (read): inputs `areaReinforcementId`. Return direction (mm vector), type id/name, `memberIds`, `boundaryCurveIds`, member count.
- **`get_path_reinforcement_data`** (read): inputs `pathReinforcementId`. Return type id/name, `memberIds`, `curveIds`, flip state if readable.

Build R25 + R24 after writing these.

- [ ] **Step 4: Add the 8 Module 3 server wrappers to `RebarTools.cs`** (same forwarding pattern; `curves`/`majorDirection` arrive as JSON strings, parsed with `JArray.Parse`/`JObject.Parse`). Build the server project.

- [ ] **Step 5: Bump tool-count test threshold from `157` to `165`; run it.** Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarSystemTools.cs src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(rebar): add Module 3 area/path reinforcement tools"
```

---

## Step 4 — Fabric reinforcement (Module 4)

File: `src/RevitCortex.Tools/Rebar/FabricReinforcementTools.cs`.

### Reference API signatures

```text
FabricArea.Create(Document, Element host, XYZ majorDirection, ElementId fabricAreaTypeId, ElementId fabricSheetTypeId)
FabricArea.Create(Document, Element host, IList<Curve> curves, XYZ majorDirection, ElementId fabricAreaTypeId, ElementId fabricSheetTypeId)
FabricArea.GetFabricSheetIds() -> IList<ElementId>;  FabricArea.RemoveFabricAreaSystem(...) (static)
FabricSheet.Create(Document, Element host, ElementId fabricSheetTypeId)               // flat
FabricSheet.Create(Document, ElementId hostId, ElementId fabricSheetTypeId, CurveLoop bendingProfile) // bent
FabricSheet.IsValidHost(Element) (static);  FabricSheet.PlaceInHost(Element, Transform)
FabricSheet.GetBendProfile()/SetBendProfile(CurveLoop);  props IsBent, FabricNumber, CutOverallLength, CutOverallWidth
FabricSheetType.CreateDefaultFabricSheetType(Document) -> ElementId
FabricSheet.GetWireItem(int idx, WireDistributionDirection) -> FabricWireItem
```

## Task 8: Module 4 — fabric creation + read tools

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/FabricReinforcementTools.cs`

- [ ] **Step 1: Create the file with `create_fabric_area` (FULL code)**

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

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates a fabric area system on a host (auto-distributes fabric sheets). Boundary mode (no curves)
/// or explicit boundary (curves[] mm). Major direction fixed at creation.
/// </summary>
public class CreateFabricAreaTool : ICortexTool
{
    public string Name => "create_fabric_area";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a fabric area system on a host (wall/floor/foundation). Provide hostId, majorDirection{x,y,z}, fabricSheetTypeId|fabricSheetTypeName; optional curves[] (mm) and fabricAreaTypeId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        if (input["majorDirection"] == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "majorDirection{x,y,z} is required");
        var major = RebarToolHelpers.ParseXyzMm(input["majorDirection"]!).Normalize();

        var sheetType = doc!.GetElement(ToolHelpers.ToElementId(input["fabricSheetTypeId"]?.Value<long?>() ?? -1)) as FabricSheetType;
        if (sheetType == null)
        {
            var name = input["fabricSheetTypeName"]?.Value<string>();
            sheetType = new FilteredElementCollector(doc).OfClass(typeof(FabricSheetType)).Cast<FabricSheetType>()
                .FirstOrDefault(t => name == null || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (sheetType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No FabricSheetType in document",
            suggestion: "Use list_rebar_fabric_types to find a fabricSheetTypeId");

        var areaType = doc.GetElement(ToolHelpers.ToElementId(input["fabricAreaTypeId"]?.Value<long?>() ?? -1)) as FabricAreaType
            ?? new FilteredElementCollector(doc).OfClass(typeof(FabricAreaType)).Cast<FabricAreaType>().FirstOrDefault();
        if (areaType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No FabricAreaType in document");

        IList<Curve>? curves = null;
        if (input["curves"] is JArray ca)
        {
            curves = RebarToolHelpers.ParseCurveSpecsMm(ca, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
        }

        if (!session.RequestConfirmation("create fabric area", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Fabric Area");
        tx.Start();
        try
        {
            FabricArea area = curves != null
                ? FabricArea.Create(doc, host!, curves, major, areaType.Id, sheetType.Id)
                : FabricArea.Create(doc, host!, major, areaType.Id, sheetType.Id);
            if (area == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no fabric area"); }
            doc.Regenerate();
            var sheetIds = area.GetFabricSheetIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var id = ToolHelpers.GetElementIdValue(area);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created fabric area {id} with {sheetIds.Count} sheet(s)",
                fabricAreaId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                sheetCount = sheetIds.Count,
                sheetIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create fabric area: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools).** Expected: both succeed.

- [ ] **Step 3: Append the remaining Module 4 tools** (standard skeleton). Contracts:

- **`create_fabric_sheet`** (write): inputs `hostId`, `fabricSheetTypeId|Name`. Flat sheet: `FabricSheet.Create(doc, host, type.Id)`. If `bendProfile[]` (mm curves) provided, build a `CurveLoop` and use the bent overload `FabricSheet.Create(doc, host.Id, type.Id, loop)`. Return `fabricSheetId`, `isBent`.
- **`place_fabric_sheet`** (write): inputs `fabricSheetId`, `hostId`, `transform` (optional `{translation:{x,y,z}}` in mm; default identity). Resolve sheet, call `sheet.PlaceInHost(host, Transform.CreateTranslation(v))`. Return `{placed:true}`.
- **`set_fabric_sheet_bend_profile`** (write): inputs `fabricSheetId`, `bendProfile[]` (mm curves). Build `CurveLoop`, call `SetBendProfile(loop)` (only valid if `IsBent`; else return `InvalidInput`). 
- **`remove_fabric_reinforcement_system`** (write, DESTRUCTIVE): inputs `fabricAreaId`. `RequestConfirmation("remove fabric area system", 1)`, call `FabricArea.RemoveFabricAreaSystem(doc, area)`.
- **`get_fabric_area_data`** (read): inputs `fabricAreaId`. Return type id/name, `sheetIds`, sheet count, major direction if readable.
- **`get_fabric_sheet_data`** (read): inputs `fabricSheetId`. Return type, `isBent`, `fabricNumber`, `cutOverallLengthMm`, `cutOverallWidthMm`.
- **`get_fabric_wire_data`** (read): inputs `fabricSheetId`, `direction` (`major|minor`). Iterate wire items via `GetWireItem(i, WireDistributionDirection.X/Y)` until it throws/null; return wire diameters (mm) and distances. Cap at a sane `maxWires` (default 200) and report truncation.

Build R25 + R24 after writing these.

- [ ] **Step 4: Add the 8 Module 4 server wrappers to `RebarTools.cs`.** Build server.

- [ ] **Step 5: Bump tool-count threshold `165` → `173`; run the test.** Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/FabricReinforcementTools.cs src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(rebar): add Module 4 fabric reinforcement tools"
```

**End of Step 4.**

---

## Step 5 — Constraints, propagation, couplers, splices (Module 5)

File: `src/RevitCortex.Tools/Rebar/RebarAdvancedTools.cs`. Splice tools are `#if REVIT2025_OR_GREATER`-gated; everything else works on all versions. Constraints expose **target descriptors**, never raw serialized `Reference` objects (per spec Open Risks).

### Reference API signatures

```text
RebarCoupler.Create(Document, ElementId couplerTypeId, RebarReinforcementData data1,
    RebarReinforcementData data2, out RebarCouplerError error)   // data2 may be null = cap one bar
RebarReinforcementData.Create(ElementId rebarId, int barPositionIndex, int end)  // 'end' 0 or 1
RebarCoupler props: CouplerMark; methods GetCoupledReinforcementData(), CouplerLinkTwoBars()
RebarCouplerType : the coupler family type (collect via OfClass(typeof(RebarCouplerType)))

Rebar.GetRebarConstraintsManager() -> RebarConstraintsManager
RebarConstraintsManager.GetAllHandles() -> IList<RebarConstrainedHandle>
  (2025+) GetAutomaticConstraintCandidatesForHandle(handle), SetPreferredConstraint(RebarConstraint)
  (<=2024) GetConstraintCandidatesForHandle(handle), SetPreferredConstraintForHandle(handle, candidate)

// Splices (2025+): RebarSpliceUtils / Rebar.GetRebarSplice() / RemoveSplice() / GetLapLength()
// Propagation: ReinforcementUtils (2024+) — propagate rebar to similar hosts.
// Annotations: MultiReferenceAnnotation.Create(Document, View, MultiReferenceAnnotationOptions)
```

## Task 9: Module 5 — couplers + constraints + propagation + annotations (all-version) and read tools

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/RebarAdvancedTools.cs`

- [ ] **Step 1: Create the file with `create_rebar_coupler` (FULL code)**

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

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates a rebar coupler connecting two bar ends, or caps a single bar end. Provide a coupler type
/// and one or two reinforcement-data descriptors {rebarId, barPositionIndex, end}.
/// </summary>
public class CreateRebarCouplerTool : ICortexTool
{
    public string Name => "create_rebar_coupler";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a rebar coupler connecting two bar ends (or cap one). Provide couplerTypeId|couplerTypeName, end1{rebarId,barPositionIndex,end}, optional end2{...}. 'end' is 0 or 1.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var couplerType = doc!.GetElement(ToolHelpers.ToElementId(input["couplerTypeId"]?.Value<long?>() ?? -1)) as RebarCouplerType;
        if (couplerType == null)
        {
            var name = input["couplerTypeName"]?.Value<string>();
            couplerType = new FilteredElementCollector(doc).OfClass(typeof(RebarCouplerType)).Cast<RebarCouplerType>()
                .FirstOrDefault(t => name == null || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (couplerType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
            "No RebarCouplerType resolved", suggestion: "Load a coupler family/type first");

        RebarReinforcementData? Parse(JToken? t)
        {
            if (t is not JObject o) return null;
            var rid = o["rebarId"]?.Value<long?>();
            if (rid == null || rid <= 0) return null;
            var bpi = o["barPositionIndex"]?.Value<int?>() ?? 0;
            var end = o["end"]?.Value<int?>() ?? 0;
            return RebarReinforcementData.Create(ToolHelpers.ToElementId(rid.Value), bpi, end);
        }

        var d1 = Parse(input["end1"]);
        if (d1 == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "end1{rebarId,barPositionIndex,end} is required");
        var d2 = Parse(input["end2"]); // may be null = cap

        if (!session.RequestConfirmation("create rebar coupler", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Rebar Coupler");
        tx.Start();
        try
        {
            var coupler = RebarCoupler.Create(doc, couplerType.Id, d1, d2, out RebarCouplerError err);
            if (coupler == null)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Coupler not created: {err}");
            }
            var id = ToolHelpers.GetElementIdValue(coupler);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = d2 != null ? $"Created coupler {id} linking two bars" : $"Created coupler {id} capping one bar",
                couplerId = id,
                linkedTwoBars = d2 != null
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create coupler: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools).** Expected: both succeed.

- [ ] **Step 3: Append remaining all-version Module 5 tools** (standard skeleton). Contracts:

- **`set_rebar_coupler_visibility`** (write): inputs `couplerId`, `viewId`, `unobscured`. Resolve `RebarCoupler`, call `SetUnobscuredInView(view, unobscured)`.
- **`manage_rebar_constraints`** (write/read hybrid — keep `manage_` as a WRITE prefix; it is not read-only): inputs `rebarId`, `action` (`list_handles|list_candidates|set_preferred|remove_preferred|recompute`). For `list_*` actions, no transaction, return handle/candidate summaries (use the 2025+ vs ≤2024 method names behind `#if REVIT2025_OR_GREATER`). For mutating actions (`set_preferred`/`remove_preferred`/`recompute`), `RequestConfirmation` then transaction. Constraint targets are addressed by `handleIndex` + a target descriptor (`{targetType: host_face|cover|other_rebar, targetId?}`), never by raw `Reference`.
- **`propagate_rebar`** (write): inputs `rebarId`, optional `targetHostIds[]`. Use `ReinforcementUtils` propagation (2024+); on R2023 return `MinVersionError("Rebar propagation", 2024)`. `RequestConfirmation("propagate rebar", targetCount)`.
- **`unify_rebars`** (write): inputs `rebarIds[]`. Unify compatible standalone bars into one set. Use the documented unify utility (verify exact entry point in the XML doc; if absent on a target, return `MinVersionError`). `RequestConfirmation("unify rebars", count)`.
- **`transfer_rebar_annotations`** (write): inputs `sourceViewId`, `targetViewId`. Use `MultiReferenceAnnotation` APIs to copy rebar tag/dimension annotations between matching views. `RequestConfirmation("transfer rebar annotations", count)`.
- **`get_rebar_coupler_data`** (read): inputs `couplerId`. Return `couplerMark`, linked reinforcement data (rebar id/position/end for each side), quantity.
- **`get_rebar_constraint_candidates`** (read): inputs `rebarId`, `handleIndex`. Return candidate descriptors for that handle (2025+ method; ≤2024 fallback).

Build R25 + R24 + R26 after writing these.

- [ ] **Step 4: Append the splice tools (`#if REVIT2025_OR_GREATER`)**

- **`splice_rebar`** (write, 2025+): inputs `rebarId`, optional `spliceTypeId`, `position` (`End1|Middle|End2`). Outside the `#if`, return `MinVersionError("Rebar splices", 2025)`.
- **`remove_rebar_splice`** (write, 2025+): inputs `rebarId`. Call `Rebar.RemoveSplice()`.
- **`get_rebar_splice_data`** (read, 2025+): inputs `rebarId`. Return lap length (mm), stagger (mm), splice position.
- **`get_rebar_splice_candidates`** (read, 2025+): inputs `rebarId`. Return candidate splice positions/types via `RebarSpliceUtils`.

Each gated tool: full body inside `#if REVIT2025_OR_GREATER ... #else return RebarToolHelpers.MinVersionError("...", 2025); #endif`. Build R24 (must compile — the `#else` path) AND R25/R26 (the `#if` path).

- [ ] **Step 5: Add the 12 Module 5 server wrappers to `RebarTools.cs`.** Build server.

- [ ] **Step 6: Bump tool-count threshold `173` → `185`; run it.** Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarAdvancedTools.cs src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(rebar): add Module 5 couplers, constraints, propagation, splices"
```

**End of Step 5.**

---

## Step 6 — Settings, numbering, rounding, bending details (Module 6)

File: `src/RevitCortex.Tools/Rebar/RebarSettingsTools.cs`. Bending-detail tools are `#if REVIT2024_OR_GREATER`-gated.

### Reference API signatures

```text
ReinforcementSettings.GetReinforcementSettings(Document) -> ReinforcementSettings
  props: HostStructuralRebar, RebarShapeDefinesHooks, RebarShapeDefinesEndTreatments (version-dependent)
  GetRebarRoundingManager() / GetFabricRoundingManager() -> ReinforcementRoundingManager
ReinforcementRoundingManager props: ApplyRebarRoundingRules, RebarLengthRoundingMethod, RebarLengthRounding, ...
Rebar.GetReinforcementRoundingManager() -> element-level override manager
RebarBendingDetail (2024+) — bending detail view element; RebarBendingDetailType
```

## Task 10: Module 6 — settings/rounding/numbering + bending details

**Files:**
- Create: `src/RevitCortex.Tools/Rebar/RebarSettingsTools.cs`

- [ ] **Step 1: Create the file with `set_reinforcement_settings` (FULL code)**

```csharp
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

/// <summary>Sets document-level reinforcement settings. Only provided fields are changed.</summary>
public class SetReinforcementSettingsTool : ICortexTool
{
    public string Name => "set_reinforcement_settings";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set document-level reinforcement settings. Optional fields: hostStructuralRebar (bool), rebarShapeDefinesHooks (bool), rebarShapeDefinesEndTreatments (bool). Some toggles are only allowed when the document has no reinforcement.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var setHost = input["hostStructuralRebar"]?.Value<bool?>();
        var setDefinesHooks = input["rebarShapeDefinesHooks"]?.Value<bool?>();
        var setDefinesEndTreatments = input["rebarShapeDefinesEndTreatments"]?.Value<bool?>();
        if (setHost == null && setDefinesHooks == null && setDefinesEndTreatments == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Provide at least one setting to change");

        if (!session.RequestConfirmation("change reinforcement settings", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc!, "RevitCortex: Set Reinforcement Settings");
        tx.Start();
        var warnings = new System.Collections.Generic.List<string>();
        try
        {
            var s = ReinforcementSettings.GetReinforcementSettings(doc!);
            if (setHost != null) s.HostStructuralRebar = setHost.Value;
            if (setDefinesHooks != null)
            {
                try { s.RebarShapeDefinesHooks = setDefinesHooks.Value; }
                catch (Exception ex) { warnings.Add($"rebarShapeDefinesHooks not changed: {ex.Message}"); }
            }
            if (setDefinesEndTreatments != null)
            {
                try { s.RebarShapeDefinesEndTreatments = setDefinesEndTreatments.Value; }
                catch (Exception ex) { warnings.Add($"rebarShapeDefinesEndTreatments not changed: {ex.Message}"); }
            }
            tx.Commit();
            return CortexResult<object>.Ok(new { changed = true, warnings });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set reinforcement settings: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build R25 + R24 (Tools).** Expected: both succeed.

- [ ] **Step 3: Append remaining Module 6 tools** (standard skeleton). Contracts:

- **`get_rebar_rounding`** (read): no/optional `rebarId` (element-level override vs document default). Return `applyRebarRoundingRules`, length rounding method + value (mm), volume rounding.
- **`get_fabric_rounding`** (read): document fabric rounding manager — same shape.
- **`manage_rebar_rounding`** (write): inputs `rebarId?`, `applyRules` (bool), `lengthRoundingMm?`, `lengthRoundingMethod?` (`Nearest|Up|Down`), `volumeRounding?`. Resolve the document or element-level `ReinforcementRoundingManager`, set provided fields inside a transaction.
- **`manage_fabric_rounding`** (write): same as above for the fabric rounding manager.
- **`manage_rebar_numbering`** (write): inputs `action` (`renumber|remove_gaps|set_number`), `rebarId?`, `newNumber?`. For `set_number`, write the rebar's `REBAR_NUMBER` parameter (editable from 2027; on older targets it may be read-only — catch and surface a `warnings` entry rather than failing the whole call). For `renumber`/`remove_gaps`, use the numbering schema APIs where available; if unavailable on the target, return `MinVersionError`.
- **`get_rebar_numbering`** (read): inputs `rebarId?` or category-wide. Return current schedule marks / numbers and gaps.
- **`create_rebar_bending_detail`** (write, 2024+): inputs `rebarId`, `viewId` (a drafting view to host the detail). Create a `RebarBendingDetail`; on R2023 return `MinVersionError("Bending details", 2024)`.
- **`modify_rebar_bending_detail`** (write, 2024+): inputs `bendingDetailId`, optional positioning fields. On R2023 return `MinVersionError(...)`.
- **`get_rebar_bending_detail_data`** (read, 2024+): inputs `bendingDetailId`. Return host rebar id, view id, segment/bend summary. On R2023 return `MinVersionError(...)`.

Each 2024+ tool wraps its body in `#if REVIT2024_OR_GREATER ... #else return RebarToolHelpers.MinVersionError("...", 2024); #endif`. Build R23 (the `#else` path must compile) + R24 + R25.

- [ ] **Step 4: Add the 10 Module 6 server wrappers to `RebarTools.cs`.** Build server.

- [ ] **Step 5: Bump tool-count threshold `185` → `195`; run it.** Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/RevitCortex.Tools/Rebar/RebarSettingsTools.cs src/RevitCortex.Server/Tools/RebarTools.cs src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "feat(rebar): add Module 6 settings, rounding, numbering, bending details"
```

**End of Step 6.** All 6 modules implemented.

---

## Step 7 — Forwarding source test, full verification, schema regen, docs

## Task 11: Server forwarding source test

**Files:**
- Create: `src/RevitCortex.Tests/Rebar/RebarServerForwardingSourceTests.cs`

Source-text assertions catch wrapper/plugin key mismatches (a known footgun — see project memory on wrapper/plugin param drift). They read the wrapper `.cs` as text and assert the right `JObject` keys are forwarded.

- [ ] **Step 1: Write the test**

```csharp
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Rebar;

public class RebarServerForwardingSourceTests
{
    private static string ReadRebarTools()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Server", "Tools", "RebarTools.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void CreateRebarFromShape_ForwardsHostAndVectors()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"hostId\"] = hostId", src);
        Assert.Contains("[\"origin\"] = JObject.Parse(origin)", src);
        Assert.Contains("[\"xVec\"] = JObject.Parse(xVec)", src);
        Assert.Contains("[\"yVec\"] = JObject.Parse(yVec)", src);
    }

    [Fact]
    public void SetRebarLayout_ForwardsLayoutObject()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"layout\"] = JObject.Parse(layout)", src);
    }

    [Fact]
    public void CreateAreaReinforcement_ForwardsMajorDirection()
    {
        var src = ReadRebarTools();
        Assert.Contains("[\"majorDirection\"] = JObject.Parse(majorDirection)", src);
    }

    [Fact]
    public void CreateRebarCoupler_ForwardsEndDescriptors()
    {
        var src = ReadRebarTools();
        Assert.Contains("create_rebar_coupler", src);
        Assert.Contains("[\"end1\"]", src);
    }
}
```

- [ ] **Step 2: Run; fix any mismatch in `RebarTools.cs` so the keys line up exactly**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~RebarServerForwardingSourceTests"`
Expected: PASS. If a test fails, the wrapper forwards a key the plugin doesn't read (or vice-versa) — align the wrapper `JObject` key to the plugin's `input["..."]` read. This is the exact class of bug the test exists to catch.

- [ ] **Step 3: Commit**

```powershell
git add src/RevitCortex.Tests/Rebar/RebarServerForwardingSourceTests.cs
git commit -m "test(rebar): assert server wrappers forward correct JObject keys"
```

---

## Task 12: Full multi-target build, schema regeneration, final test count

**Files:**
- Modify: `tool-schemas.txt` (regenerated)
- Modify: `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` (final count)

- [ ] **Step 1: Build all 5 plugin targets**

```powershell
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Expected: R23/R24/R25/R26 succeed. R27 succeeds **iff** the build machine has the .NET 10 SDK; otherwise it fails with `NETSDK1045` — that is an environment gap, not a code defect. Record which targets were validated. Building the **Plugin** also compiles the **Tools** project it references, but a green Plugin build can mask a Tools error in some cases — so ALSO run the explicit Tools build for R24 + R26 (the two net48/termination edges):

```powershell
dotnet build -c "Debug R24" src/RevitCortex.Tools/RevitCortex.Tools.csproj
dotnet build -c "Debug R26" src/RevitCortex.Tools/RevitCortex.Tools.csproj
```

- [ ] **Step 2: Build the server**

Run: `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`
Expected: succeeds.

- [ ] **Step 3: Regenerate `tool-schemas.txt`**

Run: `node server/generate-tool-schemas-csharp.mjs`
Expected: the script rewrites `tool-schemas.txt`. Confirm the new rebar tool signatures appear:

```powershell
Select-String -Path tool-schemas.txt -Pattern "rebar|fabric_area|fabric_sheet|reinforcement" | Measure-Object
```
Expected: a non-zero count covering all ~37 new tools.

- [ ] **Step 4: Set the final tool-count threshold**

The total rebar tools added = 12 (M1) + 12 (M2) + 8 (M3) + 8 (M4) + 12 (M5) + 10 (M6) = **62**. Baseline ≥133 → final threshold **≥195**. Confirm `ToolRegistrationTests.ToolCount_MatchesExpected` reads `>= 195` (set incrementally in Tasks 4/6/7/8/9/10 — verify the final value here).

- [ ] **Step 5: Run the FULL no-Revit test suite**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: all pass; only `[RequiresRevitApiFact]` tests skip. Target: **≥ 233 passed / 1 skipped / 0 failed** (221 baseline + 4 parser + 4 server-contract + 4 forwarding + the new InlineData rows).

- [ ] **Step 6: Commit**

```powershell
git add tool-schemas.txt src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs
git commit -m "chore(rebar): regenerate tool-schemas, finalize tool count"
```

---

## Task 13: Documentation + manual smoke-test checklist

**Files:**
- Modify: `docs/USER_GUIDE.md`
- Modify: `WORKFLOWS.md`

- [ ] **Step 1: Add a "Rebar / Reinforcement" section to `docs/USER_GUIDE.md`**

Insert a new section listing the 6 modules and their tools, with these worked examples (verbatim) so users can copy-paste:

````markdown
## Rebar / Reinforcement

RevitCortex exposes 62 reinforcement tools (category **Rebar**). Inputs are in **millimetres** and **degrees**; ids/`OST_*`/enum names are language-independent. Before placing rebar, confirm the host is valid with `get_rebar_host_data`, and check version-gated availability with `get_rebar_api_capabilities`.

### Discover the reinforcement setup
```
list_rebar_bar_types
list_rebar_shapes
list_rebar_hook_types
get_rebar_host_data { "hostId": 123456 }
```

### Place a shape-driven bar set in a column
```
create_rebar_from_shape {
  "hostId": 123456,
  "shapeId": 200100,
  "barTypeId": 200050,
  "origin": {"x":0,"y":0,"z":0},
  "xVec": {"x":1,"y":0,"z":0},
  "yVec": {"x":0,"y":0,"z":1},
  "layout": {"rule":"number_with_spacing","number":8,"spacingMm":200}
}
```

### Area reinforcement on a slab
```
create_area_reinforcement {
  "hostId": 654321,
  "majorDirection": {"x":1,"y":0,"z":0},
  "barTypeId": 200050
}
```

### Inspect bar geometry & quantities
```
get_rebar_element_data { "rebarId": 300999 }
get_rebar_geometry { "rebarId": 300999, "suppressHooks": true }
```

### Version-gated features
- Splices (`splice_rebar`, `get_rebar_splice_data`): Revit 2025+.
- Terminations (`set_rebar_terminations`): Revit 2026+.
- Bending details (`create_rebar_bending_detail`): Revit 2024+.
On older targets these return a structured error naming the minimum Revit version.
````

- [ ] **Step 2: Add reinforcement workflows + an operational warning to `WORKFLOWS.md`**

Append:

````markdown
## Reinforcement (Rebar) Workflows

### Session R1 — Discover reinforcement setup (open & close)
1. `list_rebar_bar_types`, `list_rebar_shapes`, `list_rebar_hook_types`
2. `get_rebar_api_capabilities` (record which gated features the model's Revit supports)
3. `get_rebar_host_data` on the target host(s)
→ Close session; record bar-type/shape ids.

### Session R2 — Place bars (open & close)
1. `get_rebar_host_data { hostId }` → confirm `isValidHost: true`
2. `create_rebar_from_shape` (or `create_rebar_from_curves`)
3. `set_rebar_layout` to adjust spacing/number
4. Spot-check with `get_rebar_element_data`
→ Close session.

### Session R3 — Area/Path/Fabric systems (open & close)
1. `create_area_reinforcement` / `create_path_reinforcement` / `create_fabric_area`
2. Inspect with `get_area_reinforcement_data` / `get_path_reinforcement_data` / `get_fabric_area_data`
3. If needed, `convert_rebar_system_to_rebars` (DESTRUCTIVE — confirmation dialog)
→ Close session.

**Operational warning:** Always pass explicit ids and millimetre inputs to rebar creation tools. Never assume localized reinforcement parameter names — read by id/enum. A non-structural or non-concrete host will be rejected by `get_rebar_host_data`/`create_*`; mark the host structural (or set a concrete material) first.
````

- [ ] **Step 3: Commit the docs**

```powershell
git add docs/USER_GUIDE.md WORKFLOWS.md
git commit -m "docs(rebar): add reinforcement section + workflows"
```

- [ ] **Step 4: Manual smoke tests in Revit (record results in the PR/commit message)**

Deploy with `deploy.ps1`, restart Revit, open a structural sample with concrete hosts, start Cortex Switch, then exercise:

1. `list_rebar_bar_types`, `list_rebar_hook_types`, `list_rebar_shapes`, `list_rebar_cover_types` → non-empty.
2. `get_rebar_api_capabilities` → matches the running Revit year.
3. `get_rebar_host_data` on a concrete column → `isValidHost: true`; on a generic model → `isValidHost: false`.
4. `create_rebar_from_shape` in the column → returns a `rebarId`; visible in Revit.
5. `set_rebar_layout` (`number_with_spacing`) → bar count updates.
6. `get_rebar_geometry` → curve count > 0, lengths in mm sane.
7. `create_area_reinforcement` on a slab/wall → member ids returned.
8. `create_path_reinforcement` along a slab edge → member ids returned.
9. `create_fabric_area` + `create_fabric_sheet` → sheet ids returned.
10. `create_rebar_coupler` linking two bar ends → coupler id returned.
11. Splice (`splice_rebar`) — only on R2025+; on R2024 it returns the version error.
12. Bending detail (`create_rebar_bending_detail`) — only on R2024+.
13. Enable read-only mode in Settings → `create_rebar_from_shape` returns `PermissionDenied`; `list_rebar_bar_types` still works.
14. Cancel a confirmation dialog on `create_rebar_from_shape` → returns `Cancelled`.

- [ ] **Step 5: Final pre-release build matrix + (optional) release**

Per the project release flow, before tagging a release run all 5 `Release` configs and `deploy.ps1` for each target, then follow `reference_release_flow` (`release.ps1` + `gh release create` on `LuDattilo/revitcortex-releases`). This is OUT OF SCOPE for the feature plan itself — it is the separate release step the user runs when ready.

---

## Self-review checklist (run before handing off for execution)

- [ ] **Spec coverage:** every tool in the spec's Modules 1–6 maps to a task (M1→T2/T3, M2→T5/T6, M3→T7, M4→T8, M5→T9, M6→T10). Server APIs (IRebarUpdateServer etc.) intentionally not scriptable — covered by `get_rebar_api_capabilities` (T2) per spec "External Server APIs".
- [ ] **No placeholders:** the 11 fully-coded tools (helpers, 4 discovery, from_shape, from_curves, free_form, set_layout, set_shape, set_host, set_visibility, set_hooks, set_terminations, move, include/exclude, split, create_area, create_fabric, create_coupler, set_settings) contain complete bodies. Remaining tools have explicit input/method/return contracts referencing only APIs in the verified catalog.
- [ ] **Type consistency:** `RebarToolHelpers.LayoutSpec`/`LayoutRuleKind`/`ApplyLayout`/`RequireRebar`/`RequireHost`/`ResolveRebar*`/`ParseXyzMm`/`ParseCurveSpecsMm`/`CurveToDtoMm`/`MinVersionError` names are used identically in every task. `ToolHelpers.ToElementId`/`GetElementIdValue`/`RequireDocument` are the real existing helpers.
- [ ] **Version gating:** every 2024+/2025+/2026+ branch has a compiling `#else` returning `MinVersionError`, validated by the R23/R24 builds.
- [ ] **Read-only correctness:** all read tools use `get_`/`list_`; all write tools (including `manage_*`, `create_*`, `set_*`, `split_*`, `propagate_*`, `unify_*`, `convert_*`, `remove_*`, `place_*`, `transfer_*`) do NOT — so `CortexRouter.IsReadOnlyTool` blocks them in read-only mode. Confirmed by Task 4 + Task 6 InlineData.


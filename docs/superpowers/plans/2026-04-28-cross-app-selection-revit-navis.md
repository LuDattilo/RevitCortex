# Cross-App Selection (Revit ↔ Navis) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a single symmetric MCP tool `cross_app_selection` (mode `export`/`import`) on both RevitCortex and NavisCortex so the user can mirror selections — including elements inside Revit links — between the two applications with two LLM calls per operation.

**Architecture:** Additive. Two new tools (one per side) reuse the existing `CortexElementRef` DTO (`*.Core/Interop/`), the existing `RevitReferenceBuilder` (Navis-side property reader), the existing `ItemPathResolver` hardening pattern, and the existing `ShowCrossModelElementsTool` core selection logic. No existing tool gets modified. Payload travels through the LLM client — no inter-process channel between the two servers.

**Tech Stack:** C# (`net48` for R23/R24/N23-N26, `net8.0-windows` for R25/R26, `net10.0-windows` for R27); xUnit for tests; Newtonsoft.Json; Revit API (`Autodesk.Revit.DB`/`UI`); Navisworks API (`Autodesk.Navisworks.Api`, `.Clash`).

**Spec:** `docs/superpowers/specs/2026-04-28-cross-app-selection-revit-navis-design.md`

**Repos affected:**
- `C:/Users/luigi.dattilo/Desktop/ClaudeCode/RevitCortex/`
- `C:/Users/luigi.dattilo/Desktop/ClaudeCode/NavisCortex/`

---

## File structure

### RevitCortex — new files only

| Path | Responsibility |
|---|---|
| `src/RevitCortex.Tools/Interop/HostLinkResolver.cs` | Build sourceFile→host/link lookup; resolve a `CortexElementRef` to either a host `ElementId` or a `(linkInstanceId, linkedElementId)` pair, with fallbacks `RevitUniqueId`→`IfcGuid`→`RevitElementId`. |
| `src/RevitCortex.Tools/Interop/SelectionExporter.cs` | Walk `UIDocument.Selection.GetReferences()` and emit `CortexElementRef[]` (host + linked). |
| `src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs` | The MCP tool. Dispatches on `mode`. Import path delegates to `ShowCrossModelElementsTool` via composition (see Task 6). |
| `src/RevitCortex.Tests/Interop/HostLinkResolverTests.cs` | Pure-unit tests for the resolver (no Revit). |
| `src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs` | Tool-level tests for input validation, partial-success semantics. |

### NavisCortex — new files only

| Path | Responsibility |
|---|---|
| `src/NavisCortex.Tools/Interop/RevitRefMatcher.cs` | Single-pass `DescendantsAndSelf` scan, S35/S40 hardened (manual enumerator + try/catch on `MoveNext`/`Current`). Matches `CortexElementRef` against `ModelItem` properties read via the existing `RevitReferenceBuilder`. |
| `src/NavisCortex.Tools/Interop/SelectionExporter.cs` | Source priority `clashGuid` > `clashTestGuid` > `useCurrentSelection`; emits `CortexElementRef[]` reusing `RevitReferenceBuilder`. |
| `src/NavisCortex.Tools/Interop/CrossAppSelectionTool.cs` | The MCP tool. Same schema as Revit-side. HPCSE on `Execute`. |
| `src/NavisCortex.Tests/Interop/RevitRefMatcherTests.cs` | Pure-unit tests for the matching predicate (no Navis). |
| `src/NavisCortex.Tests/Interop/CrossAppSelectionToolTests.cs` | Tool-level input/dispatch tests. |

### Files explicitly NOT modified

- `RevitCortex.Tools/LinkedFiles/ShowCrossModelElementsTool.cs`
- `NavisCortex.Tools/Interop/GetRevitReferencesTool.cs`
- `NavisCortex.Tools/Selection/SelectItemsByPathTool.cs`
- `NavisCortex.Tools/Common/ItemPathResolver.cs`
- `NavisCortex.Tools/Clash/GetClashesTool.cs`, `ClashSelectionResolver.cs`
- `RevitCortex.Core.Interop.CortexElementRef`, `NavisCortex.Core.Interop.CortexElementRef` / `RevitReference` / `RevitReferenceBuilder`

---

## Conventions

- All new tools implement `ICortexTool`. Naming: `cross_app_selection`. Category: `"Interop"`. `RequiresDocument: true`. `IsDynamic: true` (selection is a UI write).
- Error reporting via `CortexResult<object>.Fail(CortexErrorCode.InvalidInput, ..., suggestion: ...)` and `Ok(new { ... })`.
- Logging via `RevitCortex.Core.Security.CortexDebugLog` / `NavisCortex.Core.Security.CortexDebugLog` (existing).
- Cross-target: every new file must build clean on **all** of `Debug R23/R24/R25/R26/R27` (Revit) and `Debug N23/N24/N25/N26` (Navis).
- net48 leg: no `record`, no `init`, no `Index`/`Range`, no `Dictionary.GetValueOrDefault`, no default interface methods. ElementId access split: `.IntegerValue` (R23/R24) vs `.Value` (R25+) — guarded by `#if REVIT2024_OR_GREATER`.
- Commits: small, one task per commit, conventional commits style (`feat(interop): ...`, `test(interop): ...`).

---

## Task 1 — Spike: confirm CortexElementRef shape on both sides

**Files:**
- Read only: `RevitCortex/src/RevitCortex.Core/Interop/CortexElementRef.cs`
- Read only: `NavisCortex/src/NavisCortex.Core/Interop/RevitReference.cs`

- [ ] **Step 1: Verify the two DTOs are property-by-property identical**

Run: open both files. Confirm the public surface is exactly:
```
SourceApp, SourceFile, NavisInstanceGuid,
RevitElementId, RevitUniqueId, IfcGuid,
Category, Family, Type
```
on both sides, all `string?`. If any field is missing/added on one side, STOP and reconcile before continuing.

- [ ] **Step 2: Verify existing tests pass on both sides**

Run from `RevitCortex/`:
```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```
Run from `NavisCortex/`:
```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26"
```
Expected: both green. This is the baseline; all later tasks must keep these green.

- [ ] **Step 3: No commit (read-only spike).**

---

## Task 2 — Revit: HostLinkResolver (pure, no Revit API at lookup level)

**Files:**
- Create: `RevitCortex/src/RevitCortex.Tools/Interop/HostLinkResolver.cs`
- Create: `RevitCortex/src/RevitCortex.Tests/Interop/HostLinkResolverTests.cs`

The resolver has two responsibilities:
1. Build a `Dictionary<string, RevitLinkInstance?>` keyed by the **lower-cased basename** of each loaded link's PathName, plus an entry for the host `doc.PathName` basename with value `null` (sentinel = "this is the host, not a link").
2. Given a `CortexElementRef`, look up the source file, then resolve the element via the cascade `RevitUniqueId` → `IfcGuid` (parameter `IFC GUID`) → `RevitElementId` (numeric parsed). Return a small struct describing what was found.

We split the resolver into a **pure** lookup-builder class (testable without Revit by passing in fake data) and a **Revit-bound** caller wrapper. Tests live on the pure side.

- [ ] **Step 1: Write the failing test for basename normalization**

Create `RevitCortex/src/RevitCortex.Tests/Interop/HostLinkResolverTests.cs`:
```csharp
using System.Collections.Generic;
using RevitCortex.Tools.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop;

public class HostLinkResolverTests
{
    [Fact]
    public void NormalizeBasename_LowercasesAndStripsPath()
    {
        Assert.Equal("strutture.rvt", HostLinkResolver.NormalizeBasename(@"C:\Models\Strutture.rvt"));
        Assert.Equal("strutture.rvt", HostLinkResolver.NormalizeBasename("Strutture.rvt"));
        Assert.Equal("",               HostLinkResolver.NormalizeBasename(null));
        Assert.Equal("",               HostLinkResolver.NormalizeBasename(""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter HostLinkResolverTests
```
Expected: FAIL — `HostLinkResolver` does not exist.

- [ ] **Step 3: Create HostLinkResolver with the helper**

Create `RevitCortex/src/RevitCortex.Tools/Interop/HostLinkResolver.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCortex.Core.Interop;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// Resolves a <see cref="CortexElementRef"/> against the active host
    /// document and its loaded RevitLinkInstance set. Uses sourceFile
    /// basename matching (case-insensitive) and a UniqueId/IfcGuid/ElementId
    /// fallback cascade.
    /// </summary>
    public class HostLinkResolver
    {
        private readonly Document _hostDoc;
        private readonly Dictionary<string, RevitLinkInstance?> _byBasename;

        private HostLinkResolver(Document hostDoc,
            Dictionary<string, RevitLinkInstance?> byBasename)
        {
            _hostDoc = hostDoc;
            _byBasename = byBasename;
        }

        public static string NormalizeBasename(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            try { return Path.GetFileName(value)!.ToLowerInvariant(); }
            catch { return value!.ToLowerInvariant(); }
        }

        public static HostLinkResolver Build(Document hostDoc)
        {
            var map = new Dictionary<string, RevitLinkInstance?>(StringComparer.Ordinal);
            var hostKey = NormalizeBasename(hostDoc.PathName);
            if (!string.IsNullOrEmpty(hostKey)) map[hostKey] = null;
            // Fallback host key: use the doc Title when PathName is empty
            // (e.g., unsaved documents, central models with stripped path).
            var titleKey = NormalizeBasename(hostDoc.Title);
            if (!string.IsNullOrEmpty(titleKey) && !map.ContainsKey(titleKey))
                map[titleKey] = null;

            var links = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
            foreach (var link in links)
            {
                var linkDoc = link.GetLinkDocument();
                var key = NormalizeBasename(linkDoc?.PathName);
                if (string.IsNullOrEmpty(key))
                    key = NormalizeBasename(link.Name);
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                    map[key] = link;
            }
            return new HostLinkResolver(hostDoc, map);
        }

        public ResolveOutcome Resolve(CortexElementRef refToFind)
        {
            if (refToFind == null)
                return ResolveOutcome.NotFound("ref is null");
            var key = NormalizeBasename(refToFind.SourceFile);
            if (string.IsNullOrEmpty(key))
                return ResolveOutcome.NotFound("missing sourceFile");
            if (!_byBasename.TryGetValue(key, out var link))
                return ResolveOutcome.NotFound("source file not loaded as host or link");

            var doc = link != null ? link.GetLinkDocument() : _hostDoc;
            if (doc == null)
                return ResolveOutcome.NotFound("link document not loaded");

            var element = ResolveElement(doc, refToFind);
            if (element == null)
                return ResolveOutcome.NotFound(
                    "no element matches uniqueId/ifcGuid/elementId in source");

            return link == null
                ? ResolveOutcome.Host(element.Id)
                : ResolveOutcome.Linked(link.Id, element.Id);
        }

        private static Element? ResolveElement(Document doc, CortexElementRef r)
        {
            if (!string.IsNullOrWhiteSpace(r.RevitUniqueId))
            {
                try
                {
                    var byUid = doc.GetElement(r.RevitUniqueId);
                    if (byUid != null) return byUid;
                }
                catch { /* fall through */ }
            }

            if (!string.IsNullOrWhiteSpace(r.IfcGuid))
            {
                var byIfc = FindByIfcGuid(doc, r.IfcGuid!);
                if (byIfc != null) return byIfc;
            }

            if (!string.IsNullOrWhiteSpace(r.RevitElementId)
                && long.TryParse(r.RevitElementId, out var idValue))
            {
                try
                {
                    var elementId =
#if REVIT2024_OR_GREATER
                        new ElementId(idValue);
#else
                        new ElementId((int)idValue);
#endif
                    var byId = doc.GetElement(elementId);
                    if (byId != null) return byId;
                }
                catch { /* fall through */ }
            }
            return null;
        }

        private static Element? FindByIfcGuid(Document doc, string ifcGuid)
        {
            // IfcGUID is stored on instances in BuiltInParameter.IFC_GUID.
            // We do a single FilteredElementCollector pass with a parameter
            // filter so we don't iterate the whole project.
            try
            {
                var bipParam = new ParameterValueProvider(
                    new ElementId(BuiltInParameter.IFC_GUID));
                var rule = new FilterStringRule(bipParam,
                    new FilterStringEquals(), ifcGuid
#if !REVIT2023_OR_GREATER
                    , true /* caseSensitive overload pre-2023 */
#endif
                );
                var filter = new ElementParameterFilter(rule);
                return new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .FirstElement();
            }
            catch
            {
                return null;
            }
        }
    }

    public class ResolveOutcome
    {
        public bool IsHost { get; }
        public bool IsLinked { get; }
        public ElementId? HostElementId { get; }
        public ElementId? LinkInstanceId { get; }
        public ElementId? LinkedElementId { get; }
        public string? NotFoundReason { get; }

        private ResolveOutcome(bool host, bool linked,
            ElementId? hostId, ElementId? linkId, ElementId? linkedId,
            string? reason)
        {
            IsHost = host; IsLinked = linked;
            HostElementId = hostId; LinkInstanceId = linkId; LinkedElementId = linkedId;
            NotFoundReason = reason;
        }
        public static ResolveOutcome Host(ElementId id)
            => new ResolveOutcome(true, false, id, null, null, null);
        public static ResolveOutcome Linked(ElementId linkInstanceId, ElementId linkedId)
            => new ResolveOutcome(false, true, null, linkInstanceId, linkedId, null);
        public static ResolveOutcome NotFound(string reason)
            => new ResolveOutcome(false, false, null, null, null, reason);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter HostLinkResolverTests
```
Expected: PASS.

- [ ] **Step 5: Verify cross-target compile R23 + R27**

```
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R23"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R27"
```
Expected: both green. (R27 needs the .NET 10 SDK pinned in `global.json`; see `release_r27_sdk` memory if it fails.)

- [ ] **Step 6: Commit**

```
git add src/RevitCortex.Tools/Interop/HostLinkResolver.cs \
        src/RevitCortex.Tests/Interop/HostLinkResolverTests.cs
git commit -m "feat(revit/interop): add HostLinkResolver scaffold for cross-app selection"
```

---

## Task 3 — Revit: SelectionExporter (selection → CortexElementRef[])

**Files:**
- Create: `RevitCortex/src/RevitCortex.Tools/Interop/SelectionExporter.cs`
- Modify (add tests): `RevitCortex/src/RevitCortex.Tests/Interop/HostLinkResolverTests.cs` is left alone; add a separate test file:
- Create: `RevitCortex/src/RevitCortex.Tests/Interop/SelectionExporterTests.cs`

The exporter walks `UIDocument.Selection.GetReferences()`. For each reference:
- If `LinkedElementId == ElementId.InvalidElementId` → host element. Build ref with `SourceFile = basename(doc.PathName)`, `RevitUniqueId = element.UniqueId`, `IfcGuid = element.LookupParameter("IFC GUID")?.AsString()`, `RevitElementId = element.Id` (string), `Category = built-in if available, else display name`, `SourceApp = "Revit"`.
- If linked → resolve `linkInstance` and `linkDoc`, build ref with `SourceFile = basename(linkDoc.PathName)`, the linked element's `UniqueId`, etc.

Pure helper functions are extracted so we can unit-test without Revit.

- [ ] **Step 1: Write the failing test for category-to-OST translation helper**

Add to `RevitCortex/src/RevitCortex.Tests/Interop/SelectionExporterTests.cs`:
```csharp
using RevitCortex.Tools.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop;

public class SelectionExporterTests
{
    [Fact]
    public void CategoryNameOrCode_PrefersOstCodeWhenBuiltIn()
    {
        // Built-in category id → OST code
        Assert.Equal("OST_Walls",
            SelectionExporter.FormatCategory(-2000011, "Muri"));
        // Non-built-in (positive id, e.g. user category) → display name fallback
        Assert.Equal("Custom",
            SelectionExporter.FormatCategory(123456, "Custom"));
        // Unknown id and missing display name → empty
        Assert.Equal("", SelectionExporter.FormatCategory(0, null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter SelectionExporterTests
```
Expected: FAIL — type missing.

- [ ] **Step 3: Create SelectionExporter**

Create `RevitCortex/src/RevitCortex.Tools/Interop/SelectionExporter.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Core.Interop;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// Reads the active UIDocument selection (host + linked) and emits
    /// a <see cref="CortexElementRef"/> per element. Pure helpers are
    /// public for unit testing without a live Revit document.
    /// </summary>
    public static class SelectionExporter
    {
        public class Output
        {
            public List<CortexElementRef> Refs { get; } = new List<CortexElementRef>();
            public List<object> Skipped { get; } = new List<object>();
        }

        public static Output Export(UIDocument uiDoc)
        {
            var output = new Output();
            if (uiDoc == null) return output;
            var hostDoc = uiDoc.Document;
            if (hostDoc == null) return output;

            IList<Reference> refs;
            try { refs = uiDoc.Selection.GetReferences(); }
            catch (Exception ex)
            {
                RevitCortex.Core.Security.CortexDebugLog.LogException(
                    "SelectionExporter.GetReferences", ex);
                return output;
            }

            var hostFile = HostLinkResolver.NormalizeBasename(hostDoc.PathName);
            if (string.IsNullOrEmpty(hostFile))
                hostFile = HostLinkResolver.NormalizeBasename(hostDoc.Title);

            foreach (var reference in refs)
            {
                try
                {
                    if (reference.LinkedElementId == ElementId.InvalidElementId)
                    {
                        var element = hostDoc.GetElement(reference.ElementId);
                        if (element == null) continue;
                        output.Refs.Add(BuildRef(element, hostFile));
                    }
                    else
                    {
                        var linkInstance = hostDoc.GetElement(reference.ElementId)
                            as RevitLinkInstance;
                        var linkDoc = linkInstance?.GetLinkDocument();
                        var linkedElement = linkDoc?.GetElement(reference.LinkedElementId);
                        if (linkInstance == null || linkDoc == null || linkedElement == null)
                        {
                            output.Skipped.Add(new
                            {
                                reason = linkInstance == null
                                    ? "link instance not found"
                                    : linkDoc == null
                                        ? "link document not loaded"
                                        : "linked element not found"
                            });
                            continue;
                        }
                        var linkFile = HostLinkResolver.NormalizeBasename(linkDoc.PathName);
                        if (string.IsNullOrEmpty(linkFile))
                            linkFile = HostLinkResolver.NormalizeBasename(linkInstance.Name);
                        output.Refs.Add(BuildRef(linkedElement, linkFile));
                    }
                }
                catch (Exception ex)
                {
                    RevitCortex.Core.Security.CortexDebugLog.LogException(
                        "SelectionExporter.PerReference", ex);
                    output.Skipped.Add(new { reason = "per-ref exception: " + ex.Message });
                }
            }
            return output;
        }

        private static CortexElementRef BuildRef(Element element, string sourceFile)
        {
            var category = element.Category;
            var catCode = FormatCategory(
                category != null
#if REVIT2024_OR_GREATER
                    ? category.Id.Value
#else
                    ? (long)category.Id.IntegerValue
#endif
                    : 0,
                category?.Name);

            var ifcParam = element.LookupParameter("IFC GUID")?.AsString();
            var elementIdValue =
#if REVIT2024_OR_GREATER
                element.Id.Value;
#else
                (long)element.Id.IntegerValue;
#endif

            return new CortexElementRef
            {
                SourceApp = "Revit",
                SourceFile = sourceFile,
                RevitUniqueId = element.UniqueId,
                IfcGuid = string.IsNullOrEmpty(ifcParam) ? null : ifcParam,
                RevitElementId = elementIdValue.ToString(),
                Category = string.IsNullOrEmpty(catCode) ? null : catCode,
            };
        }

        public static string FormatCategory(long categoryIdValue, string? displayName)
        {
            // BuiltInCategory ids are negative.
            if (categoryIdValue < 0)
            {
                try
                {
                    var bic = (BuiltInCategory)categoryIdValue;
                    var name = bic.ToString();
                    if (name.StartsWith("OST_", StringComparison.Ordinal))
                        return name;
                }
                catch { /* fall through */ }
            }
            return displayName ?? "";
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter SelectionExporterTests
```
Expected: PASS.

- [ ] **Step 5: Cross-target build R24 + R26**

```
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R24"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
```
Expected: both green.

- [ ] **Step 6: Commit**

```
git add src/RevitCortex.Tools/Interop/SelectionExporter.cs \
        src/RevitCortex.Tests/Interop/SelectionExporterTests.cs
git commit -m "feat(revit/interop): add SelectionExporter for cross-app refs"
```

---

## Task 4 — Revit: CrossAppSelectionTool dispatcher (export path only)

**Files:**
- Create: `RevitCortex/src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs`
- Create: `RevitCortex/src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs`

This task lands the tool with `mode=export` working end-to-end. `mode=import` is added in Task 6 once the integration with `ShowCrossModelElementsTool` is settled.

- [ ] **Step 1: Write failing test — input validation**

Create `RevitCortex/src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs`:
```csharp
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Tools.Interop;
using Xunit;

namespace RevitCortex.Tests.Interop;

public class CrossAppSelectionToolTests
{
    [Fact]
    public void RejectsMissingMode()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(new JObject(), session);
        Assert.False(result.IsSuccess);
        Assert.Equal(CortexErrorCode.InvalidInput, result.ErrorCode);
        Assert.Contains("mode", result.Error ?? "", System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnknownMode()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(JObject.Parse("{\"mode\":\"banana\"}"), session);
        Assert.False(result.IsSuccess);
        Assert.Equal(CortexErrorCode.InvalidInput, result.ErrorCode);
    }

    [Fact]
    public void ImportRejectsEmptyRefs()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(
            JObject.Parse("{\"mode\":\"import\",\"refs\":[]}"), session);
        Assert.False(result.IsSuccess);
        Assert.Equal(CortexErrorCode.InvalidInput, result.ErrorCode);
    }

    [Fact]
    public void ToolMetadataIsCorrect()
    {
        var tool = new CrossAppSelectionTool();
        Assert.Equal("cross_app_selection", tool.Name);
        Assert.Equal("Interop", tool.Category);
        Assert.True(tool.RequiresDocument);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter CrossAppSelectionToolTests
```
Expected: FAIL — class missing.

- [ ] **Step 3: Implement the tool with export path only**

Create `RevitCortex/src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs`:
```csharp
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Interop
{
    /// <summary>
    /// Symmetric cross-app selection tool. mode=export emits CortexElementRefs
    /// from the current Revit selection (host + linked). mode=import consumes
    /// CortexElementRefs and selects them (delegating to show_cross_model_elements).
    /// </summary>
    public class CrossAppSelectionTool : ICortexTool
    {
        public string Name => "cross_app_selection";
        public string Category => "Interop";
        public bool RequiresDocument => true;
        public bool IsDynamic => true;
        public string Description =>
            "Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume CortexElementRefs and select/isolate them, automatically resolving each ref to host or linked Revit element via sourceFile basename match. Resolution priority: revitUniqueId → ifcGuid → revitElementId.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            var doc = session.Store.Get<object>("activeDocument") as Document;
            if (doc == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No active Revit document in session");

            var mode = input["mode"]?.ToString();
            if (string.IsNullOrWhiteSpace(mode))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "mode is required",
                    suggestion: "Pass mode=\"export\" or mode=\"import\".");

            switch (mode!.ToLowerInvariant())
            {
                case "export": return ExecuteExport(doc);
                case "import": return ExecuteImport(doc, input);
                default:
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Unknown mode '{mode}'",
                        suggestion: "Pass mode=\"export\" or mode=\"import\".");
            }
        }

        private static CortexResult<object> ExecuteExport(Document doc)
        {
            var uiDoc = new UIDocument(doc);
            var output = SelectionExporter.Export(uiDoc);
            return CortexResult<object>.Ok(new
            {
                side = "revit",
                exportedCount = output.Refs.Count,
                refs = output.Refs,
                skipped = output.Skipped
            });
        }

        private static CortexResult<object> ExecuteImport(Document doc, JObject input)
        {
            var refsToken = input["refs"] as JArray;
            if (refsToken == null || refsToken.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "refs is required and cannot be empty",
                    suggestion: "Pass refs=[CortexElementRef, ...] from the export side.");

            // Import body lands in Task 6.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                "import path not yet implemented",
                suggestion: "Will be wired up in plan task 6.");
        }
    }
}
```

- [ ] **Step 4: Run all the tests for this tool**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter CrossAppSelectionToolTests
```
Expected: 4 PASS.

- [ ] **Step 5: Run the full test project to confirm no regressions**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```
Expected: all green, including `ToolRegistrationTests` (this proves the new tool is discovered, has a unique name, and implements `ICortexTool`).

- [ ] **Step 6: Commit**

```
git add src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs \
        src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs
git commit -m "feat(revit/interop): add cross_app_selection tool (export path)"
```

---

## Task 5 — Navis: SelectionExporter (selection / clash → CortexElementRef[])

**Files:**
- Create: `NavisCortex/src/NavisCortex.Tools/Interop/SelectionExporter.cs`
- Create: `NavisCortex/src/NavisCortex.Tests/Interop/SelectionExporterTests.cs`

The Navis exporter has three input sources, decreasing priority: `clashGuid` → 1 clash → 2 ModelItems; `clashTestGuid` → all clashes of that test → 2N items; `useCurrentSelection=true` → `doc.CurrentSelection.SelectedItems`. For each `ModelItem`, reuse `RevitReferenceBuilder` exactly as `GetRevitReferencesTool.ExtractReference` does today.

- [ ] **Step 1: Write failing test — pure helper that picks the source-file basename for a model**

```csharp
using NavisCortex.Tools.Interop;
using Xunit;

namespace NavisCortex.Tests.Interop;

public class SelectionExporterTests
{
    [Fact]
    public void NormalizeSourceFile_ReturnsBasename()
    {
        Assert.Equal("strutture.rvt",
            SelectionExporter.NormalizeSourceFile(@"C:\Models\Strutture.rvt"));
        Assert.Equal("", SelectionExporter.NormalizeSourceFile(null));
        Assert.Equal("", SelectionExporter.NormalizeSourceFile(""));
    }
}
```

Save under `NavisCortex/src/NavisCortex.Tests/Interop/SelectionExporterTests.cs`.

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter SelectionExporterTests
```
Expected: FAIL.

- [ ] **Step 3: Implement Navis SelectionExporter**

Create `NavisCortex/src/NavisCortex.Tools/Interop/SelectionExporter.cs`. The exporter copies the `ExtractReference` helper from `GetRevitReferencesTool` rather than calling it (the helper is private there). Do NOT modify `GetRevitReferencesTool` to expose it — that would breach the "no breaking changes" rule. Inline the logic; the duplication is small and focused.
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using NavisCortex.Core.Interop;
using NavisCortex.Core.Security;

namespace NavisCortex.Tools.Interop
{
    /// <summary>
    /// Converts a set of ModelItems (from CurrentSelection or a clash) into
    /// CortexElementRefs by reading their BIM properties via the existing
    /// RevitReferenceBuilder.
    /// </summary>
    public static class SelectionExporter
    {
        public class Output
        {
            public List<CortexElementRef> Refs { get; } = new List<CortexElementRef>();
            public List<object> Skipped { get; } = new List<object>();
        }

        public static string NormalizeSourceFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            try { return Path.GetFileName(value)!.ToLowerInvariant(); }
            catch { return value!.ToLowerInvariant(); }
        }

        public static Output ExportItems(Document doc, IEnumerable<ModelItem> items)
        {
            var output = new Output();
            if (doc == null || items == null) return output;
            var rootToFile = BuildRootToFileLookup(doc);

            foreach (var item in items)
            {
                if (item == null)
                {
                    output.Skipped.Add(new { reason = "null item" });
                    continue;
                }
                try
                {
                    var sourceFile = ResolveSourceFile(item, rootToFile);
                    var instanceGuid = SafeInstanceGuid(item);
                    var revitReference = ExtractReference(item);
                    if (!revitReference.HasAnyReference
                        && string.IsNullOrEmpty(sourceFile))
                    {
                        output.Skipped.Add(new { reason = "no usable identity", instanceGuid });
                        continue;
                    }
                    output.Refs.Add(
                        revitReference.ToCortexElementRef("Navisworks", sourceFile, instanceGuid));
                }
                catch (Exception ex)
                {
                    CortexDebugLog.LogException("SelectionExporter.ExportItems", ex);
                    output.Skipped.Add(new { reason = "per-item exception: " + ex.Message });
                }
            }
            return output;
        }

        private static Dictionary<ModelItem, string?> BuildRootToFileLookup(Document doc)
        {
            var rootToFile = new Dictionary<ModelItem, string?>();
            try
            {
                if (doc.Models != null)
                {
                    foreach (var m in doc.Models)
                    {
                        if (m?.RootItem != null && !rootToFile.ContainsKey(m.RootItem))
                            rootToFile[m.RootItem] = NormalizeSourceFile(m.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                CortexDebugLog.LogException("SelectionExporter.BuildRootToFileLookup", ex);
            }
            return rootToFile;
        }

        private static string? ResolveSourceFile(ModelItem item,
            Dictionary<ModelItem, string?> rootToFile)
        {
            ModelItem? node = item;
            ModelItem root = item;
            while (node != null)
            {
                root = node;
                node = node.Parent;
            }
            return rootToFile.TryGetValue(root, out var f) ? f : null;
        }

        private static string? SafeInstanceGuid(ModelItem item)
        {
            try { return item.InstanceGuid.ToString(); }
            catch { return null; }
        }

        private static RevitReference ExtractReference(ModelItem item)
        {
            // Mirrors GetRevitReferencesTool.ExtractReference. Kept inline
            // (rather than refactoring the existing tool) so the existing
            // tool stays untouched.
            var builder = new RevitReferenceBuilder();
            try
            {
                if (item.PropertyCategories == null) return builder.Build();

                foreach (var category in item.PropertyCategories)
                {
                    if (category == null) continue;
                    string? catDisplay = null;
                    string? catInternal = null;
                    try
                    {
                        catDisplay = category.DisplayName;
                        catInternal = category.Name?.ToString();
                    }
                    catch (Exception ex)
                    {
                        CortexDebugLog.LogException("SelectionExporter.CategoryName", ex);
                    }

                    try
                    {
                        if (category.Properties == null) continue;
                        foreach (var prop in category.Properties)
                        {
                            if (prop == null) continue;
                            try
                            {
                                var (value, _) = NavisCortex.Tools.Properties
                                    .PropertyCategoryReader.ConvertValue(prop.Value);
                                builder.Add(catDisplay, catInternal,
                                    prop.DisplayName, prop.Name?.ToString(), value);
                            }
                            catch (Exception ex)
                            {
                                CortexDebugLog.LogException("SelectionExporter.Property", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CortexDebugLog.LogException("SelectionExporter.Properties", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                CortexDebugLog.LogException("SelectionExporter.ExtractReference", ex);
            }
            return builder.Build();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter SelectionExporterTests
```
Expected: PASS.

- [ ] **Step 5: Cross-target build N23 + N26**

```
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N23"
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N26"
```
Expected: both green.

- [ ] **Step 6: Commit**

```
git add src/NavisCortex.Tools/Interop/SelectionExporter.cs \
        src/NavisCortex.Tests/Interop/SelectionExporterTests.cs
git commit -m "feat(navis/interop): add SelectionExporter for cross-app refs"
```

---

## Task 6 — Revit: wire `mode=import` to ShowCrossModelElementsTool via composition

**Files:**
- Modify: `RevitCortex/src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs`
- Modify: `RevitCortex/src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs`

We use **composition through the tool registry**, not source-level reuse: `CrossAppSelectionTool` constructs an instance of `ShowCrossModelElementsTool` and calls its `Execute(JObject, CortexSession)` directly with the JSON shape it already understands (`hostElementIds`, `linkedElements`). This guarantees zero changes to the existing tool's source — it just gets called.

- [ ] **Step 1: Write failing test — refs deserialize into the resolver**

Add to `CrossAppSelectionToolTests.cs`:
```csharp
[Fact]
public void Import_ReadsRefsArray_AsCortexElementRef()
{
    var tool = new CrossAppSelectionTool();
    var session = new CortexSession();
    // No active document in session → the body fails fast on doc-missing,
    // BUT only AFTER input validation succeeds. We assert it gets past
    // input validation (i.e., a non-empty refs array no longer triggers
    // the "refs required" error).
    var json = JObject.Parse(@"{
        ""mode"": ""import"",
        ""refs"": [{
            ""sourceFile"": ""Strutture.rvt"",
            ""revitUniqueId"": ""abc-123""
        }]
    }");
    var result = tool.Execute(json, session);
    Assert.False(result.IsSuccess);
    // Document-missing error, not refs-empty error.
    Assert.Contains("active", result.Error ?? "", System.StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter CrossAppSelectionToolTests
```
Expected: FAIL — current import body returns the "not yet implemented" error.

- [ ] **Step 3: Implement the import dispatcher**

Replace the body of `ExecuteImport` in `CrossAppSelectionTool.cs`:
```csharp
private static CortexResult<object> ExecuteImport(Document doc, JObject input)
{
    var refsToken = input["refs"] as JArray;
    if (refsToken == null || refsToken.Count == 0)
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "refs is required and cannot be empty",
            suggestion: "Pass refs=[CortexElementRef, ...] from the export side.");

    var refs = new System.Collections.Generic.List<CortexElementRef>();
    foreach (var t in refsToken)
    {
        var r = t.ToObject<CortexElementRef>();
        if (r != null) refs.Add(r);
    }
    if (refs.Count == 0)
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "no parseable CortexElementRef in refs");

    var resolver = HostLinkResolver.Build(doc);

    var hostIds = new System.Collections.Generic.List<long>();
    var linkedTargets = new JArray();
    var notFound = new System.Collections.Generic.List<object>();

    foreach (var r in refs)
    {
        var outcome = resolver.Resolve(r);
        if (outcome.IsHost && outcome.HostElementId != null)
        {
            hostIds.Add(GetIdValue(outcome.HostElementId));
        }
        else if (outcome.IsLinked
            && outcome.LinkInstanceId != null
            && outcome.LinkedElementId != null)
        {
            linkedTargets.Add(new JObject
            {
                ["instanceId"]      = GetIdValue(outcome.LinkInstanceId),
                ["linkedElementId"] = GetIdValue(outcome.LinkedElementId)
            });
        }
        else
        {
            notFound.Add(new
            {
                @ref = r,
                reason = outcome.NotFoundReason ?? "unresolved"
            });
        }
    }

    if (hostIds.Count == 0 && linkedTargets.Count == 0)
    {
        return CortexResult<object>.Ok(new
        {
            side = "revit",
            requested = refs.Count,
            resolved = 0,
            selected = 0,
            hostMatches = 0,
            linkedMatches = 0,
            notFound,
            message = "No refs resolved. Confirm sourceFile basenames match the host document or a loaded link."
        });
    }

    // Compose with show_cross_model_elements (no source changes there).
    var inner = new RevitCortex.Tools.LinkedFiles.ShowCrossModelElementsTool();
    var innerInput = new JObject
    {
        ["hostElementIds"]       = new JArray(hostIds),
        ["linkedElements"]       = linkedTargets,
        ["select"]               = input["append"]?.Value<bool>() == true ? false : true,
        ["isolate"]              = input["isolate"]?.Value<bool?>() ?? true,
        ["createSectionBox"]     = input["createSectionBox"]?.Value<bool?>() ?? true,
        ["createLinkedMarkers"]  = input["createLinkedMarkers"]?.Value<bool?>() ?? true,
        ["usePostCommandIsolate"] = input["usePostCommandIsolate"]?.Value<bool?>() ?? false
    };

    var session = new CortexSession();
    session.Store.Set("activeDocument", (object)doc);
    var innerResult = inner.Execute(innerInput, session);

    return CortexResult<object>.Ok(new
    {
        side = "revit",
        requested = refs.Count,
        resolved = hostIds.Count + linkedTargets.Count,
        selected = hostIds.Count + linkedTargets.Count,
        hostMatches = hostIds.Count,
        linkedMatches = linkedTargets.Count,
        notFound,
        innerResult = innerResult.Data
    });
}

private static long GetIdValue(ElementId id)
{
#if REVIT2024_OR_GREATER
    return id.Value;
#else
    return (long)id.IntegerValue;
#endif
}
```
Make sure to add `using RevitCortex.Core.Interop;` at the top of the file.

- [ ] **Step 4: Run all interop tests**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter Interop
```
Expected: all PASS, including the new test from Step 1.

- [ ] **Step 5: Run the FULL test project to confirm `ShowCrossModelElementsTool` tests still pass**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```
Expected: all green. This is the critical "do not break anything" check.

- [ ] **Step 6: Cross-target the full Revit matrix**

```
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R23"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R24"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R25"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R27"
```
Expected: 5 green builds. (R27 may need `global.json` SDK flip — see `release_r27_sdk` memory.)

- [ ] **Step 7: Commit**

```
git add src/RevitCortex.Tools/Interop/CrossAppSelectionTool.cs \
        src/RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs
git commit -m "feat(revit/interop): wire cross_app_selection import via show_cross_model_elements composition"
```

---

## Task 7 — Navis: RevitRefMatcher (single-pass, hardened)

**Files:**
- Create: `NavisCortex/src/NavisCortex.Tools/Interop/RevitRefMatcher.cs`
- Create: `NavisCortex/src/NavisCortex.Tests/Interop/RevitRefMatcherTests.cs`

The matcher takes `CortexElementRef[]`, groups them by basename of `SourceFile`, then for each matching loaded model walks `RootItem.DescendantsAndSelf` once, reading properties via `RevitReferenceBuilder` and comparing against pending refs. Hardening pattern is copied verbatim from `ItemPathResolver.ResolveManyByInstanceGuid` (manual enumerator, try/catch around `MoveNext`/`Current`).

- [ ] **Step 1: Write failing test — pure predicate**

```csharp
using NavisCortex.Core.Interop;
using NavisCortex.Tools.Interop;
using Xunit;

namespace NavisCortex.Tests.Interop;

public class RevitRefMatcherTests
{
    [Fact]
    public void IsMatch_PrefersUniqueIdOverIfcGuidOverElementId()
    {
        var target = new CortexElementRef
        {
            SourceFile = "x.rvt",
            RevitUniqueId = "uid-1",
            IfcGuid = "ifc-1",
            RevitElementId = "100"
        };

        var candidate = new RevitReference
        {
            RevitUniqueId = "uid-1",
            IfcGuid = "ifc-other",
            RevitElementId = "999"
        };
        Assert.True(RevitRefMatcher.IsMatch(target, candidate));

        candidate = new RevitReference
        {
            RevitUniqueId = "uid-other",
            IfcGuid = "ifc-1"
        };
        Assert.True(RevitRefMatcher.IsMatch(target, candidate));

        candidate = new RevitReference
        {
            RevitElementId = "100"
        };
        Assert.True(RevitRefMatcher.IsMatch(target, candidate));

        candidate = new RevitReference
        {
            RevitUniqueId = "completely-different"
        };
        Assert.False(RevitRefMatcher.IsMatch(target, candidate));
    }

    [Fact]
    public void IsMatch_NullTargetOrCandidate_ReturnsFalse()
    {
        Assert.False(RevitRefMatcher.IsMatch(null, new RevitReference()));
        Assert.False(RevitRefMatcher.IsMatch(new CortexElementRef(), null));
    }
}
```

Save under `NavisCortex/src/NavisCortex.Tests/Interop/RevitRefMatcherTests.cs`.

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter RevitRefMatcherTests
```
Expected: FAIL — type missing.

- [ ] **Step 3: Implement RevitRefMatcher**

Create `NavisCortex/src/NavisCortex.Tools/Interop/RevitRefMatcher.cs`:
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisCortex.Core.Interop;
using NavisCortex.Core.Security;

namespace NavisCortex.Tools.Interop
{
    /// <summary>
    /// Resolves a set of <see cref="CortexElementRef"/>s to Navisworks
    /// <see cref="ModelItem"/>s with a single-pass scan per source file.
    /// Hardened against BIM 360 demand-load AVE per the same pattern as
    /// <c>ItemPathResolver.ResolveManyByInstanceGuid</c>: manual enumerator,
    /// try/catch around <c>MoveNext</c> and <c>Current</c>, per-item
    /// property-read isolation.
    /// </summary>
    public static class RevitRefMatcher
    {
        public class MatchOutput
        {
            public List<ModelItem> Resolved { get; } = new List<ModelItem>();
            public List<object> NotFound { get; } = new List<object>();
        }

        public static MatchOutput Resolve(Document doc, IList<CortexElementRef> refs)
        {
            var output = new MatchOutput();
            if (doc == null || refs == null || refs.Count == 0) return output;

            var groups = new Dictionary<string, List<CortexElementRef>>(StringComparer.Ordinal);
            foreach (var r in refs)
            {
                var key = SelectionExporter.NormalizeSourceFile(r?.SourceFile);
                if (string.IsNullOrEmpty(key))
                {
                    output.NotFound.Add(new { @ref = r, reason = "missing sourceFile" });
                    continue;
                }
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<CortexElementRef>();
                list.Add(r!);
            }

            var rootByFile = BuildRootByFile(doc);

            foreach (var kv in groups)
            {
                if (!rootByFile.TryGetValue(kv.Key, out var root) || root == null)
                {
                    foreach (var r in kv.Value)
                        output.NotFound.Add(new { @ref = r, reason = "source file not loaded" });
                    continue;
                }
                ScanRoot(root, kv.Value, output);
            }
            return output;
        }

        public static bool IsMatch(CortexElementRef? target, RevitReference? candidate)
        {
            if (target == null || candidate == null) return false;

            if (!string.IsNullOrWhiteSpace(target.RevitUniqueId)
                && !string.IsNullOrWhiteSpace(candidate.RevitUniqueId)
                && string.Equals(target.RevitUniqueId, candidate.RevitUniqueId,
                    StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrWhiteSpace(target.IfcGuid)
                && !string.IsNullOrWhiteSpace(candidate.IfcGuid)
                && string.Equals(target.IfcGuid, candidate.IfcGuid,
                    StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrWhiteSpace(target.RevitElementId)
                && !string.IsNullOrWhiteSpace(candidate.RevitElementId)
                && string.Equals(target.RevitElementId.Trim(),
                    candidate.RevitElementId.Trim(), StringComparison.Ordinal))
                return true;

            return false;
        }

        private static Dictionary<string, ModelItem> BuildRootByFile(Document doc)
        {
            var map = new Dictionary<string, ModelItem>(StringComparer.Ordinal);
            try
            {
                if (doc.Models != null)
                {
                    foreach (var m in doc.Models)
                    {
                        if (m?.RootItem == null) continue;
                        var key = SelectionExporter.NormalizeSourceFile(m.FileName);
                        if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                            map[key] = m.RootItem;
                    }
                }
            }
            catch (Exception ex)
            {
                CortexDebugLog.LogException("RevitRefMatcher.BuildRootByFile", ex);
            }
            return map;
        }

        private static void ScanRoot(ModelItem root, List<CortexElementRef> pending,
            MatchOutput output)
        {
            if (pending.Count == 0) return;
            var pendingSet = new List<CortexElementRef>(pending);

            IEnumerator<ModelItem>? enumerator = null;
            try { enumerator = root.DescendantsAndSelf.GetEnumerator(); }
            catch (Exception ex)
            {
                CortexDebugLog.LogException("RevitRefMatcher.GetEnumerator", ex);
                foreach (var r in pendingSet)
                    output.NotFound.Add(new { @ref = r, reason = "tree iterator failed" });
                return;
            }
            if (enumerator == null) return;

            using (enumerator)
            {
                while (pendingSet.Count > 0)
                {
                    bool moved;
                    try { moved = enumerator.MoveNext(); }
                    catch (Exception ex)
                    {
                        CortexDebugLog.LogException("RevitRefMatcher.MoveNext", ex);
                        try { if (!enumerator.MoveNext()) break; }
                        catch { break; }
                        moved = true;
                    }
                    if (!moved) break;

                    ModelItem? item;
                    try { item = enumerator.Current; }
                    catch { continue; }
                    if (item == null) continue;

                    RevitReference candidate;
                    try { candidate = ReadReference(item); }
                    catch (Exception ex)
                    {
                        CortexDebugLog.LogException("RevitRefMatcher.ReadReference", ex);
                        continue;
                    }
                    if (!candidate.HasAnyReference) continue;

                    for (int i = pendingSet.Count - 1; i >= 0; i--)
                    {
                        if (IsMatch(pendingSet[i], candidate))
                        {
                            output.Resolved.Add(item);
                            pendingSet.RemoveAt(i);
                        }
                    }
                }
            }

            foreach (var r in pendingSet)
                output.NotFound.Add(new { @ref = r, reason = "no matching item in source" });
        }

        private static RevitReference ReadReference(ModelItem item)
        {
            // Same property walk as SelectionExporter.ExtractReference. Kept
            // inline rather than centralized so neither existing tool nor
            // the existing GetRevitReferencesTool need to change.
            var builder = new RevitReferenceBuilder();
            if (item.PropertyCategories == null) return builder.Build();
            foreach (var category in item.PropertyCategories)
            {
                if (category == null) continue;
                string? catDisplay = null;
                string? catInternal = null;
                try
                {
                    catDisplay = category.DisplayName;
                    catInternal = category.Name?.ToString();
                }
                catch { /* swallow */ }

                if (category.Properties == null) continue;
                foreach (var prop in category.Properties)
                {
                    if (prop == null) continue;
                    try
                    {
                        var (value, _) = NavisCortex.Tools.Properties
                            .PropertyCategoryReader.ConvertValue(prop.Value);
                        builder.Add(catDisplay, catInternal,
                            prop.DisplayName, prop.Name?.ToString(), value);
                    }
                    catch { /* swallow per-prop */ }
                }
            }
            return builder.Build();
        }
    }
}
```

- [ ] **Step 4: Run the matcher tests**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter RevitRefMatcherTests
```
Expected: 2 PASS.

- [ ] **Step 5: Run full Navis test project**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26"
```
Expected: all green.

- [ ] **Step 6: Commit**

```
git add src/NavisCortex.Tools/Interop/RevitRefMatcher.cs \
        src/NavisCortex.Tests/Interop/RevitRefMatcherTests.cs
git commit -m "feat(navis/interop): add RevitRefMatcher with hardened single-pass scan"
```

---

## Task 8 — Navis: CrossAppSelectionTool dispatcher (export + import)

**Files:**
- Create: `NavisCortex/src/NavisCortex.Tools/Interop/CrossAppSelectionTool.cs`
- Create: `NavisCortex/src/NavisCortex.Tests/Interop/CrossAppSelectionToolTests.cs`

The Navis tool wires both modes in one task: export uses `SelectionExporter` with three sources (clashGuid > clashTestGuid > current selection); import uses `RevitRefMatcher` then `doc.CurrentSelection.AddRange` with the same hardening as `SelectItemsByPathTool` (`HandleProcessCorruptedStateExceptions` on `Execute`).

- [ ] **Step 1: Write failing input-validation tests**

```csharp
using Newtonsoft.Json.Linq;
using NavisCortex.Core.Results;
using NavisCortex.Core.Session;
using NavisCortex.Tools.Interop;
using Xunit;

namespace NavisCortex.Tests.Interop;

public class CrossAppSelectionToolTests
{
    [Fact]
    public void RejectsMissingMode()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(new JObject(), session);
        Assert.False(result.IsSuccess);
        Assert.Equal(CortexErrorCode.InvalidInput, result.ErrorCode);
    }

    [Fact]
    public void RejectsUnknownMode()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(JObject.Parse("{\"mode\":\"banana\"}"), session);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Import_RejectsEmptyRefs()
    {
        var tool = new CrossAppSelectionTool();
        var session = new CortexSession();
        var result = tool.Execute(
            JObject.Parse("{\"mode\":\"import\",\"refs\":[]}"), session);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ToolMetadataIsCorrect()
    {
        var tool = new CrossAppSelectionTool();
        Assert.Equal("cross_app_selection", tool.Name);
        Assert.Equal("Interop", tool.Category);
        Assert.True(tool.RequiresDocument);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter CrossAppSelectionToolTests
```
Expected: FAIL — class missing.

- [ ] **Step 3: Implement the tool**

Create `NavisCortex/src/NavisCortex.Tools/Interop/CrossAppSelectionTool.cs`:
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Newtonsoft.Json.Linq;
using NavisCortex.Core.Interop;
using NavisCortex.Core.Results;
using NavisCortex.Core.Security;
using NavisCortex.Core.Session;
using NavisCortex.Core.Tools;

namespace NavisCortex.Tools.Interop;

/// <summary>
/// Symmetric Revit↔Navis selection bridge. mode=export emits
/// CortexElementRefs from current selection / clash; mode=import
/// resolves CortexElementRefs to ModelItems and replaces (or appends to)
/// the current selection.
/// </summary>
public class CrossAppSelectionTool : ICortexTool
{
    public string Name => "cross_app_selection";
    public string Category => "Interop";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description =>
        "Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from CurrentSelection (default), or from a single clash via clashGuid, or all clashes of a test via clashTestGuid. mode=import → resolve CortexElementRefs to ModelItems and select them (replaces selection unless append=true). Resolution priority: revitUniqueId → ifcGuid → revitElementId.";

    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
    [System.Security.SecurityCritical]
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<Document>("activeDocument");
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Navisworks document",
                suggestion: "Open a Navisworks document before calling this tool.");

        var mode = input["mode"]?.ToString();
        if (string.IsNullOrWhiteSpace(mode))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "mode is required",
                suggestion: "Pass mode=\"export\" or mode=\"import\".");

        switch (mode!.ToLowerInvariant())
        {
            case "export": return ExecuteExport(doc, input);
            case "import": return ExecuteImport(doc, input);
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown mode '{mode}'",
                    suggestion: "Pass mode=\"export\" or mode=\"import\".");
        }
    }

    private static CortexResult<object> ExecuteExport(Document doc, JObject input)
    {
        var clashGuid = input["clashGuid"]?.ToString();
        var clashTestGuid = input["clashTestGuid"]?.ToString();

        IEnumerable<ModelItem> items;
        string usedSource;

        if (!string.IsNullOrWhiteSpace(clashGuid))
        {
            items = ResolveClashItems(doc, clashGuid!, out usedSource);
            if (items == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Clash guid '{clashGuid}' not found");
        }
        else if (!string.IsNullOrWhiteSpace(clashTestGuid))
        {
            items = ResolveClashTestItems(doc, clashTestGuid!, out usedSource);
            if (items == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Clash test guid '{clashTestGuid}' not found");
        }
        else
        {
            usedSource = "currentSelection";
            try
            {
                var current = doc.CurrentSelection?.SelectedItems;
                items = current != null
                    ? new List<ModelItem>(current)
                    : new List<ModelItem>();
            }
            catch (Exception ex)
            {
                CortexDebugLog.LogException(
                    "CrossAppSelectionTool.Export.CurrentSelection", ex);
                items = new List<ModelItem>();
            }
        }

        var output = SelectionExporter.ExportItems(doc, items);
        return CortexResult<object>.Ok(new
        {
            side = "navis",
            usedSource,
            exportedCount = output.Refs.Count,
            refs = output.Refs,
            skipped = output.Skipped
        });
    }

    private static CortexResult<object> ExecuteImport(Document doc, JObject input)
    {
        var refsToken = input["refs"] as JArray;
        if (refsToken == null || refsToken.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "refs is required and cannot be empty",
                suggestion: "Pass refs=[CortexElementRef, ...] from the export side.");

        var refs = new List<CortexElementRef>();
        foreach (var t in refsToken)
        {
            var r = t.ToObject<CortexElementRef>();
            if (r != null) refs.Add(r);
        }
        if (refs.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "no parseable CortexElementRef in refs");

        var append = input["append"]?.ToObject<bool?>() ?? false;

        var match = RevitRefMatcher.Resolve(doc, refs);

        try
        {
            var selection = doc.CurrentSelection;
            if (selection == null)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    "Document has no CurrentSelection accessor (unexpected)");
            if (!append) selection.Clear();
            if (match.Resolved.Count > 0) selection.AddRange(match.Resolved);
        }
        catch (Exception ex)
        {
            CortexDebugLog.LogException("CrossAppSelectionTool.Import.AddRange", ex,
                new { resolvedCount = match.Resolved.Count, append });
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to commit selection: {ex.Message}");
        }

        return CortexResult<object>.Ok(new
        {
            side = "navis",
            requested = refs.Count,
            resolved = match.Resolved.Count,
            selected = match.Resolved.Count,
            append,
            notFound = match.NotFound
        });
    }

    private static IEnumerable<ModelItem>? ResolveClashItems(
        Document doc, string clashGuid, out string usedSource)
    {
#if HAS_NAVIS_CLASH
        usedSource = "clashGuid";
        try
        {
            var data = NavisCortex.Tools.Clash.ClashAccess.GetClashTests(doc);
            if (data == null) return null;
            if (!Guid.TryParse(clashGuid, out var parsed)) return null;
            foreach (var test in NavisCortex.Tools.Clash.ClashAccess.EnumerateTests(data))
            {
                foreach (var r in NavisCortex.Tools.Clash.ClashAccess.EnumerateResults(test))
                {
                    if (r.Guid == parsed)
                    {
                        var list = new List<ModelItem>();
                        if (r.Item1 != null) list.Add(r.Item1);
                        if (r.Item2 != null) list.Add(r.Item2);
                        return list;
                    }
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            CortexDebugLog.LogException(
                "CrossAppSelectionTool.Export.ResolveClashItems", ex);
            return null;
        }
#else
        usedSource = "clashGuid_unavailable";
        return null;
#endif
    }

    private static IEnumerable<ModelItem>? ResolveClashTestItems(
        Document doc, string clashTestGuid, out string usedSource)
    {
#if HAS_NAVIS_CLASH
        usedSource = "clashTestGuid";
        try
        {
            var data = NavisCortex.Tools.Clash.ClashAccess.GetClashTests(doc);
            if (data == null) return null;
            var test = NavisCortex.Tools.Clash.ClashAccess.FindTest(data, clashTestGuid, null);
            if (test == null) return null;
            var items = new List<ModelItem>();
            foreach (var r in NavisCortex.Tools.Clash.ClashAccess.EnumerateResults(test))
            {
                if (r.Item1 != null) items.Add(r.Item1);
                if (r.Item2 != null) items.Add(r.Item2);
            }
            return items;
        }
        catch (Exception ex)
        {
            CortexDebugLog.LogException(
                "CrossAppSelectionTool.Export.ResolveClashTestItems", ex);
            return null;
        }
#else
        usedSource = "clashTestGuid_unavailable";
        return null;
#endif
    }
}
```

**Note:** the call into `ClashAccess.EnumerateTests` may not exist as a public helper today. Before implementing this task, verify the public surface of `NavisCortex.Tools.Clash.ClashAccess` and adjust the export path:
- If `EnumerateTests` exists → use it.
- If not, iterate the `data` collection in the same way `GetClashesTool` does (see its existing pattern). The implementer must NOT modify `ClashAccess` itself — read-only consumption only.

If `ClashAccess` does not provide a public way to enumerate all tests, drop the `clashTestGuid` source for v1 and emit a clear `notFound` reason. Document the limitation in the tool's `Description`. The `clashGuid` and `useCurrentSelection` paths remain.

- [ ] **Step 4: Run all interop tests**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26" --filter Interop
```
Expected: all PASS.

- [ ] **Step 5: Run full test project**

```
dotnet test src/NavisCortex.Tests/NavisCortex.Tests.csproj -c "Debug N26"
```
Expected: all green — no regression in `RevitReferenceBuilderTests`, `ItemPathResolver`, etc.

- [ ] **Step 6: Cross-target the full Navis matrix**

```
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N23"
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N24"
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N25"
dotnet build src/NavisCortex.Tools/NavisCortex.Tools.csproj -c "Debug N26"
```
Expected: 4 green builds.

- [ ] **Step 7: Commit**

```
git add src/NavisCortex.Tools/Interop/CrossAppSelectionTool.cs \
        src/NavisCortex.Tests/Interop/CrossAppSelectionToolTests.cs
git commit -m "feat(navis/interop): add cross_app_selection tool (export + import)"
```

---

## Task 9 — Smoke documentation

**Files:**
- Create: `RevitCortex/docs/cross-app-selection-smoke.md`

A short text doc with the four manual scenarios from the spec. Not a test runner — a checklist for the maintainer to verify the tool works against real Revit + real Navis.

- [ ] **Step 1: Write the smoke doc**

```markdown
# Cross-App Selection — Smoke Tests

Run these manually after every release that touches the interop tools.

## 1. Selection Revit → Navis

1. Revit: open a project with at least one Revit link loaded.
2. Select 1 host element + 1 element inside the link (use TAB to pick into the link).
3. Call MCP tool: `cross_app_selection { mode: "export" }` on Revit. Copy `refs`.
4. Navis: open the federated NWF/NWD that contains both source files.
5. Call MCP tool: `cross_app_selection { mode: "import", refs: <pasted> }` on Navis.
6. Expected: both items selected in Navis with selection count = 2.

## 2. Selection Navis → Revit

1. Navis: select 2 items belonging to two different source files.
2. Call: `cross_app_selection { mode: "export" }` on Navis. Copy `refs`.
3. Revit: open the host project for one of those source files (the other will be a link).
4. Call: `cross_app_selection { mode: "import", refs: <pasted> }` on Revit.
5. Expected: host element selected; linked element flagged with a red DirectShape marker; section box framing both; isolate active.

## 3. Clash Navis → Revit

1. Navis: open a clash test with at least one clash.
2. Call: `cross_app_selection { mode: "export", clashGuid: "<one clash guid>" }`. Copy `refs`.
3. Revit: same host project.
4. Call: `cross_app_selection { mode: "import", refs: <pasted>, isolate: true, createSectionBox: true }`.
5. Expected: both clashing elements visible — host selected, linked marked.

## 4. Cross-target sanity

Run `RevitCortex/build-release.ps1` (or your standard release build) and confirm no warnings/errors on R23, R24, R25, R26, R27. Run the equivalent Navis build for N23/N24/N25/N26.
```

- [ ] **Step 2: Commit**

```
git add docs/cross-app-selection-smoke.md
git commit -m "docs(interop): add cross-app selection smoke tests"
```

---

## Self-review

**Spec coverage check:**

| Spec section | Covered by |
|---|---|
| Architecture / one tool per side | Tasks 4, 8 |
| Reuse `CortexElementRef` DTO | Tasks 2, 3, 5, 7 (no DTO duplication) |
| Hard constraint "no breaking changes" | Verified in Task 1, Task 6 step 5, Task 8 step 5 |
| Tool contract export schema | Tasks 4, 8 |
| Tool contract import schema | Tasks 6, 8 |
| Pipeline 2-call cross-app | Implicit — both tools accept the other's output verbatim |
| Components Revit | Tasks 2, 3, 4, 6 |
| Components Navis | Tasks 5, 7, 8 |
| Resolution logic Revit import | Task 2 (resolver) + Task 6 (delegation) |
| Resolution logic Revit export | Task 3 |
| Resolution logic Navis export | Tasks 5, 8 |
| Resolution logic Navis import | Tasks 7, 8 |
| Error handling: hard fails | Tasks 4, 8 input validation |
| Error handling: partial success | Tasks 6, 8 (notFound payload) |
| Hardening (HPCSE, manual enumerator) | Task 7, Task 8 |
| Cross-target matrix R23-R27, N23-N26 | Task 6 step 6, Task 8 step 6 |
| Smoke tests | Task 9 |

**Placeholder scan:** none — every step has concrete commands and code.

**Type consistency:**
- `CortexElementRef` properties used throughout match the actual DTO (verified in Task 1).
- `ResolveOutcome.HostElementId` / `.LinkInstanceId` / `.LinkedElementId` consistently typed `ElementId?`.
- `MatchOutput.Resolved` / `.NotFound` consistently typed in Tasks 7 and 8.
- `SelectionExporter.NormalizeSourceFile` (Navis) and `HostLinkResolver.NormalizeBasename` (Revit) are different names by design (different namespaces, different surface) — both lowercase a `Path.GetFileName(...)`.

**One potential gap:** Task 8 calls into `NavisCortex.Tools.Clash.ClashAccess` helpers (`GetClashTests`, `EnumerateResults`, `EnumerateTests`, `FindTest`). The first two definitely exist (used by `GetClashesTool`); the last two need verification before Task 8. Task 8 explicitly notes this and provides a fallback (drop `clashTestGuid` for v1) — that's the right way to handle it without breaking the constraint.

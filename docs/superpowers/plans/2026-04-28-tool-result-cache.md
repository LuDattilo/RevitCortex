# Tool Result Cache — Implementation Plan

**Date:** 2026-04-28
**Author:** Claude (Sonnet 4.5)
**Status:** Draft, ready for review

## Goal

Reduce latency of read-only RevitCortex MCP tools by caching results keyed on
`(toolName, paramHash)`, with automatic invalidation tied to Revit's
`Application.DocumentChanged` event. Same input → same output, until the
document changes.

**Non-goals:**
- Reducing Anthropic token cost (handled by Claude API prompt cache).
- Persistent / cross-session cache.
- Caching for write tools.
- Caching across the IPC boundary in `RevitCortex.Server.exe` (cache lives in
  Plugin only, since invalidation events fire there).

## Why now

- Multiple MCP calls in a workflow re-issue identical queries (`get_phases`,
  `get_worksets`, `get_project_info` are common warm-ups).
- `analyze_model_statistics`, `get_warnings`, `get_linked_elements` are
  expensive on large models and re-running them per call is wasteful.
- Existing `SessionStore` already proves the pattern (cross-call state for
  `lastFilterResults`); we're generalizing that idea with proper invalidation.

## Existing building blocks (reuse, don't replace)

| File | What it does | How we'll use it |
|---|---|---|
| `RevitCortex.Core/Session/SessionStore.cs` | `ConcurrentDictionary<string, object>` keyed by string | Keep as-is; cache lives alongside, doesn't replace it |
| `RevitCortex.Core/Session/CortexSession.cs` | Holds `Store`, `Capabilities`, etc. | Add `Cache` property |
| `RevitCortex.Core/Tools/ICortexTool.cs` | Tool contract | Add optional `CacheScope? Cache => null;` |
| `RevitCortex.Plugin/CortexRouter.cs` | Dispatcher; line 102-107 calls `tool.Execute` | Wrap with cache lookup |
| `RevitCortex.Plugin/RevitCortexApp.cs` | Plugin lifecycle, has `OnStartup` | Subscribe to `DocumentChanged` here |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ RevitCortex.Server.exe (out-of-process)                     │
│   No cache here — pass through to Plugin via IPC            │
└────────────────────────────┬────────────────────────────────┘
                             │ JSON-RPC over named pipe
                             ▼
┌─────────────────────────────────────────────────────────────┐
│ RevitCortex.Plugin (in-process inside Revit)                │
│                                                             │
│  CortexRouter.Dispatch(toolName, input)                     │
│    │                                                        │
│    ├─ tool.Cache != null && IsReadOnly?                     │
│    │     ↓                                                  │
│    │   Yes → ToolResultCache.GetOrCompute(...)              │
│    │     ├─ Hit  → return cached                            │
│    │     └─ Miss → tool.Execute(...) → store → return       │
│    │                                                        │
│    └─ No → tool.Execute(...) directly                       │
│                                                             │
│  DocumentChangeWatcher (subscribed at startup)              │
│    Application.DocumentChanged →                            │
│      cache.InvalidateScope(CacheScope.Document)             │
│      cache.InvalidateScope(CacheScope.Transaction)          │
│                                                             │
│  Application.DocumentSaved/Synchronized →                   │
│      cache.InvalidateScope(CacheScope.Transaction)          │
└─────────────────────────────────────────────────────────────┘
```

## Cache scopes

```csharp
public enum CacheScope
{
    /// Immutable for the lifetime of the Revit session.
    /// Examples: get_project_info (project name, address), list_schedulable_fields.
    Session,

    /// Invalidated by any DocumentChanged event.
    /// Examples: get_phases, get_worksets, get_warnings, get_materials,
    ///           analyze_model_statistics, get_linked_file_instances.
    Document,

    /// Invalidated by DocumentChanged AND DocumentSaved/Synchronized.
    /// Use when external sync may bring new state (e.g. workshared models).
    /// Examples: get_warnings if we want freshness post-sync.
    Transaction,
}
```

Default for `ICortexTool.Cache` is `null` → no caching. Opt-in only.

## Data model

```csharp
public sealed class CachedEntry
{
    public required string ToolName { get; init; }
    public required string ParamHash { get; init; }
    public required CacheScope Scope { get; init; }
    public required CortexResult<object> Result { get; init; }
    public required long DocumentVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public int HitCount { get; set; }
}

public interface IToolResultCache
{
    bool TryGet(string toolName, string paramHash, CacheScope scope,
                long currentDocVersion, out CortexResult<object> result);
    void Set(string toolName, string paramHash, CacheScope scope,
             long currentDocVersion, CortexResult<object> result);
    void InvalidateScope(CacheScope scope);
    void InvalidateAll();
    CacheStats GetStats();
}

public sealed class CacheStats
{
    public int EntryCount { get; init; }
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public long EstimatedBytes { get; init; }
    public Dictionary<string, ToolStat> PerTool { get; init; } = new();
}
```

`ParamHash`: SHA-256 of canonical-form input JObject (sorted keys, no
whitespace). Reuse `JObject.ToString(Formatting.None)` then hash.

`DocumentVersion`: monotonic counter incremented on each `DocumentChanged`.
Stored on `CortexSession`. Per-document if multi-doc support is needed
(future).

## Files to create

```
src/RevitCortex.Core/Caching/
  CacheScope.cs              (enum + 3 values)
  CachedEntry.cs             (record-like class for net48 compat)
  IToolResultCache.cs        (interface)
  ToolResultCache.cs         (implementation, ConcurrentDictionary)
  CacheStats.cs              (telemetry record)

src/RevitCortex.Plugin/Caching/
  DocumentChangeWatcher.cs   (subscribes to Revit events, drives invalidation)

src/RevitCortex.Tests/Caching/
  ToolResultCacheTests.cs    (unit tests for cache logic)
  DocumentChangeWatcherTests.cs (event handling, may need fakes)
```

## Files to modify

```
src/RevitCortex.Core/Tools/ICortexTool.cs
  + CacheScope? Cache => null;

src/RevitCortex.Core/Session/CortexSession.cs
  + IToolResultCache Cache { get; }
  + long DocumentVersion { get; internal set; }

src/RevitCortex.Plugin/CortexRouter.cs
  Dispatch(): wrap tool.Execute with cache lookup when tool.Cache != null

src/RevitCortex.Plugin/RevitCortexApp.cs
  OnStartup: instantiate cache + DocumentChangeWatcher, wire events
  OnShutdown: unsubscribe events

3-6 cache-eligible tools per Phase 1:
src/RevitCortex.Tools/Project/GetProjectInfoTool.cs       → CacheScope.Session
src/RevitCortex.Tools/Project/GetPhasesTool.cs            → CacheScope.Session
src/RevitCortex.Tools/Project/GetWorksetsTool.cs          → CacheScope.Session
src/RevitCortex.Tools/LinkedFiles/GetLinkedFileInstancesTool.cs → CacheScope.Document
```

## Implementation steps (TDD)

### Phase 0 — Scaffolding (no behavior change)

1. **Create `CacheScope.cs`** in `RevitCortex.Core/Caching/`. Three values.
   No tests needed.
2. **Create `IToolResultCache.cs`** interface. No tests needed.
3. **Add `CacheScope? Cache => null;` to `ICortexTool`.** Default
   implementation, all existing tools unaffected.
4. **Build R24/R25/R26.** Verify zero changes to existing behavior.

### Phase 1 — Cache implementation (TDD)

5. **Write `ToolResultCacheTests.cs`** first. Cases:
   - `TryGet` on empty cache → false
   - `Set` then `TryGet` same key → true with same value
   - `TryGet` with stale DocumentVersion → false
   - `InvalidateScope(Document)` removes Document entries, keeps Session
   - `InvalidateAll` clears everything
   - Concurrent Set+Get is safe (basic stress test)
   - `GetStats` returns non-zero counts after activity
6. **Implement `ToolResultCache.cs`**. `ConcurrentDictionary<string, CachedEntry>`
   keyed `"{toolName}|{paramHash}"`. Make tests pass.
7. **Add `Cache` and `DocumentVersion` to `CortexSession`.** Constructor
   wires defaults.

### Phase 2 — Router integration

8. **Write `CortexRouterCacheTests.cs`**. Cases:
   - Tool with `Cache = null` always calls Execute
   - Tool with `Cache = Session` called twice with same input → Execute
     called once
   - Tool with `Cache = Document` after DocumentVersion bump → Execute
     called again
   - Failed result (`CortexResult.Fail`) is NOT cached
9. **Modify `CortexRouter.Dispatch`** to consult cache. Make tests pass.
   Key code path:
   ```csharp
   if (tool.Cache.HasValue && IsReadOnly(tool))
   {
       var hash = HashParams(input);
       if (_session.Cache.TryGet(tool.Name, hash, tool.Cache.Value,
                                  _session.DocumentVersion, out var cached))
           return cached;
       var fresh = tool.Execute(input, _session);
       if (fresh.IsSuccess)
           _session.Cache.Set(tool.Name, hash, tool.Cache.Value,
                              _session.DocumentVersion, fresh);
       return fresh;
   }
   ```

### Phase 3 — Document event wiring

10. **Write `DocumentChangeWatcherTests.cs`** with fake `IToolResultCache`.
    Cases:
    - `OnDocumentChanged` invokes `InvalidateScope(Document)` and
      `InvalidateScope(Transaction)`
    - `OnDocumentSaved` invokes `InvalidateScope(Transaction)` only
    - Disposing watcher unsubscribes
11. **Implement `DocumentChangeWatcher.cs`** in `RevitCortex.Plugin/Caching/`.
    Subscribes to `Application.DocumentChanged`,
    `Application.DocumentSaved`, `Application.DocumentSynchronizedWithCentral`.
    On any event: `_session.DocumentVersion++` and call invalidation.
12. **Modify `RevitCortexApp.OnStartup`** to instantiate watcher.
    `OnShutdown` disposes it.

### Phase 4 — Opt-in tools

13. **Add `CacheScope` overrides** to 3-6 tools (one per scope category):
    - `GetProjectInfoTool.Cache = CacheScope.Session`
    - `GetPhasesTool.Cache = CacheScope.Session`
    - `GetWorksetsTool.Cache = CacheScope.Session`
    - `GetLinkedFileInstancesTool.Cache = CacheScope.Document`
    - `GetMaterialsTool.Cache = CacheScope.Document`
14. **Manual smoke test in Revit**: call each cached tool twice, verify
    second call returns instantly (microsecond-level vs. millisecond).
    Then trigger a model change, verify cache invalidates.

### Phase 5 — Diagnostics

15. **Add `get_cache_stats` tool** (read-only, never cached itself):
    returns `CacheStats` JSON. Useful for debugging hit rate and seeing
    which tools benefit most. Place in
    `src/RevitCortex.Tools/Meta/GetCacheStatsTool.cs`.
16. **Add `clear_cache` tool** (admin, manual flush). Returns count of
    entries cleared.

### Phase 6 — Build, test, ship

17. **Build all targets**: Debug R24, R25, R26. Zero errors, zero warnings.
18. **Run all tests**: `dotnet test` in `RevitCortex.Tests`. All green.
19. **Deploy to user-scope AND machine-scope** (both
    `AppData\Roaming\Autodesk\Revit\Addins\<ver>\RevitCortex\` and
    `ProgramData\Autodesk\Revit\Addins\<ver>\RevitCortex\`). See
    `deploy-r25.bat` for pattern.
20. **Live verification in Revit 2025**: open Snowdon Towers sample, call
    `get_phases` 3x → expect 1 miss + 2 hits. Modify any element, call
    again → expect new miss. Use `get_cache_stats` to confirm.

## Open questions

These are deliberately left for the implementation session, not pre-decided
here:

1. **Memory cap?** No cap initially. Add if `analyze_model_statistics`
   results bloat memory on huge models. LRU eviction is a follow-up.
2. **Multi-document Revit?** Initial impl: single active document. If user
   switches doc, version counter is per-session, so we'd risk stale hits
   from another doc. Mitigation: include `Document.PathName` in cache key,
   OR clear cache on `Application.DocumentChanged` when `args.GetDocument()`
   differs from session's active doc. Defer to Phase 2 review.
3. **Concurrent calls?** Plugin is single-threaded by Revit API contract,
   but cache should still use `ConcurrentDictionary` for paranoia (stats
   reads can come from a tool while a write happens).
4. **Should cache survive `Document.Save`?** `CacheScope.Document` says yes,
   `CacheScope.Transaction` says no. Per-tool decision. Default
   `Document` because Save doesn't change model state, only persists it.
5. **Net48 compat?** All caching code lives in `RevitCortex.Core`
   (netstandard2.0). Avoid `record`, `init`, `Index`/`Range`, default
   interface members. Use classes with constructor + readonly properties.

## Risk / what could go wrong

| Risk | Mitigation |
|---|---|
| Cache returns stale data after model change | Tests for `DocumentChanged` invalidation; manual verify in Phase 4. Default to `Document` scope (safe) over `Session` when in doubt. |
| Memory grows unbounded | Per-tool max entries (e.g. 50) is trivial to add later. Initial expectation: <10MB across all tools. |
| Cache key collisions | SHA-256 of canonical JSON. Probability of collision ~0. |
| Failure cached and "stuck" | Only success results are cached (Phase 2 step 8 test). Failures always re-execute. |
| Multi-doc switch returns wrong-doc cache | Open question #2; default to clearing on doc switch. |
| net48 build break | Use only language features available in C# 7.3 / netstandard2.0 in `RevitCortex.Core`. Check by building Debug R24 in CI. |

## Estimated effort

- Phase 0: 30 min (scaffolding)
- Phase 1: 90 min (cache + tests, TDD)
- Phase 2: 60 min (router integration + tests)
- Phase 3: 60 min (event wiring + tests)
- Phase 4: 30 min (opt-in 5 tools)
- Phase 5: 45 min (diagnostics tools)
- Phase 6: 60 min (build, deploy, live verify)

**Total: ~6 hours focused work** in a clean session.

## Out of scope (future work, separate PR)

- LRU eviction with size cap
- Per-tool entry count limits
- Cache warming on startup (pre-fetch common reads)
- Distributed cache for multi-user scenarios
- Persistent cache (disk-backed for cold-start speed)
- Caching at the `Server.exe` IPC layer (would duplicate Plugin cache; only
  useful if Plugin↔Server hop becomes the bottleneck, which it isn't today)
- Adding cache scope to all 100+ existing tools (do it incrementally as
  bottlenecks are identified via `get_cache_stats`)

## Acceptance criteria

- [ ] All Phase 0-6 steps complete
- [ ] Build green for R24/R25/R26
- [ ] All unit tests green (existing + new)
- [ ] Manual live test in Revit 2025 confirms hit/miss behavior matches
      design
- [ ] `get_cache_stats` shows non-zero hits after a typical workflow
      (e.g., `workflow_morning_coordination` or `analyze_model_statistics`
      twice in a row)
- [ ] Zero regressions in existing tools (regression smoke test of 5 random
      tools)
- [ ] Memory updated with caching pattern + how to opt-in new tools

# Power BI Table Management — Design Specification

**Author:** RevitCortex team
**Date:** 2026-05-11
**Status:** Draft for review
**Scope:** Defines how RevitCortex publishes, replaces, and deduplicates data in Power BI across **all** export paths, independent of which Revit feature triggers the push.

---

## 1. Purpose

We have several Revit → Power BI publishing entry points (`pbi_publish_elements`, `pbi_publish_schedules`, `pbi_publish_selection`, `push_to_powerbi`, `push_table_to_powerbi`). Each was designed independently. The result today:

- Some paths produce duplicates when re-run (`mode: "append"`).
- Some paths silently delete data that the user did *not* intend to overwrite (`mode: "replace"` on a single schedule wipes all schedules in the table).
- There is **no shared contract** for what "publish this data" means semantically.
- The Revit user — who thinks in terms of *Walls schedule*, *current selection*, *Doors family* — has no mental model that maps onto what actually ends up in the Power BI dataset.

This spec defines the **shared contract** so that all push paths behave predictably, no matter where the data comes from in Revit.

---

## 2. Background — Power BI ingestion constraints

The behavior we want is constrained by the Power BI service itself. Key facts (sources at the end of this section):

1. **No primary key, no upsert.** The Power BI Push REST API has only `POST /rows` (append), `DELETE /rows` (truncate the entire table), and `PUT /tables/{name}` (schema change). There is no `MERGE`, no `DELETE WHERE`, no row-level identity.
2. **DELETE is all-or-nothing per table.** You cannot delete "all rows where `ScheduleId = X`". You can only wipe the whole table.
3. **`POST /rows` is append-only.** Two posts of the same row produce two physical rows. The service does **not** check for duplicates.
4. **The Push family is on a deprecation path.** Microsoft announced that new real-time semantic models (push, streaming, PubNub) cannot be created after **2027-10-31**; existing ones will keep working. Reference: [Real-time streaming in Power BI](https://learn.microsoft.com/en-us/power-bi/connect-data/service-real-time-streaming).
5. **The only first-party "atomic replace" verb is `POST /imports?nameConflict=Overwrite`** — but it overwrites the entire `.pbix` model file, not individual tables, and requires us to generate a Tabular model client-side. Not feasible from a Revit add-in.
6. **OneDrive/SharePoint scheduled refresh** *can* approximate snapshot semantics: each logical entity becomes one file; overwriting the file is the "upsert". Latency is bounded by Power BI's hourly OneDrive sync. Reference: [Data refresh in Power BI](https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data).
7. **Limits** (Push):
   - 75 tables per dataset, 75 columns per table.
   - 10 000 rows per `POST /rows`, 1 000 000 rows/hour per model, 120 requests/min.
   - 16 MB per request body.
   - 5 M rows lifetime per table (FIFO) or 200 K with `defaultRetentionPolicy=basicFIFO`.
   Reference: [Push semantic model limitations](https://learn.microsoft.com/en-us/power-bi/developer/embedded/push-datasets-limitations).

The consequence for our design: **we cannot delegate identity to the service**. RevitCortex must own the rules for "what counts as a duplicate" and "what gets replaced".

---

## 3. Current state — what RevitCortex does today

| Path | Output | `mode: replace` does | `mode: append` does | Identity |
|---|---|---|---|---|
| `pbi_publish_elements` | Push API → `Elements` table | DELETE all rows of `Elements`, then POST new | POST new (additive) | None — duplicates possible on `(ElementId, ExportRunId)` |
| `pbi_publish_schedules` | Push API → `Schedules` table | DELETE all rows of `Schedules`, then POST new | POST new (additive) | None — duplicates possible on `(ScheduleId, ElementId, ColumnName)` |
| `pbi_publish_selection` | Push API → `Selection` table | DELETE all rows of `Selection`, then POST new snapshot | — | None |
| `push_to_powerbi` | CSV in OneDrive | Writes/overwrites a file (one per call) | — | File path uniqueness |
| `push_table_to_powerbi` | CSV in OneDrive | Writes/overwrites a file | — | File path uniqueness |

**Problems:**

1. **Granularity mismatch.** The user wants to "update the Doors schedule" but the tool deletes *all* schedules in the dataset because the Push API can only wipe the whole table.
2. **Silent duplicates in `append`.** A user re-running the same publish doubles the data with no warning.
3. **No identity contract.** Two schedules with the same ColumnName in their definition (e.g. both have "Mark") will collide if joined naively in PBI.
4. **No state file.** Once data is in PBI, RevitCortex has no idea what was published. A second user (or the same user from a different machine) cannot reason about what's already there.

---

## 4. Goals

In priority order:

1. **No silent data loss.** A push call must never delete data the caller did not explicitly opt to overwrite.
2. **No silent duplicates.** Re-publishing the same logical entity must produce the same end-state in PBI, regardless of how many times it's called.
3. **Predictable join keys.** Every row in every table must carry the keys needed to reconstruct the entity-level relationships in PBI (`ElementId`, `ScheduleId`, `UniqueId`, etc.).
4. **One contract for all paths.** Push API and CSV-based paths must use the same semantics, so users (and downstream automation) don't need a mental map of which tool behaves how.
5. **Pro-license friendly.** The default path must work for a user with only a Power BI Pro license. Fabric-only solutions (Lakehouse, Direct Lake, MERGE on Delta) are out of scope for the default path.

---

## 5. Non-goals

- Replacing the Push API as the transport. We stay on Push for now; we will plan a migration path before 2027-10-31 in a separate spec.
- Real-time streaming semantics (sub-second latency, KPI tiles). Out of scope.
- Lake-house / Fabric integration. Out of scope for v1.1; can be added later as an optional output target.
- True transactional atomicity (no partial visibility during replace). The Push API cannot provide this; we accept a brief window of empty/inconsistent state during a replace operation.

---

## 6. Design

### 6.1 Logical units and natural keys

We define the following **logical units** and their **natural keys**. These are language-independent, model-independent, and stable across re-publishes.

| Logical unit | Natural key | Table | Notes |
|---|---|---|---|
| Element snapshot | `ElementId` | `Elements` | One row per element in the active document. |
| Schedule cell | `(ScheduleId, ElementId, ColumnName)` | `Schedules` | One row per (schedule, element, schedule field). Group/total rows have no `ElementId` and are not emitted. |
| Selection set | `(SelectionSetId, ElementId)` | `Selection` | `SelectionSetId` is the named selection identifier (or `"__current__"` for ad-hoc). |
| Element parameter | `(ElementId, ParameterName)` | `ElementParameters` | Optional; used when callers want flat key/value pairs. |
| Custom table | `(elementIdColumn, ...)` | filesystem CSV | User-defined via `push_table_to_powerbi`. ElementId column is enforced when possible. |

**Universal rule:** every Push API row carries `_SchemaVersion`, `ExportRunId`, `ExportedAtUtc`. These are not part of the natural key but are required for diagnostics and dedup across runs.

### 6.2 Replacement scope contract

A push call must declare its **replacement scope**. The scope determines what is wiped before posting new data. The valid scopes are:

| Scope | Meaning | Wipe behavior |
|---|---|---|
| `whole-table` | "I'm publishing the entire content of this table; everything else is stale." | DELETE all rows of the target table, then POST new rows. |
| `entity` | "I'm publishing one or more *entities* (e.g. specific ScheduleIds). Leave other entities alone." | Requires a **state file** (see 6.3): wipe table, re-post union(other entities from state, new entity rows). |
| `append` | "I'm adding to whatever is there. I take responsibility for duplicates." | No wipe, POST only. The response carries an explicit warning about duplicate risk. |

Default scope is **`whole-table`** (current default for `replace` mode). This preserves backward compatibility.

`entity` is the new mode and is the safe default for "update one schedule" workflows. It requires state (6.3).

`append` is preserved with its existing semantics. The response always includes a warning when this scope is used, listing the keys of the rows that were appended so the caller can detect duplicates by joining its own output against PBI later.

### 6.3 State file (`~/.revitcortex/pbi_state.json`)

To support `entity` scope, RevitCortex maintains a local state file per (workspace, dataset).

**Schema:**

```jsonc
{
  "version": "1.0",
  "entries": {
    "<workspaceId>/<datasetId>": {
      "<tableName>": {
        "<entityKey>": {
          "exportRunId": "uuid",
          "exportedAtUtc": "2026-05-11T20:00:00Z",
          "rows": [ /* compacted row objects */ ]
        }
      }
    }
  }
}
```

Example: after publishing `Structural Wall Schedule` (ScheduleId 12345), the state file contains an entry under `<workspaceId>/<datasetId>/Schedules/12345` with all the rows for that schedule.

When the user pushes a **different** schedule (ScheduleId 67890) with `scope: "entity"`:
1. Load state.
2. Compute the union of `(other entities' rows from state) + (this push's new rows)`.
3. DELETE the table.
4. POST the union.
5. Update state with the new entity's rows.

**Edge cases:**

- **State file missing or corrupt** → tool reverts to `whole-table` and emits a warning telling the user "no state found; entity scope degraded to whole-table; other entities may be lost".
- **Two machines publishing to the same dataset** → state is per-machine. We don't try to synchronize. The response always tells the caller what scope was effectively applied, so they can notice drift.
- **Dataset deleted externally in PBI** → the next push will recreate it and the state becomes stale. Tool detects "dataset not found" and resets that dataset's state entry.
- **Rows older than 30 days** in state are pruned on the next run to keep the file small (configurable threshold).

**The state file is a cache, not a source of truth.** Power BI remains the source of truth. The state is purely an optimization to compute the "union" required by Push API limitations.

### 6.4 Intra-run deduplication

Within a single push call, RevitCortex deduplicates rows by natural key before posting. This protects against bugs in callers (e.g. the same ElementId listed twice in a user-supplied `scheduleIds` array).

Dedup rules:

- For `Elements` table: dedup by `ElementId`. If duplicates appear in the input, the **last one wins** and a warning is included in the response.
- For `Schedules` table: dedup by `(ScheduleId, ElementId, ColumnName)`. Last one wins.
- For `Selection` table: dedup by `(SelectionSetId, ElementId)`. Last one wins.
- For `push_table_to_powerbi`: dedup is skipped if no `elementIdColumn` is provided. Otherwise dedup by that column.

### 6.5 ElementId enforcement

Every push that represents per-element data **must** include `ElementId` in its output. This was already established in schema v1.1 (separate spec, already implemented):

- `Elements` table → always has `ElementId` (existing behavior).
- `Schedules` table → has `ElementId` from v1.1.
- `Selection` table → always has `ElementId`.
- `push_to_powerbi` CSV → always has `ElementId` as the first column. In `columnMappings` mode, RevitCortex prepends `ElementId` automatically if the user did not include it.
- `push_table_to_powerbi` → checks for an `ElementId` column (or whatever the caller declared via `elementIdColumn`). If absent, the row is written anyway but the response contains a `warning` and `hasElementId: false` in the metadata sidecar.

### 6.6 Response contract

Every push tool returns:

```jsonc
{
  "success": true,
  "requestedScope": "entity",                          // what the caller asked for
  "actualScope":    "whole-table",                     // what the tool actually applied (may differ on degraded path)
  "tableName":      "Schedules",
  "rowsPosted":     1234,
  "rowsDeduped":    7,
  "rowsReplaced":   0,
  "rowsAppended":   0,
  "entityKeys":     ["12345", "67890"],
  "warnings":       [ "..." ],                         // empty array if none
  "stateFile":      "C:\\Users\\...\\pbi_state.json",
  "elapsedMs":      1234
}
```

The caller can always reason about exactly what happened: how many rows went in, how many were dedup'd intra-run, and whether the scope was downgraded (e.g. `entity` requested but `whole-table` applied because no state file existed).

### 6.7 Backward compatibility

- Existing callers passing `mode: "replace"` get **`scope: "whole-table"`** automatically (no behavior change).
- Existing callers passing `mode: "append"` get **`scope: "append"`** with the new warning. No behavior change beyond the warning.
- New callers can opt into `scope: "entity"` explicitly. This is the recommended default for `pbi_publish_schedules` once the state machinery is in place.
- The `mode` parameter is deprecated in favor of `scope` but kept working for one release cycle.

---

## 7. Implementation plan (informational; full plan in a separate document)

1. Build the state file abstraction (`PowerBiStateStore`) — local-machine JSON, read/write/prune.
2. Refactor `PowerBiServiceClient` to expose `ReplaceTableAsync(tableName, rows, scope, state)` instead of letting tools call `DeleteRowsAsync` directly.
3. Implement intra-run dedup in `PowerBiServiceClient.ReplaceTableAsync`.
4. Update all 5 push tools to call the new abstraction and accept the new `scope` parameter.
5. Add the response contract fields to every tool's output.
6. Add a feature-flag `enableEntityScope` in settings; off by default until tested.
7. Update USER_GUIDE.md and add a worked example for each scope.
8. Write tests for state file roundtrip, dedup, scope degradation.

---

## 8. Future work (out of scope for this spec)

- Migration path away from Push REST before 2027-10-31. Likely target: OneDrive CSV-per-entity with scheduled refresh. Will require its own spec.
- Optional Fabric Lakehouse output target for customers with capacity. Adds `MERGE` semantics natively.
- Multi-user state synchronization (if multiple Revit users publish to the same dataset). Today every machine has its own state cache; merging would require a shared store.

---

## 9. Open questions for review

These need explicit answers before this spec is locked:

1. **Q1.** Is `entity` scope the right default for `pbi_publish_schedules`, or should we keep `whole-table` as default to avoid surprising users with state-file behavior they didn't ask for?
2. **Q2.** What happens if the state file shows rows for a `ScheduleId` that no longer exists in the document (user deleted the schedule)? Drop those rows from the next replay, or keep them as "ghost history"?
3. **Q3.** Should we expose a `pbi_purge_table` tool that wipes a table and clears state for it? Useful for "start fresh" operations but dangerous.
4. **Q4.** What dedup policy do we want for `Elements` when two pushes target the same model from different filter scopes (e.g. one publishes Walls only, another publishes Doors only)? Today they collide on `ElementId` and the last-write-wins per `whole-table` scope. Should `entity` scope work for `Elements` keyed by `(CategoryFilter, ElementId)`? Or is the natural unit for `Elements` always the whole model?
5. **Q5.** Do we want a "dry-run" mode that computes the union and reports what *would* be posted without actually deleting/posting? Aligns with the `dryRun` pattern in destructive RevitCortex tools.

---

## 10. References

- [Push Datasets REST API](https://learn.microsoft.com/en-us/rest/api/power-bi/push-datasets)
- [Push semantic model limitations](https://learn.microsoft.com/en-us/power-bi/developer/embedded/push-datasets-limitations)
- [Real-time streaming retirement notice](https://learn.microsoft.com/en-us/power-bi/connect-data/service-real-time-streaming)
- [Imports - Post Import](https://learn.microsoft.com/en-us/rest/api/power-bi/imports/post-import)
- [Data refresh in Power BI](https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data)
- [Using incremental refresh with dataflows](https://learn.microsoft.com/en-us/power-query/dataflows/incremental-refresh)
- [Direct Lake overview](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview)
- Internal: `docs/superpowers/specs/2026-05-11-powerbi-live-phase2c-design.md` (Phase 2C handoff)
- Internal: `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs` (schema v1.1)

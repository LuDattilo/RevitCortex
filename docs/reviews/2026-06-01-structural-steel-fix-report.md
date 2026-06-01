# Structural Steel Commands - Fix Report

Date: 2026-06-01  
Scope reviewed: merge `feat/structural-steel` into `main`, especially:

- `src/RevitCortex.Tools/StructuralSteel/*`
- `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- `src/RevitCortex.Tests/StructuralSteel/*`
- `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs`
- `docs/USER_GUIDE.md`, `WORKFLOWS.md`, `tool-schemas.txt`

## Verification Baseline

Commands run during review:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Results:

- R25 plugin build: passed, 0 errors.
- R24 plugin build: passed, 0 errors, existing warnings outside StructuralSteel.
- MCP server build: passed, 0 errors.
- Tests: passed, 297 passed, 1 skipped.

Note: initial `--no-restore` attempts failed because restored assets were stale/missing for `net8.0-windows10.0.19041.0`; rerunning with restore resolved R25/tests. This is environmental, not a StructuralSteel code failure.

## Executive Summary

The StructuralSteel suite is broadly well organized and compiles across R25/R24. The main risks are not syntax or registration errors; they are behavioral contract issues:

1. `set_steel_connection_type` recreates a connection and may silently drop handler state.
2. capability reporting says some things are supported/detectable when the implementation later reports them as impossible from JSON.
3. documentation overstates `dryRun` coverage for write tools.
4. one fabrication command uses a non-public Revit API setter via reflection.

Recommended fix order:

1. Fix `set_steel_connection_type` data preservation and response contract.
2. Correct capabilities payload and docs for provider/custom mutation semantics.
3. Make `dryRun` behavior consistent or narrow documentation/tool descriptions.
4. Decide whether to keep, gate, or remove reflective setting of fabrication unique IDs.

## Fix 1 - Preserve State in `set_steel_connection_type`

Severity: P1  
Files:

- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTools.cs`
- `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- `docs/USER_GUIDE.md`
- `WORKFLOWS.md`
- `tool-schemas.txt` only if schema/parameters change
- tests under `src/RevitCortex.Tests/StructuralSteel/`

### Current Behavior

`SetSteelConnectionTypeTool` reads the connected element ids, deletes the existing `StructuralConnectionHandler`, and creates a new handler with the requested type.

The result currently preserves:

- connected element ids
- new connection handler type
- new connection id

Likely lost state:

- `ApprovalTypeId`
- `CodeCheckingStatus`
- `OverrideTypeParams`
- `SingleElementEndIndex`
- input points / input references
- any comments/parameters attached to the handler element

Input points are already documented as not preservable because `ConnectionInputPoint` is not reconstructible from JSON. The problem is that the tool response does not report the broader state loss, and the tool does not attempt to restore simple writable properties.

### Proposed Implementation

Before deletion, snapshot all readable state:

- connected element ids
- old handler type id/name
- approval type id
- code-checking status
- `OverrideTypeParams`
- `SingleElementEndIndex`
- input point count
- input reference count, if safely readable

During actual execution:

1. Start one transaction.
2. Delete old handler.
3. Create new handler.
4. Restore best-effort writable state:
   - `created.ApprovalTypeId = oldApprovalTypeId` when valid.
   - `created.CodeCheckingStatus = oldStatus`.
   - `created.OverrideTypeParams = oldOverrideTypeParams`.
   - `created.SingleElementEndIndex = oldSingleElementEndIndex`, only if Revit accepts it.
5. Return:
   - `restoredFields`
   - `lostFields`
   - `warnings`
   - `previousConnectionId`
   - `connectionId`

Dry-run should include exactly what will be preserved and what will be lost.

### Suggested Response Shape

```json
{
  "dryRun": true,
  "connectionId": 123,
  "newConnectionHandlerTypeId": 456,
  "connectedElementIds": [10, 11],
  "willPreserve": ["connectedElementIds", "approvalTypeId", "codeCheckingStatus", "overrideTypeParams"],
  "willLose": ["inputPoints", "inputReferences"],
  "stateSnapshot": {
    "approvalTypeId": 789,
    "codeCheckingStatus": "NotCalculated",
    "overrideTypeParams": false,
    "singleElementEndIndex": 0,
    "inputPointCount": 2
  }
}
```

### Tests

Because most Revit API behavior cannot be unit-tested without Revit, add source/contract tests that verify:

- dry-run response includes `willPreserve` and `willLose`;
- implementation references `ApprovalTypeId`, `CodeCheckingStatus`, `OverrideTypeParams`;
- docs state the recreation limitation clearly.

Manual Revit smoke test:

1. Create a generic/typed connection.
2. Set approval and code-checking status.
3. Run `set_steel_connection_type` with `dryRun: true`.
4. Run real command.
5. Verify with `get_steel_connection_data` that preserved fields survived.

## Fix 2 - Correct Capability Semantics

Severity: P2  
Files:

- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelDiscoveryTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelToolHelpers.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelProviderTools.cs`
- `docs/USER_GUIDE.md`
- `WORKFLOWS.md`
- tests under `src/RevitCortex.Tests/StructuralSteel/`

### Current Behavior

`get_structural_steel_api_capabilities` returns:

- `connectionProviderInstalled = false`
- `supportsCustomConnectionMutation = true` on R23-R26

But provider availability is not queryable through the public Revit API, and `manage_custom_steel_connection_type` always fails because Reference/Subelement mutation cannot be expressed from JSON.

This creates a misleading contract:

- `false` reads as "no provider installed", when the truth is "unknown/not queryable".
- `supportsCustomConnectionMutation = true` reads as "the command can perform mutation", while the command cannot mutate from MCP input.

### Proposed Implementation

Keep backwards-compatible fields if needed, but add explicit fields:

```json
{
  "connectionProviderInstalled": false,
  "connectionProviderDetection": "not_queryable_public_api",
  "connectionProviderState": "unknown",
  "customConnectionMutationApiMembersExist": true,
  "supportsCustomConnectionMutationFromJson": false,
  "customConnectionMutationReason": "requires interactive Reference/Subelement objects"
}
```

For R27:

```json
{
  "customConnectionMutationApiMembersExist": false,
  "supportsCustomConnectionMutationFromJson": false,
  "customConnectionMutationReason": "legacy add/remove APIs removed; UpdateCustomConnectionType still requires Reference objects"
}
```

Update `ProviderUnavailableError` wording so it does not imply a provider is known absent when detection is impossible.

### Tests

Add contract tests verifying capabilities response source contains:

- `supportsCustomConnectionMutationFromJson`
- `connectionProviderState` or `connectionProviderDetection`
- no wording that reports provider absence as a fact.

Manual check:

```json
get_structural_steel_api_capabilities {}
```

Confirm the payload differentiates:

- Revit API member existence
- MCP JSON-expressible capability
- provider detection state

## Fix 3 - Align `dryRun` Documentation and Behavior

Severity: P2  
Files:

- `docs/USER_GUIDE.md`
- `WORKFLOWS.md`
- `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- write tools in `src/RevitCortex.Tools/StructuralSteel/`

### Current Behavior

Docs say write tools accept `dryRun: true`, but only some do.

Examples with dry-run:

- `create_generic_steel_connection`
- `create_steel_connection`
- `set_steel_connection_type`
- `delete_steel_connection`
- `create_steel_structural_connection_type`
- `add_steel_fabrication_info`
- `add_steel_solid_cut`
- `add_steel_instance_void_cut`

Examples without dry-run despite being write operations:

- `modify_steel_connection_inputs`
- `set_steel_connection_approval`
- `set_steel_connection_status`
- `set_steel_connection_default_order`
- `remove_steel_solid_cut`
- `set_steel_solid_cut_face_splitting`
- `remove_steel_instance_void_cut`
- `set_steel_fabrication_unique_id`

All these tools still have native confirmation, but a caller who passes `dryRun: true` to a non-supporting command gets a real write after confirmation rather than an explicit preview.

### Proposed Implementation Options

Preferred: add dry-run to all write tools.

For mutators, dry-run should return:

- target ids;
- action;
- current state if cheap/readable;
- intended new state;
- `dryRun = true`.

Minimum acceptable: narrow documentation and descriptions:

- "Some write tools support dryRun; all write tools show native confirmation."
- In `tool-schemas.txt`, only tools with actual dry-run expose `dryRun`.
- In `USER_GUIDE.md`, list dry-run-supported commands explicitly.

### Tests

Add a source-level StructuralSteel dry-run coverage test:

- enumerate StructuralSteel write tool names;
- assert each either implements `dryRun` or appears in a documented exception list.

This prevents future drift.

## Fix 4 - Reassess Reflective `UniqueID` Setter

Severity: P3  
Files:

- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelFabricationTools.cs`
- `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- `docs/USER_GUIDE.md`
- `WORKFLOWS.md`

### Current Behavior

`set_steel_fabrication_unique_id` uses reflection to invoke a non-public setter for `SteelElementProperties.UniqueID`.

The implementation is defensive and returns structured failure if the setter is absent, but it still depends on an internal/non-public API path. This conflicts with the otherwise conservative StructuralSteel design, where unavailable public API is reported honestly instead of guessed.

### Proposed Decision

Choose one of these:

1. Remove write support and convert the tool to an unsupported structured failure, matching the provider/custom-connection philosophy.
2. Keep it but rename/describe as experimental/internal:
   - add `experimental = true` in result;
   - document that it relies on a non-public setter and may fail across Revit versions;
   - add a capability field such as `supportsSetSteelFabricationUniqueId = "best_effort_non_public_setter"`.
3. Gate it behind a setting if RevitCortex wants to avoid hidden API mutation by default.

### Tests

Add source-level test that verifies docs mention the non-public setter if the command remains available.

Manual Revit smoke:

1. Add steel fabrication info to a disposable steel element.
2. Read current unique id.
3. Set a new GUID.
4. Read again.
5. Resolve via `get_steel_reference_by_fabrication_id`.

## Suggested Fix Sequence

1. Patch `SetSteelConnectionTypeTool` snapshot/restore/report behavior.
2. Add dry-run support to the most dangerous non-preview mutators:
   - remove cuts;
   - set approval/status/default order;
   - set fabrication unique id, if kept.
3. Patch capability payload semantics.
4. Update `USER_GUIDE.md`, `WORKFLOWS.md`, and regenerate `tool-schemas.txt` if wrapper signatures change.
5. Add tests:
   - source-level test for state preservation fields;
   - capabilities contract test;
   - dry-run coverage test;
   - docs consistency test for reflective setter, if retained.
6. Verify:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

## Acceptance Criteria

- `set_steel_connection_type` no longer silently drops simple writable state.
- dry-run for `set_steel_connection_type` reports state preservation/loss before deletion.
- capabilities no longer imply provider absence or executable custom mutation when the public API is not queryable/JSON-expressible.
- docs accurately state which write tools support dry-run.
- any reflective/non-public fabrication setter is either removed, gated, or clearly marked experimental.
- R25 and R24 plugin builds pass.
- R26 tests pass.


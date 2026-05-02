# Coordination Model Read-Only Tools Design

## Context

RevitCortex currently has mature linked-file tooling for Revit links, CAD imports, IFC links, and cross-model selection. Autodesk Revit SDK 2026.4 and 2027 include `CoordinationModel` samples that expose `Autodesk.Revit.DB.ExternalData.CoordinationModelLinkUtils` for coordination model links.

The first iteration should add value without changing model state. It will discover coordination models and report compact metadata through MCP.

## Goals

- Add read-only visibility into coordination models linked in the active Revit document.
- Keep the feature safe for daily use: no transactions, reloads, visibility overrides, or link creation.
- Preserve cross-target builds for R24/R25/R26/R27.
- Follow existing `ICortexTool` patterns and place the feature near existing `LinkedFiles` tools.

## Non-Goals

- Do not link local or cloud coordination models.
- Do not reload, unload, hide, show, color, or change transparency.
- Do not copy Autodesk SDK sample code directly.
- Do not replace existing `manage_links` or Revit link tooling.

## Tool Surface

Add one tool:

`get_coordination_models`

Category: `LinkedFiles`

Requires document: `true`

Dynamic: `false` for the first iteration, because unavailable API targets are handled inside the tool with a controlled response.

Inputs:

- `nameFilter` optional string, case-insensitive partial match against model/type/instance names.
- `includeInstances` optional bool, default `true`.
- `maxInstances` optional int, default `100`, capped to a conservative upper bound.

Output:

- `modelCount`: number of coordination model types returned.
- `totalInstances`: number of included instances.
- `apiAvailable`: bool.
- `models`: compact list grouped by type.

Each model item should include, where the Revit API exposes it:

- `typeId`
- `typeName`
- `pathType`, for example local or cloud
- `isCloud`
- `path` or user-visible path when available
- `instanceCount`
- `instances`, when requested, with `instanceId`, `name`, and transform origin in millimeters.

If the active target does not expose the coordination model API, return a successful empty/unsupported payload rather than throwing:

```json
{
  "apiAvailable": false,
  "modelCount": 0,
  "totalInstances": 0,
  "models": [],
  "message": "Coordination Model API is not available for this Revit target."
}
```

## Architecture

Implement the tool in `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs`.

The tool should:

1. Read the active `Document` from `CortexSession.Store`.
2. For Revit targets where coordination model APIs are available, collect candidate elements/types using Revit API collectors and `CoordinationModelLinkUtils` predicates.
3. Group instances by type.
4. Convert `ElementId` values through `ToolHelpers.GetElementIdValue`.
5. Return compact anonymous objects through `CortexResult<object>.Ok`.

For unsupported targets, compile the same class but route execution to a controlled unsupported response using preprocessor guards.

## Compatibility

The implementation must compile on:

- `Debug R24` (`net48`)
- `Debug R25` (`net8.0-windows`)
- `Debug R26` (`net8.0-windows`)
- `Debug R27` (`net10.0-windows`, when the machine has a compatible SDK setup)

If the coordination model API is only available on R26+, all direct references to `Autodesk.Revit.DB.ExternalData.CoordinationModelLinkUtils` and related types must live inside `#if REVIT2026_OR_GREATER` blocks.

The code must avoid net8-only syntax that breaks `net48`, including records, `init`, `Dictionary.GetValueOrDefault`, ranges, and default interface methods.

## Error Handling

- Missing active document: `CortexErrorCode.InvalidInput`.
- Invalid input values, such as negative `maxInstances`: `CortexErrorCode.InvalidInput`.
- Unexpected Revit API failure: `CortexErrorCode.Unknown` with a concise message.
- Unsupported target: successful controlled response with `apiAvailable: false`.

## Tests and Verification

Automated tests should rely on existing tool registration conventions where possible. Logic that does not require live Revit objects can be covered with small helper tests if introduced.

Verification commands:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Run R27 only if the .NET SDK setup supports it in the current environment.

## Rollout

After implementation, regenerate tool schema signatures if the server/tool schema inventory requires it:

```powershell
node server/generate-tool-schemas-csharp.mjs
```

Document the new workflow in `WORKFLOWS.md` only after the tool is verified against a real model or a repeatable smoke test.

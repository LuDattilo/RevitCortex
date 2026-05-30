# RevitCortex Complete Structural Steel API Design

**Date:** 2026-05-30
**Status:** Draft design, pending implementation plan
**Owner:** RevitCortex

## Goal

Add a complete, practical RevitCortex MCP surface for Revit structural steel fabrication workflows: steel fabrication metadata, structural connection handlers, connection types, approval/status data, custom/generic connections, solid cuts, instance void cuts, steel warnings, material/external id links, and provider/capability reporting.

The implementation must preserve the project rules:

- Every plugin command returns `CortexResult<object>`.
- Every destructive or model-changing command uses `session.RequestConfirmation(...)`, with dry-run support where a preview is meaningful.
- All C# changes build for `Debug R25` and `Debug R24`; release work also validates R23, R26, and R27.
- Revit 2023/2024 net48 compatibility takes precedence over newer syntax or APIs.
- Tools use language-independent identifiers where possible: element ids, `OST_*` categories, type ids, enum names, connection GUIDs, and failure ids.

## Source Verification

The API surface was checked against the local NuGet RevitAPI XML documentation packages used by the repo:

- `Nice3point.Revit.Api.RevitAPI` 2023.1.90
- `Nice3point.Revit.Api.RevitAPI` 2024.3.40
- `Nice3point.Revit.Api.RevitAPI` 2025.4.50
- `Nice3point.Revit.Api.RevitAPI` 2026.4.10
- `Nice3point.Revit.Api.RevitAPI` 2027.0.20

The public Autodesk references used for design validation are:

- Steel Fabrication API guide: `https://help.autodesk.com/cloudhelp/2025/ITA/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/Structural_Engineering/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_Structural_Engineering_Steel_Fabrication_html.html`
- `SteelElementProperties` API reference: `https://help.autodesk.com/view/RVT/2025/ENU/?guid=911b649a-d108-14a2-dc09-8e97d489c17d`
- Structural steel connections product guide: `https://help.autodesk.com/cloudhelp/2024/ENU/Revit-StructEng/files/GUID-14D5F295-680A-440B-848C-847363BA0D06.htm`
- Structural connection input elements: `https://help.autodesk.com/cloudhelp/2024/ENU/Revit-StructEng/files/GUID-D5EBC18C-FDA5-49F8-84D8-1FD134886DB6.htm`
- Structural steel fabrication shape guide: `https://help.autodesk.com/cloudhelp/2027/ENU/Revit-StructEng/files/GUID-8381A08F-CBCD-4215-AB36-82648C07B8F6.htm`

The local API inventory found the following core steel-related APIs in Revit 2026:

- `Autodesk.Revit.DB.Steel.SteelElementProperties`: 27 methods, 4 properties.
- `Autodesk.Revit.DB.Structure.StructuralConnectionHandler`: 18 methods, 5 properties.
- `Autodesk.Revit.DB.Structure.StructuralConnectionHandlerType`: 26 methods, 1 property.
- `Autodesk.Revit.DB.Structure.StructuralConnectionType`: 5 methods, 1 property.
- `Autodesk.Revit.DB.Structure.StructuralConnectionApprovalType`: approval type element support.
- `Autodesk.Revit.DB.Structure.StructuralConnectionSettings`: connection settings support.
- `Autodesk.Revit.DB.Structure.ConnectionValidationInfo` and `ConnectionValidationWarning`: validation result support.
- `Autodesk.Revit.DB.Structure.StructuralConnectionsProviderRegistry` and `IStructuralConnectionsProvider`: provider discovery/extension infrastructure.
- `Autodesk.Revit.DB.SolidSolidCutUtils`: 10 methods.
- `Autodesk.Revit.DB.InstanceVoidCutUtils`: 7 methods.
- Steel failure namespaces such as `BuiltInFailures.SteelElementFailures`, `StructuralConnectionFailures`, and `StructuralCustomConnectionFailures`.

Important verified method families:

- `SteelElementProperties`: `AddFabricationInformationForRevitElements`, `AddToElement`, `CanHaveCell`, `GetSteelElementProperties`, `GetFabricationUniqueID`, `GetReference`, `GetExternalId`, `GetRevitId`, `GetAllExternalIds`, `GetAllRevitMaterialsIds`, `RegisterMaterial`, `RemoveLink`, `ClearWarnings`, `CountOfAsyncWarnings`, `GetCurrWarnings`, `GetElemsWithWarnings`, `PostWarning`, `RemoveWarning`, `FlushWarnings`, `SetChanged`.
- `StructuralConnectionHandler`: `Create`, `CreateGenericConnection`, `AddElementIds`, `AddReferences`, `RemoveElementIds`, `RemoveReferences`, `GetConnectedElementIds`, `GetInputPoints`, `GetInputReferences`, `GetInputPoint`, `GetOrigin`, `GetFailed`, `SetDefaultElementOrder`, plus approval/code-checking/disconnect/override properties.
- `StructuralConnectionHandlerType`: `Create`, `CreateDefaultStructuralConnectionHandlerType`, `FindGenericConnectionType`, `GetDefaultConnectionHandlerType`, `GetInputPointsInfo`, `SetInputPointsInfo`, `AddElementsToCustomConnection`, `UpdateCustomConnectionType`, `RemoveMainSubelementsFromCustomConnection`, and detailed/custom data buffer methods.
- `StructuralConnectionType`: `Create`, `GetAllStructuralConnectionTypeIds`, `GetFamilySymbolId`, `SetFamilySymbolId`, `ValidFamilySymbolId`.
- `SolidSolidCutUtils`: `AddCutBetweenSolids`, `RemoveCutBetweenSolids`, `CutExistsBetweenElements`, `CanElementCutElement`, `GetCuttingSolids`, `GetSolidsBeingCut`, `IsAllowedForSolidCut`, `SplitFacesOfCuttingSolid`.
- `InstanceVoidCutUtils`: `AddInstanceVoidCut`, `RemoveInstanceVoidCut`, `InstanceVoidCutExists`, `GetCuttingVoidInstances`, `GetElementsBeingCut`, `CanBeCutWithVoid`, `IsVoidInstanceCuttingElement`.

## Compatibility Strategy

The baseline implementation targets APIs available in Revit 2023/2024 whenever possible. Local XML verification found:

- `SteelElementProperties`, `StructuralConnectionHandler`, `StructuralConnectionHandlerType`, `StructuralConnectionType`, `StructuralConnectionApprovalType`, `SolidSolidCutUtils`, and `InstanceVoidCutUtils` are available in R23-R27.
- `AssemblyInstance.IsPrecastAssembly` is not relevant to steel but was checked separately for Precast.
- `StructuralConnectionHandlerType.AddElementsToCustomConnection(...)` is present in R23-R26 and not present with the same XML signature in R27. Calls that need this method must be isolated behind a version adapter and return a structured unsupported-version error if the active target does not expose it.
- Detailed/custom connection data methods that require raw `IntPtr` buffers are not a good public MCP contract. They should be wrapped only as safe summaries or intentionally excluded from mutation tools unless a first-party typed adapter is added.

Tool schemas should remain stable across versions. If a caller requests a feature unavailable in the active target, the plugin returns `CortexResult.Fail(CortexErrorCode.InvalidInput, ...)` with a suggestion that names the minimum or maximum supported Revit version.

## Architecture

Add a first-class `StructuralSteel` category.

Files to create:

- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelConnectionTypeTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelFabricationTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelCutTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelProviderTools.cs`
- `src/RevitCortex.Tools/StructuralSteel/StructuralSteelToolHelpers.cs`
- `src/RevitCortex.Server/Tools/StructuralSteelTools.cs`
- `src/RevitCortex.Tests/StructuralSteel/StructuralSteelToolContractTests.cs`
- `src/RevitCortex.Tests/StructuralSteel/StructuralSteelCompatibilityTests.cs`

Existing files to modify:

- `src/RevitCortex.Tools/RevitCortex.Tools.csproj` only if version-specific compile constants are needed.
- `src/RevitCortex.Plugin/Discovery/DocumentAnalyzer.cs` if dynamic structural steel capabilities are added.
- `src/RevitCortex.Core/Discovery/DocumentCapabilities.cs` if dynamic structural steel capabilities are added.
- `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` only if the expected minimum tool count needs to move.
- `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs` for new read-only naming coverage.
- `docs/USER_GUIDE.md`
- `tool-schemas.txt`, regenerated by `node server/generate-tool-schemas-csharp.mjs`
- `WORKFLOWS.md`

`StructuralSteelToolHelpers` owns shared logic:

- `RequireElement(Document, long)`
- `RequireSteelCandidate(Document, long)`
- `RequireConnectionHandler(Document, long)`
- `RequireConnectionHandlerType(Document, long?)`
- `ResolveStructuralConnectionType(Document, long?, string?)`
- `ResolveApprovalType(Document, long?, string?)`
- `ResolveElementIds(Document, JArray, string fieldName)`
- `ResolveReferences(Document, JArray, string fieldName)`
- `ParseConnectionInputPoints(JArray)`
- `ParseConnectionInputPointInfo(JArray)`
- `ParseConnectionStatus(string?)`
- `ParseCutOptions(JObject)`
- `ToMm(double feet)`
- `FromMm(double mm)`
- `GetElementIdValue(ElementId)`
- `EnumParseOrError<TEnum>(string, string fieldName)`
- version-gated helpers for custom connection APIs that changed in R27

## Tool Surface

### Module 1 - Discovery and Inventory

Read-only tools:

- `get_structural_steel_api_capabilities`
- `list_steel_connection_handlers`
- `list_steel_connection_types`
- `list_steel_connection_handler_types`
- `list_steel_approval_types`
- `list_steel_connection_providers`
- `get_steel_connection_data`
- `get_steel_connection_type_data`
- `get_steel_connection_settings`
- `get_steel_element_properties`
- `get_steel_external_id_map`
- `get_steel_material_links`
- `get_steel_element_warnings`
- `get_steel_cut_data`
- `analyze_structural_steel_model`

These tools return compact summaries by default. Large arrays such as all external ids, all warnings, all handlers, or all cut relationships must be capped by explicit `maxResults` parameters.

### Module 2 - Connection Creation and Mutation

Write tools:

- `create_steel_connection`
- `create_generic_steel_connection`
- `modify_steel_connection_inputs`
- `set_steel_connection_type`
- `set_steel_connection_approval`
- `set_steel_connection_status`
- `set_steel_connection_disconnected`
- `set_steel_connection_default_order`
- `delete_steel_connection`

`create_steel_connection` supports creation from element ids and a connection handler type id/name. `create_generic_steel_connection` uses `StructuralConnectionHandler.CreateGenericConnection(...)` and is the safe baseline when detailed connection providers are not installed.

`modify_steel_connection_inputs` supports action-based operations: `add_element_ids`, `remove_element_ids`, `add_references`, and `remove_references`. Raw arbitrary references are not accepted as strings. References are resolved only from supported descriptors such as element id plus stable reference hint, selected subelement, or current selection.

### Module 3 - Connection Type and Approval Administration

Write tools:

- `create_steel_connection_handler_type`
- `create_default_steel_connection_handler_type`
- `create_steel_structural_connection_type`
- `set_steel_connection_type_family_symbol`
- `manage_steel_approval_type`
- `manage_custom_steel_connection_type`

Read-only tools:

- `get_steel_connection_input_points`
- `get_steel_connection_applicability`
- `get_steel_connection_validation`

`manage_custom_steel_connection_type` is version-gated and intentionally narrow. It supports documented custom connection member operations where typed Revit objects can be resolved safely. It does not expose arbitrary `IntPtr` detailed parameter buffers through MCP.

### Module 4 - Fabrication Metadata, Materials, and Warnings

Write tools:

- `add_steel_fabrication_info`
- `attach_steel_fabrication_link`
- `remove_steel_fabrication_link`
- `register_steel_material`
- `post_steel_warning`
- `remove_steel_warning`
- `clear_steel_warnings`
- `flush_steel_warnings`
- `mark_steel_element_changed`

Read-only tools:

- `get_steel_fabrication_unique_id`
- `get_steel_revit_element_by_fabrication_id`
- `get_steel_external_material`
- `get_steel_warning_counts`

These tools wrap `SteelElementProperties` and should be used for structural members, connection handlers, and specific connection subelements such as bolts, anchors, plates, holes, welds, studs, and modifiers.

### Module 5 - Solid Cuts and Instance Void Cuts

Write tools:

- `add_steel_solid_cut`
- `remove_steel_solid_cut`
- `set_steel_solid_cut_face_splitting`
- `add_steel_instance_void_cut`
- `remove_steel_instance_void_cut`

Read-only tools:

- `get_solid_cut_relationships`
- `get_instance_void_cut_relationships`
- `check_steel_cut_eligibility`

Solid cut and void cut tools are not limited to only steel elements in the Revit API, but the StructuralSteel category exposes them because they are central to steel detailing. Inputs still validate categories and return warnings when the operation is generic Revit geometry rather than a steel fabrication-specific operation.

### Module 6 - Provider and Extension Infrastructure

Read-only tools:

- `get_structural_connection_provider_registry`
- `get_structural_connection_provider_data`
- `get_structural_connection_validation_info`

The API contains provider interfaces and extension-server style infrastructure. RevitCortex will not compile arbitrary provider implementations from MCP input. Provider APIs are covered by discovery and capability reporting. A future first-party compiled provider can be added as a normal plugin feature, not as user-submitted code.

## Input Contracts

Element ids are numeric ids. Coordinates and distances are millimeters in MCP inputs unless a field explicitly says otherwise. Revit internal feet are never exposed as the primary user-facing unit.

Connection creation:

```json
{
  "elementIds": [12345, 12346],
  "connectionHandlerTypeId": 56789,
  "connectionName": "Base plate",
  "inputPoints": [
    {"id": "primary", "x": 0, "y": 0, "z": 0}
  ],
  "dryRun": true
}
```

Connection input mutation:

```json
{
  "connectionId": 12345,
  "action": "add_element_ids",
  "elementIds": [22334, 22335],
  "dryRun": true
}
```

Cut creation:

```json
{
  "cutElementId": 1001,
  "targetElementId": 1002,
  "splitFaces": true,
  "dryRun": true
}
```

Fabrication link:

```json
{
  "elementIds": [12345, 12346],
  "fabricationGuid": "00000000-0000-0000-0000-000000000000",
  "dryRun": true
}
```

## Result Contracts

Creation and mutation tools return:

- created or modified element ids
- resolved type ids and names
- connected input element ids
- input references that were accepted or skipped
- approval/status/code-checking data when available
- warning ids and messages
- `versionLimitations` when a request was degraded or blocked by target version

Read tools return:

- compact summaries first
- optional detailed arrays capped by explicit `max*` parameters
- all distances in millimeters
- enum names as strings
- GUIDs as strings

Failures use existing `CortexErrorCode` values:

- `InvalidInput` for bad ids, unsupported enum values, invalid connection input, unavailable version, invalid cut relationship, or missing provider support
- `ElementNotFound` for missing elements, handlers, handler types, structural connection types, approval types, materials, or references
- `PermissionDenied` for read-only mode or unsafe provider/server-code requests
- `TransactionFailed` for failed Revit transactions
- `Cancelled` when the user cancels the confirmation dialog
- `Unknown` for unexpected Revit API exceptions after contextual details are captured

## Safety and Read-Only Mode

Read-only tools must use allowed prefixes: `get_`, `list_`, `analyze_`, or `check_`.

Write tools must not use read-only prefixes. They must:

- validate all ids before opening a transaction
- support `dryRun: true` for bulk or destructive changes where preview is meaningful
- request confirmation before model writes
- return counts and summaries rather than huge per-element payloads by default
- avoid raw `IntPtr` parameter buffer mutation from MCP input

Destructive operations include deleting connections, removing connection inputs, changing approval/status, removing fabrication links, clearing/flushing warnings, material relinking, adding/removing cuts, and changing custom connection members.

## Dynamic Capabilities

Most structural steel tools can be registered as always-on because they return useful errors if a project has no structural steel setup. Dynamic capabilities are useful for model-specific hints:

- `hasStructuralFraming`
- `hasStructuralColumns`
- `hasStructuralConnectionHandlers`
- `hasSteelFabricationLinks`
- `hasSteelWarnings`
- `hasStructuralConnectionTypes`
- `hasStructuralConnectionProviders`
- `supportsCustomSteelConnectionMutation`
- `supportsSteelElementProperties`
- `supportsSolidSolidCutUtils`
- `supportsInstanceVoidCutUtils`

The MCP-visible tool list should not explode or disappear unpredictably. Prefer stable tool availability plus `get_structural_steel_api_capabilities` for exact availability.

## Testing Strategy

Tests that do not require Revit:

- tool registration uniqueness and snake_case names
- server wrapper method signatures in `StructuralSteelTools.cs`
- read-only naming conventions
- JSON input parser tests for connection creation, input mutation, cut specs, fabrication link specs, and enum parsing
- version-guard tests for the R27 custom connection API change
- helper tests for unit conversion, id conversion, GUID parsing, and category checks

Build verification:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
node server/generate-tool-schemas-csharp.mjs
```

Manual Revit smoke tests:

- open a structural steel sample with beams and columns
- list connection handler types and structural connection types
- create a generic connection between valid structural members
- read connection inputs, origin, approval, detailed/custom flags, and code-checking status
- add and remove a supported input element
- add fabrication information to supported steel elements
- read and resolve fabrication unique ids
- register a material link and read it back
- create and remove a solid cut between eligible elements
- create and remove an instance void cut where a valid void family is available
- verify read-only mode blocks write tools
- verify cancelled TaskDialog returns `Cancelled`

## Documentation Updates

Update:

- `docs/USER_GUIDE.md` with a Structural Steel section and examples.
- `tool-schemas.txt` after every schema change.
- `WORKFLOWS.md` with practical structural steel workflows:
  - discover steel connection setup
  - create a generic connection
  - inspect connection inputs and failed state
  - attach and inspect steel fabrication information
  - manage steel warnings
  - create and inspect steel cuts
  - validate read-only mode before steel authoring

Add an operational warning to `WORKFLOWS.md`: detailed steel connections depend on installed structural connection providers and valid steel fabrication-compatible families. Prefer `create_generic_steel_connection` when provider availability is unknown.

## Implementation Order

Implementation should be delivered in separate, buildable steps:

1. Shared helpers plus discovery tools.
2. Generic and typed connection read tools.
3. Generic connection creation and safe input mutation.
4. Connection type and approval/status tools.
5. Steel fabrication metadata, material links, and warnings.
6. Solid and instance void cut tools.
7. Provider/capability reporting.
8. Documentation, schema regeneration, and full verification.

Each step should compile and test before the next step begins.

## Open Risks

- Detailed steel connections depend on installed Revit structural connection providers, steel content, valid input shapes, and model configuration.
- Some structural connection data uses raw binary buffers and `IntPtr` APIs that are not safe or stable as public MCP contracts.
- Revit references and subelements are difficult to serialize safely. Public contracts must use supported target descriptors instead of arbitrary raw references.
- The R27 local XML no longer exposes the same `AddElementsToCustomConnection` signature found in R23-R26.
- Steel fabrication shape creation and updates can involve asynchronous warnings; tools must report queued/current warnings clearly.
- Solid/void cut APIs are generic Revit APIs and may succeed on non-steel elements. Tool results must state whether the operation was steel-specific or generic geometry.

## Acceptance Criteria

The work is complete when:

- Every tool listed in this design is implemented or intentionally version-gated with a clear structured response.
- R25 and R24 plugin builds pass.
- Server build passes.
- Unit tests pass.
- `tool-schemas.txt` is regenerated.
- `docs/USER_GUIDE.md` and `WORKFLOWS.md` document the new workflows.
- Manual smoke tests cover at least one successful generic steel connection, steel metadata read, steel warning read, solid cut, and instance void cut in Revit.

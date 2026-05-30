# RevitCortex Complete Precast API Design

**Date:** 2026-05-30
**Status:** Draft design, pending implementation plan
**Owner:** RevitCortex

## Goal

Add a complete, practical RevitCortex MCP surface for Revit precast workflows: core assemblies, assembly views, parts, part division/merge/offsets, precast assembly detection, optional Autodesk Structural Precast API automation, reinforcement generation, shop drawing generation, CAM export, and capability reporting.

The implementation must preserve the project rules:

- Every plugin command returns `CortexResult<object>`.
- Every destructive or model-changing command uses `session.RequestConfirmation(...)`, with dry-run support where a preview is meaningful.
- All C# changes build for `Debug R25` and `Debug R24`; release work also validates R23, R26, and R27.
- Revit 2023/2024 net48 compatibility takes precedence over newer syntax or APIs.
- Tools use language-independent identifiers where possible: element ids, type ids, `OST_*` categories, enum names, view template ids, and assembly ids.

## Source Verification

The core API surface was checked against the local NuGet RevitAPI XML documentation packages used by the repo:

- `Nice3point.Revit.Api.RevitAPI` 2023.1.90
- `Nice3point.Revit.Api.RevitAPI` 2024.3.40
- `Nice3point.Revit.Api.RevitAPI` 2025.4.50
- `Nice3point.Revit.Api.RevitAPI` 2026.4.10
- `Nice3point.Revit.Api.RevitAPI` 2027.0.20

The optional Structural Precast API surface was checked against locally installed Autodesk XML documentation:

- `C:\Program Files\Autodesk\Revit 2023\AddIns\Precast\Autodesk.Precast.API.xml`
- `C:\Program Files\Autodesk\Revit 2025\AddIns\Precast\Autodesk.Precast.API.xml`
- `C:\Program Files\Autodesk\Revit 2027\AddIns\Precast\Autodesk.Precast.API.xml`

The corresponding DLLs were found at:

- `C:\Program Files\Autodesk\Revit 2023\AddIns\Precast\Autodesk.Precast.API.dll`
- `C:\Program Files\Autodesk\Revit 2025\AddIns\Precast\Autodesk.Precast.API.dll`
- `C:\Program Files\Autodesk\Revit 2027\AddIns\Precast\Autodesk.Precast.API.dll`

No `Autodesk.Precast.API.dll` was found under `C:\Program Files\Autodesk\Revit 2024` or `C:\Program Files\Autodesk\Revit 2026` on this machine. The design therefore treats the Structural Precast API as optional runtime functionality, not as a static compile dependency.

The public Autodesk references used for design validation are:

- Assemblies and Views API guide: `https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Construction_Modeling/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Construction_Modeling_Assemblies_and_Views_html.html`
- Parts API guide: `https://help.autodesk.com/cloudhelp/2024/PTB/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Construction_Modeling/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Construction_Modeling_Parts_html.html`
- Structural Precast product/API overview: `https://help.autodesk.com/cloudhelp/2023/ENU/Revit-StructEng/files/GUID-3AA696FA-8CE6-43CC-AB60-D0BC305351EC.htm`
- About the Structural Precast API: `https://help.autodesk.com/cloudhelp/2023/ENU/Revit-StructEng/files/GUID-079D5ED9-AC40-4DB7-9951-B0C6C1D81BFF.htm`
- Precast API Shop Drawing Command: `https://help.autodesk.com/cloudhelp/2023/ENU/Revit-StructEng/files/GUID-F4049C69-116C-414C-8BA2-7CFD4C748F54.htm`

The local RevitAPI inventory found the following core construction-modeling APIs in Revit 2026:

- `Autodesk.Revit.DB.AssemblyInstance`: 17 methods, 3 properties.
- `Autodesk.Revit.DB.AssemblyViewUtils`: 12 methods.
- `Autodesk.Revit.DB.Part`: 7 methods, 3 properties.
- `Autodesk.Revit.DB.PartUtils`: 23 methods.
- `Autodesk.Revit.DB.PartMaker`: 3 methods.
- `AssemblyInstance.IsPrecastAssembly`: available in R23-R27.
- `ParameterTypeId.AssemblyPrecastFreeze`: available in R23-R27.
- `Application.IsPrecastEnabled` and `ControlledApplication.IsPrecastEnabled`: present in local R27 XML only.

The local `Autodesk.Precast.API.xml` inventory found 96 public `Autodesk.RevitPrecast.*` types in each installed version checked. Important namespaces and classes include:

- `Autodesk.RevitPrecast.PrecastUtils`
- `Autodesk.RevitPrecast.Reinforcement.ReinforcementCreator`
- `Autodesk.RevitPrecast.Reinforcement.IReinforcementOptions`
- `Autodesk.RevitPrecast.Reinforcement.IRebarArea`
- `Autodesk.RevitPrecast.Reinforcement.IReinforcementLayerBars`
- `Autodesk.RevitPrecast.Reinforcement.IReinforcementLayerFabricSheet`
- `Autodesk.RevitPrecast.Reinforcement.IReinforcementLayerGirders`
- `Autodesk.RevitPrecast.ShopDrawing.ShopDrawingCreator`
- `Autodesk.RevitPrecast.ShopDrawing.ShopDrawingCreatorFactory`
- `Autodesk.RevitPrecast.ShopDrawing.IShopDrawingOptions`
- `Autodesk.RevitPrecast.CamExport.CamExporter`
- `Autodesk.RevitPrecast.CamExport.ICamExportOptions`
- `Autodesk.RevitPrecast.Split.*`

Important verified method and option families:

- `AssemblyInstance`: `Create`, `PlaceInstance`, `AddMemberIds`, `RemoveMemberIds`, `SetMemberIds`, `GetMemberIds`, `Disassemble`, `GetCenter`, `GetTransform`, `SetTransform`, `CompareAssemblyInstances`, `AllowsAssemblyViewCreation`, `AreElementsValidForAssembly`, `CanRemoveElementsFromAssembly`, `IsMember`, `IsValidNamingCategory`, `IsPrecastAssembly`.
- `AssemblyViewUtils`: `Create3DOrthographic`, `CreateDetailSection`, `CreateMaterialTakeoff`, `CreatePartList`, `CreateSheet`, `CreateSingleCategorySchedule`, `AcquireAssemblyViews`.
- `Part`: `CanOffsetFace`, `GetFaceOffset`, `SetFaceOffset`, `ResetFaceOffset`, `ResetPartShape`, `GetSourceElementIds`, `GetSourceElementOriginalCategoryIds`, `Excluded`, `OriginalCategoryId`, `PartMaker`.
- `PartUtils`: `CreateParts`, `AreElementsValidForCreateParts`, `DivideParts`, `ArePartsValidForDivide`, `CreateMergedPart`, `ArePartsValidForMerge`, `FindMergeableClusters`, `GetAssociatedPartMaker`, `GetAssociatedParts`, `GetChainLengthToOriginal`, `GetMergedParts`, `GetPartMakerMethodToDivideVolumeFW`, `GetSplittingCurves`, `GetSplittingElements`, `HasAssociatedParts`, `IsMergedPart`, `IsPartDerivedFromLink`, `IsValidForCreateParts`.
- `PrecastUtils`: `GetProductType`, `GetReinforcementOptions`.
- `ReinforcementCreator`: `CreateSlabReinforcement`, `CreateWallReinforcement`.
- `ShopDrawingCreatorFactory`: `Get`, `GetUserDefined`.
- `ShopDrawingCreator`: `Create`.
- `CamExporter`: `Export`, `GetReinforcementExport`.
- `IShopDrawingOptions`: `AssemblyEcs`, `CenterOfGravitySymbolId`, `DimLineNames`, `DimensionToDimensionDistance`, `DimensionToElementDistance`, `DimensionTypeId`, `ExtentsName`, `OutlineName`, `OverwriteMode`, `ShopDrawingName`, `TagSymbols`, `TemplateId`, `TextNoteTypeId`.
- `ICamExportOptions`: `ExportReinforcementInformationOnly`, `FileFormat`, `MultipleElementsInOneFile`, `OverwriteMode`, `SubdirectoryPerProductType`, `TargetDirectory`.

## Compatibility Strategy

The baseline implementation uses only RevitAPI core assemblies/parts APIs for compilation. The optional Structural Precast API is accessed through a reflection adapter that is loaded at runtime only when `Autodesk.Precast.API.dll` is available beside the active Revit installation.

Required strategy:

- Do not add a static project reference to `Autodesk.Precast.API.dll`.
- Add `PrecastAddinAdapter` with reflection-based method discovery and invocation.
- `get_precast_api_capabilities` reports whether the optional DLL is present, loaded, and which method families are callable.
- Core tools for `AssemblyInstance`, `AssemblyViewUtils`, `Part`, `PartUtils`, and `PartMaker` are always compiled.
- Optional tools for reinforcement, shop drawings, and CAM export return `InvalidInput` or `PermissionDenied` with a clear suggestion when the Structural Precast API is not available in the active Revit installation.
- `Application.IsPrecastEnabled` is used only behind compile-time guards or reflection because it appears in local R27 XML only.

Tool schemas should remain stable across versions. If a caller requests optional Precast functionality on a Revit installation without the add-in DLL, the plugin returns `CortexResult.Fail(CortexErrorCode.InvalidInput, ...)` with a suggestion to install/enable Autodesk Structural Precast for that Revit version.

## Architecture

Add a first-class `Precast` category.

Files to create:

- `src/RevitCortex.Tools/Precast/PrecastAssemblyTools.cs`
- `src/RevitCortex.Tools/Precast/PrecastPartTools.cs`
- `src/RevitCortex.Tools/Precast/PrecastAddinAdapter.cs`
- `src/RevitCortex.Tools/Precast/PrecastReinforcementTools.cs`
- `src/RevitCortex.Tools/Precast/PrecastShopDrawingTools.cs`
- `src/RevitCortex.Tools/Precast/PrecastCamExportTools.cs`
- `src/RevitCortex.Tools/Precast/PrecastToolHelpers.cs`
- `src/RevitCortex.Server/Tools/PrecastTools.cs`
- `src/RevitCortex.Tests/Precast/PrecastToolContractTests.cs`
- `src/RevitCortex.Tests/Precast/PrecastCompatibilityTests.cs`
- `src/RevitCortex.Tests/Precast/PrecastAddinAdapterTests.cs`

Existing files to modify:

- `src/RevitCortex.Tools/RevitCortex.Tools.csproj` only if compile constants are needed.
- `src/RevitCortex.Plugin/Discovery/DocumentAnalyzer.cs` if dynamic precast capabilities are added.
- `src/RevitCortex.Core/Discovery/DocumentCapabilities.cs` if dynamic precast capabilities are added.
- `src/RevitCortex.Tests/Tools/ToolRegistrationTests.cs` only if the expected minimum tool count needs to move.
- `src/RevitCortex.Tests/Security/ReadOnlyModeTests.cs` for new read-only naming coverage.
- `docs/USER_GUIDE.md`
- `tool-schemas.txt`, regenerated by `node server/generate-tool-schemas-csharp.mjs`
- `WORKFLOWS.md`

`PrecastToolHelpers` owns shared core logic:

- `RequireAssembly(Document, long)`
- `RequirePart(Document, long)`
- `RequirePartMaker(Document, long)`
- `ResolveElementIds(Document, JArray, string fieldName)`
- `ResolveAssemblyType(Document, long?, string?)`
- `ResolveNamingCategory(Document, long?, string?)`
- `ResolveViewTemplate(Document, long?, string?)`
- `ResolveTitleBlock(Document, long?, string?)`
- `ParseXyzMm(JObject)`
- `ParseCurveSpecsMm(JArray)`
- `ParseTransformMm(JObject)`
- `ParseAssemblyViewRequest(JObject)`
- `ParsePartDivisionRequest(JObject)`
- `ParseFaceOffsetRequest(JObject)`
- `ToMm(double feet)`
- `FromMm(double mm)`
- `GetElementIdValue(ElementId)`
- `EnumParseOrError<TEnum>(string, string fieldName)`

`PrecastAddinAdapter` owns optional API logic:

- detect active Revit version and expected add-in folder
- load `Autodesk.Precast.API.dll` if present and safe
- discover `PrecastUtils`, `ReinforcementCreator`, `ShopDrawingCreatorFactory`, `ShopDrawingCreator`, `CamExporter`
- construct option objects from the installed implementation classes where available
- map JSON option specs to interface properties by reflection
- return a typed availability summary rather than throwing missing-method exceptions

## Tool Surface

### Module 1 - Capabilities, Assemblies, and Product Data

Read-only tools:

- `get_precast_api_capabilities`
- `list_precast_assemblies`
- `get_precast_assembly_data`
- `get_precast_product_type`
- `get_precast_reinforcement_options`
- `get_precast_assembly_comparison`
- `analyze_precast_model`

Write tools:

- `create_precast_assembly`
- `modify_precast_assembly_members`
- `place_precast_assembly_instance`
- `move_precast_assembly`
- `disassemble_precast_assembly`
- `set_precast_assembly_name`

`list_precast_assemblies` primarily uses `AssemblyInstance.IsPrecastAssembly` and falls back to category/type heuristics only when needed. It must state in results whether each item is API-confirmed as precast or inferred.

### Module 2 - Assembly Views, Sheets, and Schedules

Write tools:

- `create_precast_assembly_views`
- `create_precast_assembly_sheet`
- `create_precast_assembly_schedules`
- `acquire_precast_assembly_views`

Read-only tools:

- `get_precast_assembly_views`
- `get_precast_assembly_documentation_status`

These tools wrap `AssemblyViewUtils`. Supported outputs include orthographic 3D views, detail sections by `AssemblyDetailViewOrientation`, material takeoffs, part lists, single-category schedules, and sheets.

### Module 3 - Parts and Division

Read-only tools:

- `list_precast_parts`
- `get_precast_part_data`
- `get_precast_part_associations`
- `get_precast_part_division_data`
- `check_precast_part_eligibility`
- `find_precast_mergeable_part_clusters`

Write tools:

- `create_precast_parts`
- `divide_precast_parts`
- `merge_precast_parts`
- `set_precast_part_face_offset`
- `reset_precast_part_face_offset`
- `reset_precast_part_shape`
- `set_precast_part_excluded`
- `update_precast_part_sources`

Part division supports intersecting elements and explicit line/arc curve specs in millimeters. Face offset mutation requires a supported face descriptor, not arbitrary serialized geometry.

### Module 4 - Optional Structural Precast Reinforcement

Write tools:

- `create_precast_wall_reinforcement`
- `create_precast_slab_reinforcement`

Read-only tools:

- `get_precast_reinforcement_export`
- `get_precast_reinforcement_option_schema`

These tools require the optional `Autodesk.Precast.API.dll`. Reinforcement options map to `IReinforcementOptions`, `IRebarArea`, `IReinforcementLayerBars`, `IReinforcementLayerFabricSheet`, `IReinforcementLayerGirders`, and related enum types.

The public schema should support:

- area reinforcement source: from user configuration, none, or explicit options
- edge reinforcement source: from user configuration, none, or explicit options
- bars, girders, and fabric sheet layers
- rebar/fabric/girder type ids
- counts, distances, maximum distances, and offsets in millimeters
- inside/outside layer selection where exposed by the installed API

### Module 5 - Optional Structural Precast Shop Drawings

Write tools:

- `create_precast_shop_drawing`
- `create_precast_shop_drawings_batch`

Read-only tools:

- `get_precast_shop_drawing_defaults`
- `get_precast_shop_drawing_option_schema`

These tools require the optional `Autodesk.Precast.API.dll`. `ShopDrawingCreatorFactory.Get(...)` is the default path. `GetUserDefined(...)` is exposed as an explicit option because user-defined creation can depend more heavily on local configuration.

Events from the Precast API are not surfaced as independent MCP commands in the first implementation. Tool results should report created sheet/view ids and warnings after the operation completes.

### Module 6 - Optional Structural Precast CAM Export

Write tools:

- `create_precast_cam_export`
- `create_precast_cam_export_batch`

Read-only tools:

- `get_precast_cam_option_schema`
- `get_precast_cam_reinforcement_preview`

These tools require the optional `Autodesk.Precast.API.dll`. `TargetDirectory` is a filesystem write path and must be explicit in the input. The tool should validate that the directory exists or can be created, and return a clear permission error if not.

Supported options map to `ICamExportOptions`:

- file format
- overwrite mode
- multiple elements in one file
- subdirectory per product type
- export reinforcement information only
- target directory

## Input Contracts

Element ids are numeric ids. Coordinates and distances are millimeters in MCP inputs unless a field explicitly says otherwise. Revit internal feet are never exposed as the primary user-facing unit.

Assembly creation:

```json
{
  "elementIds": [12345, 12346],
  "namingCategoryId": 2000011,
  "assemblyTypeName": "PC-WALL-A01",
  "dryRun": true
}
```

Assembly view creation:

```json
{
  "assemblyId": 12345,
  "views": [
    {"type": "3d_orthographic", "viewTemplateId": 456},
    {"type": "detail_section", "orientation": "ElevationFront", "viewTemplateId": 457},
    {"type": "part_list", "viewTemplateId": 458}
  ],
  "dryRun": true
}
```

Part division:

```json
{
  "partIds": [1001, 1002],
  "intersectingElementIds": [2001, 2002],
  "curves": [
    {"type": "line", "start": {"x": 0, "y": 0, "z": 0}, "end": {"x": 5000, "y": 0, "z": 0}}
  ],
  "dryRun": true
}
```

CAM export:

```json
{
  "assemblyIds": [12345],
  "targetDirectory": "C:\\Exports\\Precast",
  "fileFormat": "Unitechnik",
  "overwriteMode": "Overwrite",
  "multipleElementsInOneFile": false,
  "subdirectoryPerProductType": true,
  "dryRun": true
}
```

## Result Contracts

Creation and mutation tools return:

- created or modified element ids
- resolved assembly/type/category ids and names
- created view, schedule, sheet, and export ids/paths where applicable
- part association summaries
- generated reinforcement ids when the optional API reports them
- warnings
- `optionalApiAvailability`
- `versionLimitations` when a request was degraded or blocked by target version

Read tools return:

- compact summaries first
- optional detailed arrays capped by explicit `max*` parameters
- all distances in millimeters
- enum names as strings
- optional API availability and loaded DLL path when relevant

Failures use existing `CortexErrorCode` values:

- `InvalidInput` for bad ids, unsupported enum values, invalid assembly members, invalid part division data, unavailable optional API, missing configuration, or invalid target directory
- `ElementNotFound` for missing elements, assemblies, parts, view templates, title blocks, schedules, or type ids
- `PermissionDenied` for read-only mode, filesystem export denial, or unsafe optional API loading
- `TransactionFailed` for failed Revit transactions
- `Cancelled` when the user cancels the confirmation dialog
- `Unknown` for unexpected Revit or Precast API exceptions after contextual details are captured

## Safety and Read-Only Mode

Read-only tools must use allowed prefixes: `get_`, `list_`, `analyze_`, `check_`, or `find_`.

Write tools must not use read-only prefixes. They must:

- validate all ids before opening a transaction
- support `dryRun: true` for bulk or destructive changes where preview is meaningful
- request confirmation before model writes
- return counts and summaries rather than huge per-element payloads by default
- validate export directories before CAM export
- report optional Precast DLL path and availability without loading arbitrary user-provided assemblies

Destructive operations include disassembling assemblies, removing assembly members, dividing/merging parts, changing part source ids, excluding parts, resetting part shape/offsets, creating reinforcement, generating shop drawings, and exporting files.

## Dynamic Capabilities

Most core precast tools can be registered as always-on because they return useful errors if a project has no precast setup. Dynamic capabilities are useful for version and installation hints:

- `hasPrecastAssemblies`
- `hasParts`
- `hasPartMakers`
- `hasPrecastAssemblyViews`
- `supportsAssemblyViews`
- `supportsPartDivision`
- `supportsOptionalPrecastApi`
- `supportsPrecastReinforcement`
- `supportsPrecastShopDrawings`
- `supportsPrecastCamExport`
- `supportsApplicationIsPrecastEnabled`

The MCP-visible tool list should not explode or disappear unpredictably. Prefer stable tool availability plus `get_precast_api_capabilities` for exact availability.

## Testing Strategy

Tests that do not require Revit:

- tool registration uniqueness and snake_case names
- server wrapper method signatures in `PrecastTools.cs`
- read-only naming conventions
- JSON input parser tests for assembly creation, assembly view specs, part division specs, face offset specs, reinforcement options, shop drawing options, and CAM options
- optional adapter tests for missing DLL, missing type, missing method, and schema generation
- version-guard tests for `Application.IsPrecastEnabled`
- helper tests for unit conversion, id conversion, path validation, and enum parsing

Build verification:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
node server/generate-tool-schemas-csharp.mjs
```

Manual Revit smoke tests:

- open a structural/precast sample with walls or slabs
- create parts from eligible walls/floors and read associated parts
- divide parts with a curve and with intersecting grid/reference elements
- merge compatible parts and inspect mergeable clusters
- create an assembly and verify `IsPrecastAssembly` where applicable
- create orthographic 3D, detail, part list, material takeoff, schedule, and sheet views for an assembly
- on a Revit version with `Autodesk.Precast.API.dll`, create wall or slab reinforcement using default/user-configuration options
- create a shop drawing for a valid precast assembly
- export a CAM file to an explicit test directory
- verify read-only mode blocks write tools
- verify cancelled TaskDialog returns `Cancelled`

## Documentation Updates

Update:

- `docs/USER_GUIDE.md` with a Precast section and examples.
- `tool-schemas.txt` after every schema change.
- `WORKFLOWS.md` with practical precast workflows:
  - discover optional Precast API availability
  - create parts and divide them
  - create a precast assembly
  - generate assembly documentation
  - create Precast API reinforcement when the DLL is available
  - create shop drawings
  - export CAM files
  - handle missing optional Precast API gracefully

Add an operational warning to `WORKFLOWS.md`: core Parts/Assemblies tools are always RevitAPI-based, but reinforcement, shop drawing, and CAM automation require the Autodesk Structural Precast add-in DLL for the active Revit version.

## Implementation Order

Implementation should be delivered in separate, buildable steps:

1. Shared helpers plus core capability/discovery tools.
2. Assembly read/write tools.
3. Assembly view, sheet, and schedule tools.
4. Part creation, division, merge, and offset tools.
5. Optional Precast add-in adapter with capability reporting.
6. Optional reinforcement tools.
7. Optional shop drawing tools.
8. Optional CAM export tools.
9. Documentation, schema regeneration, and full verification.

Each step should compile and test before the next step begins.

## Open Risks

- `Autodesk.Precast.API.dll` is not present for every installed Revit version on this machine and is not a NuGet package dependency in the repo.
- Reflection mapping for optional API options is more brittle than a static reference and must be covered by adapter tests.
- Precast reinforcement, shop drawing, and CAM workflows depend on local Precast configuration, content libraries, templates, families, and product rules.
- Shop drawing and CAM operations can be long-running and may create many views, sheets, and files.
- Precast API events are useful for deep customization but are not suitable as standalone MCP commands in the first implementation.
- Part face references are difficult to serialize safely. Public contracts must use supported face descriptors instead of arbitrary raw references.
- Some assembly and part operations require document regeneration between transactions; tools must commit/regenerate at documented boundaries.

## Acceptance Criteria

The work is complete when:

- Every tool listed in this design is implemented or intentionally version-gated with a clear structured response.
- R25 and R24 plugin builds pass.
- Server build passes.
- Unit tests pass.
- `tool-schemas.txt` is regenerated.
- `docs/USER_GUIDE.md` and `WORKFLOWS.md` document the new workflows.
- Manual smoke tests cover at least one successful core assembly operation, assembly view creation, part creation/division operation, and optional Precast API operation on a Revit version where the DLL is installed.

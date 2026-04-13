import { z } from "zod";

// ── ifc_get_capabilities ──
export const IfcGetCapabilitiesInput = z.object({});

// ── ifc_validate_request ──
export const IfcValidateRequestInput = z.object({
  filePath: z.string().describe("Full path to the IFC file to validate"),
});

// ── ifc_link ──
export const IfcLinkInput = z.object({
  ifcFilePath: z
    .string()
    .describe("Full path to the IFC file to link"),
  revitFilePath: z
    .string()
    .optional()
    .describe(
      "Full path for the intermediate .ifc.RVT file. If omitted, defaults to <ifcFilePath>.RVT"
    ),
  recreateLink: z
    .boolean()
    .optional()
    .default(true)
    .describe("Whether to recreate the intermediate Revit file from IFC. Default: true"),
});

// ── ifc_reload_link ──
export const IfcReloadLinkInput = z.object({
  linkTypeId: z
    .number()
    .describe("The RevitLinkType element ID of the existing IFC link"),
  newIfcFilePath: z
    .string()
    .optional()
    .describe("New IFC file path. If omitted, reloads from the original path"),
  recreateLink: z
    .boolean()
    .optional()
    .default(true)
    .describe("Whether to recreate the intermediate Revit file from IFC. Default: true"),
});

// ── ifc_open_or_import ──
export const IfcOpenOrImportInput = z.object({
  filePath: z.string().describe("Full path to the IFC file"),
  action: z
    .enum(["open", "link"])
    .optional()
    .default("open")
    .describe("'open' creates a new Revit document from IFC; 'link' creates a reference link"),
  intent: z
    .enum(["reference", "parametric"])
    .optional()
    .default("reference")
    .describe(
      "'reference' imports as lightweight reference geometry; 'parametric' imports as editable Revit elements"
    ),
  forceImport: z
    .boolean()
    .optional()
    .default(false)
    .describe("Force re-import even if a corresponding Revit file already exists"),
  autoJoin: z
    .boolean()
    .optional()
    .default(true)
    .describe("Enable auto-join at end of import"),
});

// ── ifc_export_basic ──
export const IfcExportBasicInput = z.object({
  outputDirectory: z
    .string()
    .describe("Directory for the exported IFC file. Must exist."),
  fileName: z
    .string()
    .optional()
    .default("")
    .describe("Output file name without extension. Empty = auto-name from document title"),
  fileVersion: z
    .enum([
      "Default",
      "IFC2x2",
      "IFC2x3",
      "IFC2x3CV2",
      "IFC4",
      "IFC4RV",
      "IFC4DTV",
      "IFC4x3",
    ])
    .optional()
    .default("IFC4RV")
    .describe("IFC version to export. Default: IFC4RV (IFC4 Reference View)"),
  filterViewId: z
    .number()
    .optional()
    .describe("View element ID whose visibility settings govern export. Omit to export all."),
  exportBaseQuantities: z
    .boolean()
    .optional()
    .default(false)
    .describe("Export IFC base quantities"),
  wallAndColumnSplitting: z
    .boolean()
    .optional()
    .default(false)
    .describe("Split multi-level walls and columns by level"),
  spaceBoundaryLevel: z
    .number()
    .optional()
    .default(0)
    .describe("Space boundary export level: 0, 1, or 2"),
});

// ── ifc_export_with_configuration ──
export const IfcExportWithConfigurationInput = z.object({
  outputDirectory: z
    .string()
    .describe("Directory for the exported IFC file. Must exist."),
  fileName: z
    .string()
    .optional()
    .default("")
    .describe("Output file name without extension. Empty = auto-name"),
  configurationName: z
    .string()
    .describe(
      "Name of a built-in or custom export configuration (see ifc_list_export_configurations)"
    ),
  filterViewId: z
    .number()
    .optional()
    .describe("View element ID whose visibility settings govern export"),
  overrides: z
    .record(z.string())
    .optional()
    .describe(
      "Key-value overrides applied via AddOption(). E.g. {\"ExportRoomsInView\": \"true\"}"
    ),
});

// ── ifc_list_export_configurations ──
export const IfcListExportConfigurationsInput = z.object({});

// ── ifc_get_export_configuration ──
export const IfcGetExportConfigurationInput = z.object({
  configurationName: z
    .string()
    .describe("Name of the export configuration to retrieve"),
});

// ── ifc_set_family_mapping_file ──
export const IfcSetFamilyMappingFileInput = z.object({
  filePath: z
    .string()
    .describe("Full path to the family mapping file (.txt). Set empty string to clear."),
});

// ══════════════════════════════════════════════════════════════
// Step 2 — IFC Native Reconstruction
// ══════════════════════════════════════════════════════════════

// ── ifc_analyze_rebuildability ──
export const IfcAnalyzeRebuildabilityInput = z.object({
  categoryFilter: z
    .string()
    .optional()
    .describe(
      "OST category code to filter (e.g. 'OST_Walls'). Omit to analyze all IFC elements."
    ),
  maxElements: z
    .number()
    .optional()
    .default(200)
    .describe("Max elements to analyze. Default: 200"),
});

// ── ifc_list_rebuild_candidates ──
export const IfcListRebuildCandidatesInput = z.object({
  categoryFilter: z
    .string()
    .optional()
    .describe("OST category code to filter. Omit for all categories."),
  minConfidence: z
    .number()
    .optional()
    .default(0.5)
    .describe("Minimum rebuild confidence threshold (0.0-1.0). Default: 0.5"),
  maxElements: z
    .number()
    .optional()
    .default(100)
    .describe("Max candidates to return. Default: 100"),
});

// ── ifc_rebuild_walls ──
export const IfcRebuildWallsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all wall candidates."),
  wallTypeId: z
    .number()
    .optional()
    .describe("WallType element ID to use. Omit to use the closest matching type by thickness."),
  structural: z
    .boolean()
    .optional()
    .default(false)
    .describe("Mark rebuilt walls as structural. Default: false"),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_floors ──
export const IfcRebuildFloorsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all floor candidates."),
  floorTypeId: z
    .number()
    .optional()
    .describe("FloorType element ID to use. Omit to use the default floor type."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_roofs ──
export const IfcRebuildRoofsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all roof candidates."),
  roofTypeId: z
    .number()
    .optional()
    .describe("RoofType element ID to use. Omit to use the default roof type."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_structural_members ──
export const IfcRebuildStructuralMembersInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild. Omit to rebuild all structural candidates."),
  memberType: z
    .enum(["columns", "beams", "all"])
    .optional()
    .default("all")
    .describe("Which structural member type to rebuild. Default: all"),
  familySymbolId: z
    .number()
    .optional()
    .describe("FamilySymbol element ID to use. Omit to auto-select by cross-section."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_openings ──
export const IfcRebuildOpeningsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs representing openings. Omit to auto-detect."),
  hostElementIds: z
    .array(z.number())
    .optional()
    .describe("Host wall/floor element IDs to search for openings. Omit to search all rebuilt elements."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_rebuild_family_instances ──
export const IfcRebuildFamilyInstancesInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific DirectShape element IDs to rebuild as family instances."),
  categoryFilter: z
    .enum(["OST_Doors", "OST_Windows", "OST_GenericModel"])
    .optional()
    .describe("Category to filter. Omit to rebuild doors and windows."),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview only, no changes made. Default: true"),
});

// ── ifc_compare_original_vs_rebuilt ──
export const IfcCompareOriginalVsRebuiltInput = z.object({
  originalElementId: z
    .number()
    .describe("Element ID of the original DirectShape (IFC import)"),
  rebuiltElementId: z
    .number()
    .describe("Element ID of the rebuilt native Revit element"),
});

// ── ifc_tag_unreconstructable_elements ──
export const IfcTagUnreconstructableElementsInput = z.object({
  elementIds: z
    .array(z.number())
    .optional()
    .describe("Specific element IDs to tag. Omit to tag all elements that failed rebuild analysis."),
  tagValue: z
    .string()
    .optional()
    .default("IFC_UNRECONSTRUCTABLE")
    .describe("Value to set in the Comments parameter. Default: IFC_UNRECONSTRUCTABLE"),
});

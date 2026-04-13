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

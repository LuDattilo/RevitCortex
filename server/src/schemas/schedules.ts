import { z } from "zod";

export const ModifyScheduleInput = z.object({
  scheduleId: z.number().optional().describe("Schedule element ID"),
  scheduleName: z.string().optional().describe("Schedule name (alternative to ID)"),
  action: z.enum(["add_field", "remove_field", "set_sorting", "clear_sorting", "rename"]).describe("Action"),
  fieldNames: z.array(z.string()).optional().describe("Field names (for add_field/remove_field)"),
  sortFields: z.array(z.object({
    fieldName: z.string().describe("Field name to sort by"),
    sortOrder: z.enum(["ascending", "descending"]).optional().default("ascending"),
  })).optional().describe("Sort fields (for set_sorting)"),
  newName: z.string().optional().describe("New name (for rename)"),
});

export const CreatePresetScheduleInput = z.object({
  preset: z.enum(["door_by_room", "window_by_room", "room_finish", "material_takeoff", "sheet_list", "view_list"]).describe("Schedule preset type"),
  name: z.string().optional().describe("Custom schedule name"),
  categoryName: z.string().optional().describe("Category for material_takeoff (OST_* or display name)"),
});

export const ExportFamiliesInput = z.object({
  outputDirectory: z.string().describe("Directory to export .rfa files to"),
  categories: z.array(z.string()).optional().describe("Category filter"),
  groupByCategory: z.boolean().optional().default(true).describe("Create subfolders by category"),
  overwrite: z.boolean().optional().default(false).describe("Overwrite existing files"),
});

export const ExportSharedParameterFileInput = z.object({
  filePath: z.string().optional().describe("Export path (omit for JSON response only)"),
});

export const CreateStructuralFramingSystemInput = z.object({
  levelName: z.string().describe("Level name for beam placement"),
  xMin: z.number().optional().default(0).describe("Boundary X min in mm"),
  xMax: z.number().optional().default(10000).describe("Boundary X max in mm"),
  yMin: z.number().optional().default(0).describe("Boundary Y min in mm"),
  yMax: z.number().optional().default(10000).describe("Boundary Y max in mm"),
  spacing: z.number().optional().default(1000).describe("Beam spacing in mm"),
  beamTypeName: z.string().optional().describe("Beam family type name"),
  elevation: z.number().optional().default(0).describe("Elevation offset from level in mm"),
});

export const SyncCsvParametersInput = z.object({
  data: z.array(z.object({
    elementId: z.number().describe("Element ID"),
    parameters: z.record(z.string()).describe("Parameter name-value pairs"),
  })).min(1).describe("Data rows to sync"),
  dryRun: z.boolean().optional().default(true).describe("Preview without applying"),
});

export const BatchExportInput = z.object({
  format: z.enum(["DWG", "DXF", "DGN", "IMAGE"]).optional().default("DWG").describe("Export format"),
  sheetIds: z.array(z.number()).optional().describe("Sheet IDs to export"),
  viewIds: z.array(z.number()).optional().describe("View IDs to export"),
  outputDirectory: z.string().optional().describe("Output directory (defaults to Desktop/RevitExport)"),
});

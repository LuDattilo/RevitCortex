import { z } from "zod";

export const BulkModifyParameterValuesInput = z.object({
  elementIds: z.array(z.number()).optional().describe("Element IDs to modify"),
  categoryName: z.string().optional().describe("Category name (OST_* or display name)"),
  parameterName: z.string().describe("Parameter name to modify"),
  operation: z.enum(["set", "prefix", "suffix", "find_replace", "clear"]).optional().default("set").describe("Operation type"),
  value: z.string().optional().describe("Value for set/prefix/suffix operations"),
  findText: z.string().optional().describe("Text to find (for find_replace)"),
  replaceText: z.string().optional().describe("Replacement text (for find_replace)"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
  onlyEmpty: z.boolean().optional().default(false).describe("Only modify empty values"),
});

export const ClearParameterValuesInput = z.object({
  parameterName: z.string().describe("Parameter name to clear"),
  categories: z.array(z.string()).optional().describe("Category filter (OST_* or display name)"),
  scope: z.enum(["whole_model", "active_view", "selection"]).optional().default("whole_model").describe("Scope"),
  filterValue: z.string().optional().describe("Only clear if current value matches"),
  parameterType: z.enum(["instance", "type"]).optional().default("instance").describe("Parameter type"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const TransferParametersInput = z.object({
  sourceElementId: z.number().describe("Source element ID"),
  targetElementIds: z.array(z.number()).min(1).describe("Target element IDs"),
  parameterNames: z.array(z.string()).optional().describe("Specific parameters to transfer (all if omitted)"),
  includeType: z.boolean().optional().default(false).describe("Include type parameters"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const SetMaterialPropertiesInput = z.object({
  requests: z.array(z.object({
    materialId: z.number().describe("Material element ID"),
    name: z.string().optional().describe("New material name"),
    description: z.string().optional().describe("Description"),
    manufacturer: z.string().optional().describe("Manufacturer"),
    model: z.string().optional().describe("Model"),
    url: z.string().optional().describe("URL"),
    cost: z.string().optional().describe("Cost"),
    mark: z.string().optional().describe("Mark"),
    keynote: z.string().optional().describe("Keynote"),
    comments: z.string().optional().describe("Comments"),
  })).min(1).describe("Material property updates"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const BatchRenameInput = z.object({
  elementIds: z.array(z.number()).optional().describe("Specific element IDs to rename (works for any element including system types)"),
  targetCategory: z.enum(["Views", "Sheets", "Levels", "Grids", "Rooms", "WallTypes", "FloorTypes", "CeilingTypes", "RoofTypes"]).optional().describe("Target category to rename"),
  findText: z.string().optional().describe("Text to find"),
  replaceText: z.string().optional().describe("Replacement text"),
  prefix: z.string().optional().describe("Prefix to add"),
  suffix: z.string().optional().describe("Suffix to add"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const LoadFamilyInput = z.object({
  action: z.enum(["load", "list", "duplicate_type"]).optional().default("list").describe("Action to perform"),
  familyPath: z.string().optional().describe("Path to .rfa file (for load)"),
  categoryFilter: z.string().optional().describe("Category filter (for list)"),
  sourceTypeId: z.number().optional().describe("Source type ID (for duplicate_type)"),
  newTypeName: z.string().optional().describe("New type name (for duplicate_type)"),
});

export const RenameFamiliesInput = z.object({
  operation: z.enum(["prefix", "suffix", "find_replace"]).describe("Rename operation"),
  prefix: z.string().optional().describe("Prefix to add"),
  suffix: z.string().optional().describe("Suffix to add"),
  findText: z.string().optional().describe("Text to find"),
  replaceText: z.string().optional().describe("Replacement text"),
  categories: z.array(z.string()).optional().describe("Category filter"),
  renameTypes: z.boolean().optional().default(false).describe("Also rename family types"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const RenameViewsInput = z.object({
  operation: z.enum(["prefix", "suffix", "find_replace"]).describe("Rename operation"),
  prefix: z.string().optional().describe("Prefix to add"),
  suffix: z.string().optional().describe("Suffix to add"),
  findText: z.string().optional().describe("Text to find"),
  replaceText: z.string().optional().describe("Replacement text"),
  viewTypes: z.array(z.string()).optional().describe("Filter by view types"),
  filterName: z.string().optional().describe("Filter by name substring"),
  dryRun: z.boolean().optional().default(true).describe("Preview changes without applying"),
});

export const ManageLinksInput = z.object({
  action: z.enum(["list", "reload", "unload"]).optional().default("list").describe("Action to perform"),
  linkId: z.number().optional().describe("Link element ID (for reload/unload)"),
});

export const SendCodeToRevitInput = z.object({
  code: z.string().describe("C# code to execute (auto-imports System, Linq, Revit.DB)"),
  transactionMode: z.enum(["auto", "none"]).optional().default("auto").describe("Transaction wrapping mode"),
});

export const WipeEmptyTagsInput = z.object({
  dryRun: z.boolean().optional().default(true).describe("Preview without deleting"),
  viewId: z.number().optional().describe("Scope to specific view"),
  categories: z.array(z.string()).optional().describe("Category filter"),
});

import { z } from "zod";

export const AnalyzeModelStatisticsInput = z.object({
  includeDetailedTypes: z.boolean().optional().default(false).describe("Include type breakdown by category"),
  compact: z.boolean().optional().default(false).describe("Limit category breakdown to top 20"),
});

export const CheckModelHealthInput = z.object({});

export const AuditFamiliesInput = z.object({
  includeUnused: z.boolean().optional().default(true).describe("Include unused families"),
  categoryFilter: z.string().optional().describe("Filter by category (OST_* or display name)"),
  sortBy: z.enum(["name", "instance_count"]).optional().default("instance_count").describe("Sort order"),
});

export const PurgeUnusedInput = z.object({
  dryRun: z.boolean().optional().default(true).describe("Preview without deleting"),
  maxElements: z.number().int().optional().default(500).describe("Maximum elements to process"),
});

export const CadLinkCleanupInput = z.object({
  action: z.enum(["list", "delete"]).optional().default("list").describe("List or delete CAD imports/links"),
  deleteImports: z.boolean().optional().default(false).describe("Delete imported CADs"),
  deleteLinks: z.boolean().optional().default(false).describe("Delete linked CADs"),
  elementIds: z.array(z.number()).optional().describe("Specific element IDs to delete"),
});

export const ClashDetectionInput = z.object({
  categoryA: z.string().optional().describe("Category A (OST_* or display name)"),
  categoryB: z.string().optional().describe("Category B (OST_* or display name)"),
  elementIdsA: z.array(z.number()).optional().describe("Element IDs for set A"),
  elementIdsB: z.array(z.number()).optional().describe("Element IDs for set B"),
  tolerance: z.number().optional().default(0).describe("Clash tolerance in mm"),
  maxResults: z.number().int().optional().default(100).describe("Maximum clash results"),
});

export const ExportRoomDataInput = z.object({
  includeUnplacedRooms: z.boolean().optional().default(false).describe("Include unplaced rooms"),
  includeNotEnclosedRooms: z.boolean().optional().default(false).describe("Include not-enclosed rooms"),
  maxResults: z.number().int().optional().default(100).describe("Maximum rooms to return"),
});

export const ExportScheduleInput = z.object({
  scheduleId: z.number().describe("Schedule element ID"),
  exportPath: z.string().optional().describe("File path to export to (omit for JSON response)"),
  delimiter: z.enum(["Tab", "Comma", "Semicolon", "Space"]).optional().default("Tab").describe("Delimiter for export"),
  includeHeaders: z.boolean().optional().default(true).describe("Include column headers"),
});

export const DeleteScheduleInput = z.object({
  scheduleId: z.number().optional().describe("Schedule element ID"),
  scheduleName: z.string().optional().describe("Schedule name (alternative to ID)"),
  confirm: z.boolean().describe("Must be true to delete"),
});

export const DuplicateScheduleInput = z.object({
  scheduleId: z.number().optional().describe("Schedule element ID"),
  scheduleName: z.string().optional().describe("Schedule name (alternative to ID)"),
  newName: z.string().describe("Name for the duplicated schedule"),
});

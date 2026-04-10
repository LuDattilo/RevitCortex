import { z } from "zod";

// Excel tools
export const ExportToExcelInput = z.object({
  categories: z.array(z.string()).optional().describe("Category filter (OST_* or display name). Omit for all."),
  parameterNames: z.array(z.string()).optional().describe("Specific parameter names to include"),
  includeTypeParameters: z.boolean().optional().default(false).describe("Include type parameters"),
  includeElementId: z.boolean().optional().default(true).describe("Include ElementId column"),
  filePath: z.string().optional().describe("Output .xlsx path (defaults to Desktop)"),
  sheetName: z.string().optional().default("Export").describe("Excel sheet name"),
  maxElements: z.number().optional().default(10000).describe("Max elements to export"),
});

export const ImportFromExcelInput = z.object({
  filePath: z.string().describe("Path to .xlsx file with ElementId column"),
  sheetName: z.string().optional().describe("Sheet name (defaults to first sheet)"),
  dryRun: z.boolean().optional().default(true).describe("Preview without applying changes"),
});

// Workflow tools
export const WorkflowClashReviewInput = z.object({
  categoryA: z.string().describe("First category (OST_* or display name)"),
  categoryB: z.string().describe("Second category (OST_* or display name)"),
  tolerance: z.number().optional().default(0).describe("Tolerance in mm"),
  createSectionBox: z.boolean().optional().default(true).describe("Create 3D section box view for clashes"),
});

export const WorkflowRoomDocumentationInput = z.object({
  levelName: z.string().optional().describe("Level filter (omit for all levels)"),
  createSections: z.boolean().optional().default(true).describe("Create N/S section views per room"),
  offset: z.number().optional().default(300).describe("View boundary offset in mm"),
});

export const WorkflowSheetSetInput = z.object({
  sheets: z.array(z.object({
    number: z.string().optional().describe("Sheet number"),
    name: z.string().optional().describe("Sheet name"),
  })).min(1).describe("Sheet definitions"),
  titleBlockName: z.string().optional().describe("Title block family or type name"),
});

export const WorkflowModelAuditInput = z.object({
  includeWarnings: z.boolean().optional().default(true).describe("Include warning details"),
  includeFamilies: z.boolean().optional().default(true).describe("Include family analysis"),
  maxWarnings: z.number().optional().default(50).describe("Max warning types to return"),
});

export const WorkflowDataRoundtripInput = z.object({
  categories: z.array(z.string()).min(1).describe("Categories to export (OST_* or display name)"),
  parameterNames: z.array(z.string()).optional().describe("Specific parameter names to include"),
  includeTypeParameters: z.boolean().optional().default(false).describe("Include type parameters"),
  filePath: z.string().optional().describe("Output .xlsx path (defaults to Desktop)"),
});

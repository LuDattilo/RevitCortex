import { z } from "zod";

export const AlignViewportsInput = z.object({
  sourceViewportId: z.number().describe("Source viewport ID to align to"),
  targetViewportIds: z.array(z.number()).min(1).describe("Target viewport IDs to align"),
  alignMode: z.enum(["placement", "coordinates"]).optional().default("placement").describe("Alignment mode"),
});

export const BatchCreateSheetsInput = z.object({
  sheets: z.array(z.object({
    number: z.string().optional().describe("Sheet number (e.g. A-101)"),
    name: z.string().optional().describe("Sheet name"),
    titleBlockName: z.string().optional().describe("Title block family type name"),
    viewIds: z.array(z.number()).optional().describe("View IDs to place on this sheet"),
  })).min(1).describe("Sheet definitions to create"),
  defaultTitleBlockName: z.string().optional().describe("Default title block for sheets without one specified"),
});

export const CreatePlaceholderSheetsInput = z.object({
  action: z.enum(["create", "list", "convert", "delete"]).describe("Action to perform"),
  sheets: z.array(z.object({
    number: z.string().optional().describe("Sheet number"),
    name: z.string().optional().describe("Sheet name"),
  })).optional().describe("Sheet definitions for create action"),
  sheetIds: z.array(z.number()).optional().describe("Sheet IDs for convert/delete actions"),
  titleBlockId: z.number().optional().describe("Title block type ID for convert action"),
});

export const DuplicateSheetWithContentInput = z.object({
  sheetId: z.number().describe("Source sheet element ID"),
  copies: z.number().int().optional().default(1).describe("Number of copies"),
  duplicateViews: z.boolean().optional().default(true).describe("Duplicate placed views with detailing"),
  keepLegends: z.boolean().optional().default(true).describe("Place legends on new sheets"),
  keepSchedules: z.boolean().optional().default(true).describe("Place schedules on new sheets"),
  copyRevisions: z.boolean().optional().default(false).describe("Copy revision assignments"),
  sheetNumberPrefix: z.string().optional().describe("Prefix for new sheet numbers"),
  sheetNumberSuffix: z.string().optional().describe("Suffix for new sheet numbers"),
});

export const DuplicateSheetWithViewsInput = z.object({
  sheetId: z.number().describe("Source sheet element ID"),
  copies: z.number().int().optional().default(1).describe("Number of copies"),
  duplicateViews: z.boolean().optional().default(true).describe("Duplicate placed views"),
  keepLegends: z.boolean().optional().default(true).describe("Keep legend views on new sheets"),
  keepSchedules: z.boolean().optional().default(true).describe("Keep schedules on new sheets"),
  newSheetNumberPrefix: z.string().optional().describe("Prefix for new sheet numbers"),
  viewDuplicateOption: z.enum(["Duplicate", "DuplicateWithDetailing", "DuplicateAsDependent"]).optional().default("DuplicateWithDetailing").describe("View duplication option"),
});

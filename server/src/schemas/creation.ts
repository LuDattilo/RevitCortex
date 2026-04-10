import { z } from "zod";

const PointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().optional().describe("Z coordinate in mm"),
});

export const CreateFloorInput = z.object({
  boundaryPoints: z.array(PointSchema).optional().describe("Boundary points in mm (min 3)"),
  roomId: z.number().optional().describe("Room ID to derive boundary from"),
  floorTypeName: z.string().optional().describe("Floor type name"),
  levelElevation: z.number().optional().describe("Level elevation in mm"),
  isStructural: z.boolean().optional().default(false).describe("Is structural floor"),
});

export const CreateGridInput = z.object({
  xCount: z.number().int().optional().default(0).describe("Number of X-axis grids (vertical)"),
  yCount: z.number().int().optional().default(0).describe("Number of Y-axis grids (horizontal)"),
  xSpacing: z.number().optional().default(5000).describe("X grid spacing in mm"),
  ySpacing: z.number().optional().default(5000).describe("Y grid spacing in mm"),
  xStartLabel: z.string().optional().default("A").describe("Starting label for X grids"),
  yStartLabel: z.string().optional().default("1").describe("Starting label for Y grids"),
  xNamingStyle: z.enum(["alphabetic", "numeric"]).optional().default("alphabetic"),
  yNamingStyle: z.enum(["alphabetic", "numeric"]).optional().default("numeric"),
  elevation: z.number().optional().default(0).describe("Grid elevation in mm"),
  xExtentMin: z.number().optional().describe("X grid extent min Y in mm"),
  xExtentMax: z.number().optional().describe("X grid extent max Y in mm"),
  yExtentMin: z.number().optional().describe("Y grid extent min X in mm"),
  yExtentMax: z.number().optional().describe("Y grid extent max X in mm"),
});

export const CreateLevelInput = z.object({
  name: z.string().describe("Level name"),
  elevation: z.number().describe("Elevation in mm"),
  isBuildingStory: z.boolean().optional().default(true).describe("Mark as building story"),
  createFloorPlan: z.boolean().optional().default(false).describe("Create associated floor plan"),
  createCeilingPlan: z.boolean().optional().default(false).describe("Create associated ceiling plan"),
});

export const CreateRoomInput = z.object({
  name: z.string().optional().describe("Room name"),
  location: z.object({
    x: z.number().describe("X in mm"),
    y: z.number().describe("Y in mm"),
    z: z.number().optional().default(0).describe("Z in mm (used to find level)"),
  }).describe("Location point inside enclosed walls"),
  number: z.string().optional().describe("Room number"),
  levelId: z.number().optional().describe("Specific level ID"),
  department: z.string().optional().describe("Department name"),
  comments: z.string().optional().describe("Comments"),
  limitOffset: z.number().optional().default(0).describe("Upper limit offset in mm"),
  baseOffset: z.number().optional().default(0).describe("Base offset in mm"),
});

export const CreateArrayInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to copy"),
  arrayType: z.enum(["linear", "radial"]).optional().default("linear"),
  count: z.number().int().min(1).describe("Number of copies"),
  spacingX: z.number().optional().default(0).describe("X spacing in mm (linear)"),
  spacingY: z.number().optional().default(0).describe("Y spacing in mm (linear)"),
  spacingZ: z.number().optional().default(0).describe("Z spacing in mm (linear)"),
  centerX: z.number().optional().default(0).describe("Rotation center X in mm (radial)"),
  centerY: z.number().optional().default(0).describe("Rotation center Y in mm (radial)"),
  totalAngle: z.number().optional().default(360).describe("Total angle in degrees (radial)"),
});

export const CreateFilledRegionInput = z.object({
  boundaryPoints: z.array(PointSchema).min(3).describe("Boundary points in mm"),
  viewId: z.number().optional().describe("View ID (defaults to active view)"),
  filledRegionTypeName: z.string().optional().describe("Filled region type name"),
});

export const CreateScheduleInput = z.object({
  categoryName: z.string().optional().describe("Category (OST_* or display name)"),
  name: z.string().optional().default("New Schedule").describe("Schedule name"),
  scheduleType: z.enum(["regular", "material_takeoff", "key_schedule", "sheet_list", "view_list"]).optional().default("regular"),
  fields: z.array(z.object({
    parameterName: z.string().describe("Parameter name to add as field"),
    heading: z.string().optional().describe("Custom column heading"),
    isHidden: z.boolean().optional().default(false).describe("Hide this field"),
  })).optional().describe("Fields to add to schedule"),
});

export const CreateSheetInput = z.object({
  sheetNumber: z.string().optional().describe("Sheet number (e.g. A101)"),
  sheetName: z.string().optional().describe("Sheet name"),
  titleBlockFamilyName: z.string().optional().describe("Title block family name"),
  titleBlockTypeName: z.string().optional().describe("Title block type name"),
  titleBlockTypeId: z.number().optional().describe("Title block type ID"),
});

export const CreateRevisionInput = z.object({
  action: z.enum(["list", "create", "add_to_sheets"]).describe("Action to perform"),
  date: z.string().optional().describe("Revision date (for create)"),
  description: z.string().optional().describe("Description (for create)"),
  issuedBy: z.string().optional().describe("Issued by (for create)"),
  issuedTo: z.string().optional().describe("Issued to (for create)"),
  sheetIds: z.array(z.number()).optional().describe("Sheet IDs (for add_to_sheets)"),
  revisionId: z.number().optional().describe("Revision ID (for add_to_sheets, defaults to latest)"),
});

export const TagRoomsInput = z.object({
  useLeader: z.boolean().optional().default(false).describe("Add leader lines to tags"),
  roomIds: z.array(z.number()).optional().describe("Specific room IDs (omit for all rooms in view)"),
});

export const TagWallsInput = z.object({
  useLeader: z.boolean().optional().default(false).describe("Add leader lines to tags"),
});

export const SaveSelectionInput = z.object({
  name: z.string().describe("Name for the selection"),
  elementIds: z.array(z.number()).optional().describe("Element IDs (omit to use current Revit selection)"),
  overwrite: z.boolean().optional().default(false).describe("Replace existing selection with same name"),
});

export const LoadSelectionInput = z.object({
  name: z.string().optional().describe("Selection name (omit to list all saved selections)"),
  selectInView: z.boolean().optional().default(true).describe("Select elements in current view"),
});

export const DeleteSelectionInput = z.object({
  name: z.string().describe("Name of saved selection to delete"),
});

import { z } from "zod";

export const ApplyViewTemplateInput = z.object({
  action: z.enum(["list", "apply", "remove"]).optional().default("list").describe("Action to perform"),
  viewIds: z.array(z.number()).optional().describe("View IDs to apply/remove template"),
  templateId: z.number().optional().describe("Template ID to apply"),
  templateName: z.string().optional().describe("Template name to apply (alternative to templateId)"),
  filterViewType: z.string().optional().describe("Filter templates by view type when listing"),
});

export const BatchModifyViewRangeInput = z.object({
  viewIds: z.array(z.number()).min(1).describe("View IDs to modify"),
  topOffset: z.number().optional().describe("Top clip plane offset in mm"),
  cutPlaneOffset: z.number().optional().describe("Cut plane offset in mm"),
  bottomOffset: z.number().optional().describe("Bottom clip plane offset in mm"),
  depthOffset: z.number().optional().describe("View depth offset in mm"),
});

export const CreateViewInput = z.object({
  viewType: z.enum(["floorplan", "ceilingplan", "section", "3d"]).describe("Type of view to create"),
  levelId: z.number().optional().describe("Level ID (for plan views)"),
  levelName: z.string().optional().describe("Level name (alternative to levelId)"),
  name: z.string().optional().describe("View name"),
  scale: z.number().int().optional().default(100).describe("View scale"),
  sectionOriginX: z.number().optional().describe("Section origin X in mm"),
  sectionOriginY: z.number().optional().describe("Section origin Y in mm"),
  sectionOriginZ: z.number().optional().describe("Section origin Z in mm"),
  sectionDirection: z.enum(["north", "south", "east", "west"]).optional().default("north").describe("Section view direction"),
  sectionWidth: z.number().optional().default(10000).describe("Section width in mm"),
  sectionHeight: z.number().optional().default(5000).describe("Section height in mm"),
  sectionDepth: z.number().optional().default(5000).describe("Section depth in mm"),
});

export const DuplicateViewInput = z.object({
  viewIds: z.array(z.number()).min(1).describe("View IDs to duplicate"),
  duplicateOption: z.enum(["Duplicate", "WithDetailing", "AsDependent"]).optional().default("WithDetailing").describe("Duplication option"),
  namePrefix: z.string().optional().describe("Prefix for duplicated view names"),
  nameSuffix: z.string().optional().describe("Suffix for duplicated view names"),
});

export const CreateViewFilterInput = z.object({
  action: z.enum(["create", "apply", "list"]).optional().default("create").describe("Action to perform"),
  filterName: z.string().optional().describe("Filter name"),
  categoryNames: z.array(z.string()).optional().describe("Categories for the filter (OST_* or display name)"),
  rules: z.array(z.object({
    parameterName: z.string().describe("Parameter name"),
    evaluator: z.enum(["equals", "not_equals", "greater", "less", "contains", "not_contains", "starts_with", "ends_with"]).describe("Comparison type"),
    value: z.string().describe("Value to compare against"),
  })).optional().describe("Filter rules"),
  viewIds: z.array(z.number()).optional().describe("View IDs to apply filter to"),
  filterId: z.number().optional().describe("Existing filter ID to apply"),
  overrideColor: z.string().optional().describe("Override color hex (e.g. #FF0000)"),
  overrideTransparency: z.number().int().optional().describe("Override transparency 0-100"),
  overrideHalftone: z.boolean().optional().describe("Override halftone"),
  visible: z.boolean().optional().default(true).describe("Filter visibility"),
});

export const OverrideGraphicsInput = z.object({
  action: z.enum(["set", "reset"]).optional().default("set").describe("Set or reset overrides"),
  elementIds: z.array(z.number()).min(1).describe("Element IDs to override"),
  viewId: z.number().optional().describe("View ID (defaults to active view)"),
  projectionLineColor: z.string().optional().describe("Projection line color hex"),
  surfaceForegroundColor: z.string().optional().describe("Surface foreground color hex"),
  surfaceBackgroundColor: z.string().optional().describe("Surface background color hex"),
  transparency: z.number().int().optional().describe("Transparency 0-100"),
  halftone: z.boolean().optional().describe("Apply halftone"),
  projectionLineWeight: z.number().int().optional().describe("Projection line weight 1-16"),
});

export const PlaceViewportInput = z.object({
  sheetId: z.number().describe("Sheet element ID"),
  viewId: z.number().describe("View element ID to place"),
  positionX: z.number().optional().default(0).describe("X position on sheet in mm"),
  positionY: z.number().optional().default(0).describe("Y position on sheet in mm"),
});

export const SectionBoxFromSelectionInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to create section box from"),
  offset: z.number().optional().default(1000).describe("Offset around elements in mm"),
  duplicateView: z.boolean().optional().default(true).describe("Create new 3D view or modify active"),
  viewName: z.string().optional().describe("Name for the new 3D view"),
});

export const ManageUnplacedViewsInput = z.object({
  action: z.enum(["list", "delete"]).optional().default("list").describe("List or delete unplaced views"),
  viewTypes: z.array(z.string()).optional().describe("Filter by view types"),
  filterName: z.string().optional().describe("Filter by view name substring"),
  dryRun: z.boolean().optional().default(true).describe("Preview deletions without executing"),
  maxResults: z.number().int().optional().default(500).describe("Maximum results to return"),
});

export const ManageViewTemplatesInput = z.object({
  action: z.enum(["list", "duplicate", "delete", "rename"]).optional().default("list").describe("Action to perform"),
  filterViewType: z.string().optional().describe("Filter templates by view type when listing"),
  templateIds: z.array(z.number()).optional().describe("Template IDs for duplicate/delete"),
  templateId: z.number().optional().describe("Template ID for rename"),
  newName: z.string().optional().describe("New name for rename action"),
});

export const CreateViewsFromRoomsInput = z.object({
  roomIds: z.array(z.number()).min(1).describe("Room element IDs"),
  viewType: z.enum(["callout", "section", "elevation"]).optional().default("callout").describe("Type of view to create"),
  offset: z.number().optional().default(500).describe("Offset around room in mm"),
  scale: z.number().int().optional().default(50).describe("View scale"),
  namingPattern: z.string().optional().default("{RoomNumber} - {RoomName}").describe("View naming pattern"),
});

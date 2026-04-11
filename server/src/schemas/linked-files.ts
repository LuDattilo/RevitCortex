import { z } from "zod";

export const GetLinkedFileInstancesInput = z.object({
  linkName: z.string().optional().describe("Filter by link name (partial, case-insensitive)"),
});

export const GetLinkTransformInput = z.object({
  instanceId: z.number().describe("The RevitLinkInstance element ID"),
});

export const ReloadLinkedFileFromInput = z.object({
  linkTypeId: z.number().describe("The RevitLinkType element ID"),
  newPath: z.string().describe("New file path to reload the link from"),
});

export const AddLinkedFileInput = z.object({
  filePath: z.string().describe("Path to the Revit file to link"),
  positionX: z.number().optional().default(0).describe("X position in mm"),
  positionY: z.number().optional().default(0).describe("Y position in mm"),
  positionZ: z.number().optional().default(0).describe("Z position in mm"),
});

export const PinUnpinLinkInstanceInput = z.object({
  instanceIds: z.array(z.number()).describe("Array of RevitLinkInstance element IDs"),
  pin: z.boolean().optional().default(true).describe("True to pin, false to unpin"),
});

export const MoveLinkInstanceInput = z.object({
  instanceId: z.number().describe("The RevitLinkInstance element ID"),
  x: z.number().optional().default(0).describe("X offset/position in mm"),
  y: z.number().optional().default(0).describe("Y offset/position in mm"),
  z: z.number().optional().default(0).describe("Z offset/position in mm"),
  mode: z.enum(["delta", "absolute"]).optional().default("delta").describe("'delta' for relative move, 'absolute' for set position"),
});

export const AlignLinkToHostInput = z.object({
  instanceId: z.number().describe("The RevitLinkInstance element ID"),
  alignMode: z.enum(["origin", "shared"]).optional().default("origin").describe("'origin' aligns to internal origin, 'shared' aligns to shared coordinates"),
});

export const HighlightLinkedElementInput = z.object({
  instanceId: z.number().describe("The RevitLinkInstance element ID in the host document"),
  linkedElementId: z.number().describe("The element ID inside the linked document"),
  createSectionBox: z.boolean().optional().default(true).describe("Create a section box around the element"),
  offset: z.number().optional().default(1000).describe("Section box offset in mm"),
});

export const GetSelectedLinkedElementsInput = z.object({
  includeCategorySummary: z.boolean().optional().default(true).describe("Include element counts by category for each selected link"),
  maxCategories: z.number().optional().default(20).describe("Max categories to include in summary"),
});

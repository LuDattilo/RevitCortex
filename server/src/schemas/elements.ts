import { z } from "zod";

export const GetElementParametersInput = z.object({
  elementIds: z
    .array(z.number())
    .min(1)
    .describe("Array of Revit element IDs to query"),
  includeTypeParameters: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include type-level parameters. Default: true"),
});

export const AIElementFilterInput = z.object({
  data: z.object({
    filterCategory: z
      .string()
      .optional()
      .describe("BuiltInCategory code, e.g. OST_Walls, OST_Doors, OST_Rooms"),
    includeTypes: z
      .boolean()
      .optional()
      .default(false)
      .describe("Include type elements. Default: false"),
    includeInstances: z
      .boolean()
      .optional()
      .default(true)
      .describe("Include instance elements. Default: true"),
    maxElements: z
      .number()
      .int()
      .optional()
      .default(100)
      .describe("Max elements to return. Default: 100"),
  }),
});

export const SetElementParametersInput = z.object({
  requests: z
    .array(
      z.object({
        elementId: z.number().describe("Revit element ID"),
        parameterName: z.string().describe("Parameter name to set"),
        value: z
          .union([z.string(), z.number(), z.boolean()])
          .describe("Value to set"),
      })
    )
    .min(1)
    .describe("Array of parameter set requests"),
});

export const GetSelectedElementsInput = z.object({
  limit: z.number().int().optional().default(500).describe("Max elements to return. Default: 500"),
});

export const GetCurrentViewElementsInput = z.object({
  modelCategoryList: z.array(z.string()).optional().describe("Model categories (OST_*) to include"),
  annotationCategoryList: z.array(z.string()).optional().describe("Annotation categories to include"),
  includeHidden: z.boolean().optional().default(false).describe("Include hidden elements. Default: false"),
  limit: z.number().int().optional().default(500).describe("Max elements. Default: 500"),
  fields: z.array(z.string()).optional().describe("Specific parameter names to extract"),
});

export const GetLinkedElementsInput = z.object({
  linkName: z.string().optional().describe("Filter linked models by name (partial match)"),
  categories: z.array(z.string()).optional().describe("Category codes (OST_*) to filter"),
  parameterNames: z.array(z.string()).optional().describe("Parameter names to extract per element"),
  maxElements: z.number().int().optional().default(5000).describe("Max elements per link. Default: 5000"),
});

const PointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().describe("Z coordinate in mm"),
});

export const GetElementsInSpatialVolumeInput = z.object({
  volumeType: z.enum(["room", "area", "custom"]).optional().default("room").describe("Volume type"),
  volumeIds: z.array(z.number()).optional().describe("Specific room/area IDs to search"),
  categoryFilter: z.array(z.string()).optional().describe("Categories (OST_*) to filter"),
  maxElementsPerVolume: z.number().int().optional().default(100).describe("Max elements per volume"),
  customMinX: z.number().optional().describe("Custom bounding box min X (mm)"),
  customMinY: z.number().optional().describe("Custom bounding box min Y (mm)"),
  customMinZ: z.number().optional().describe("Custom bounding box min Z (mm)"),
  customMaxX: z.number().optional().describe("Custom bounding box max X (mm)"),
  customMaxY: z.number().optional().describe("Custom bounding box max Y (mm)"),
  customMaxZ: z.number().optional().describe("Custom bounding box max Z (mm)"),
});

export const DeleteElementInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to delete"),
  dryRun: z.boolean().optional().default(true).describe("Preview mode — true = show what would be deleted without deleting. Default: true"),
});

export const OperateElementInput = z.object({
  data: z.object({
    elementIds: z.array(z.number()).min(1).describe("Element IDs to operate on"),
    action: z.enum([
      "select", "selectionbox", "setcolor", "settransparency",
      "hide", "temphide", "isolate", "unhide", "resetisolate", "delete"
    ]).describe("Operation to perform"),
    colorValue: z.array(z.number().min(0).max(255)).length(3).optional().describe("RGB color [R,G,B] for setcolor"),
    transparencyValue: z.number().min(0).max(100).optional().describe("Transparency 0-100 for settransparency"),
  }),
});

export const ChangeElementTypeInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to change type"),
  targetTypeId: z.number().optional().describe("Target type element ID (preferred)"),
  targetTypeName: z.string().optional().describe("Target type name to search for"),
  targetFamilyName: z.string().optional().describe("Family name to narrow type search"),
});

export const ModifyElementInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to modify"),
  action: z.enum(["move", "rotate", "mirror", "copy"]).describe("Modification action"),
  translation: PointSchema.optional().describe("Translation vector for move (mm)"),
  rotationCenter: PointSchema.optional().describe("Rotation center point (mm)"),
  rotationAngle: z.number().optional().describe("Rotation angle in degrees"),
  mirrorPlaneOrigin: PointSchema.optional().describe("Mirror plane origin (mm)"),
  mirrorPlaneNormal: PointSchema.optional().describe("Mirror plane normal vector"),
  copyOffset: PointSchema.optional().describe("Copy offset vector (mm)"),
});

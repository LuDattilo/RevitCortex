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

export const CopyElementsInput = z.object({
  elementIds: z.array(z.number()).min(1).describe("Element IDs to copy"),
  sourceViewId: z.number().optional().describe("Source view ID for view-to-view copy"),
  targetViewId: z.number().optional().describe("Target view ID for view-to-view copy"),
  offsetX: z.number().optional().default(0).describe("X offset in mm"),
  offsetY: z.number().optional().default(0).describe("Y offset in mm"),
  offsetZ: z.number().optional().default(0).describe("Z offset in mm"),
});

export const MeasureBetweenElementsInput = z.object({
  elementId1: z.number().optional().describe("First element ID"),
  elementId2: z.number().optional().describe("Second element ID"),
  point1: PointSchema.optional().describe("First point {x,y,z} in mm (alternative to elementId1)"),
  point2: PointSchema.optional().describe("Second point {x,y,z} in mm (alternative to elementId2)"),
  measureType: z.enum(["center_to_center", "closest_points", "bounding_box"]).optional().default("center_to_center").describe("Measurement method"),
});

export const RenumberElementsInput = z.object({
  elementIds: z.array(z.number()).optional().describe("Specific elements to renumber (optional)"),
  targetCategory: z.enum(["Rooms", "Doors", "Windows", "Parking"]).describe("Category to renumber"),
  parameterName: z.string().optional().describe("Custom parameter name (overrides built-in)"),
  startNumber: z.number().int().optional().default(1).describe("Starting number"),
  increment: z.number().int().optional().default(1).describe("Increment between numbers"),
  prefix: z.string().optional().default("").describe("Prefix before number"),
  suffix: z.string().optional().default("").describe("Suffix after number"),
  sortBy: z.enum(["location", "name", "none"]).optional().default("location").describe("Sort order"),
  dryRun: z.boolean().optional().default(true).describe("Preview mode. Default: true"),
});

export const FindUntaggedElementsInput = z.object({
  categories: z.array(z.string()).optional().describe("Categories (OST_*) to check. Defaults to common types."),
  viewId: z.number().optional().describe("View ID to check (defaults to active view)"),
  limit: z.number().int().optional().default(500).describe("Max results. Default: 500"),
});

export const FindUndimensionedElementsInput = z.object({
  categories: z.array(z.string()).optional().describe("Categories (OST_*) to check. Defaults to structural/arch types."),
  viewId: z.number().optional().describe("View ID to check (defaults to active view)"),
  limit: z.number().int().optional().default(500).describe("Max results. Default: 500"),
});

export const ExportElementsDataInput = z.object({
  categories: z.array(z.string()).optional().describe("Categories (OST_*) to export"),
  parameterNames: z.array(z.string()).optional().describe("Columns to export (omit for auto-discover)"),
  includeTypeParameters: z.boolean().optional().default(false).describe("Include type parameters"),
  includeElementId: z.boolean().optional().default(true).describe("Include ElementId column"),
  outputFormat: z.enum(["json", "csv"]).optional().default("json").describe("Output format"),
  maxElements: z.number().int().optional().default(100).describe("Max elements. Default: 100"),
  filterParameterName: z.string().optional().describe("Filter by parameter name"),
  filterValue: z.string().optional().describe("Filter value"),
  filterOperator: z.enum(["equals", "contains", "greater_than", "less_than", "not_equals"]).optional().describe("Filter operator"),
});

export const MatchElementPropertiesInput = z.object({
  sourceElementId: z.number().describe("Element to copy properties from"),
  targetElementIds: z.array(z.number()).min(1).describe("Elements to copy properties to"),
  parameterNames: z.array(z.string()).optional().describe("Specific parameters (omit for all writable)"),
  includeTypeParameters: z.boolean().optional().default(false).describe("Also copy type parameters"),
});

const LineSegment = z.object({
  p0: PointSchema.describe("Start point in mm"),
  p1: PointSchema.describe("End point in mm"),
});

export const CreateLineBasedElementInput = z.object({
  data: z.array(z.object({
    category: z.string().describe("BuiltInCategory (OST_Walls, OST_StructuralFraming, etc.)"),
    typeId: z.number().optional().describe("Family/type ID"),
    locationLine: z.object({
      p0: PointSchema.describe("Start point in mm"),
      p1: PointSchema.describe("End point in mm"),
    }).describe("Line geometry"),
    height: z.number().optional().describe("Height in mm"),
    baseLevel: z.number().optional().describe("Base level elevation in mm"),
    baseOffset: z.number().optional().default(0).describe("Offset from level in mm"),
  })).min(1),
});

export const CreatePointBasedElementInput = z.object({
  data: z.array(z.object({
    typeId: z.number().describe("Family symbol ID"),
    locationPoint: PointSchema.describe("Placement point in mm"),
    baseLevel: z.number().optional().describe("Base level elevation in mm"),
    rotation: z.number().optional().describe("Rotation in degrees (Z-axis)"),
    hostWallId: z.number().optional().describe("Host wall ID for doors/windows"),
    facingFlipped: z.boolean().optional().describe("Flip door/window facing"),
  })).min(1),
});

export const CreateSurfaceBasedElementInput = z.object({
  data: z.array(z.object({
    category: z.enum(["OST_Floors", "OST_Ceilings", "OST_Roofs"]).describe("Surface category"),
    typeId: z.number().optional().describe("Type ID"),
    boundary: z.object({
      outerLoop: z.array(LineSegment).min(3).describe("Boundary line segments (min 3)"),
    }),
    baseLevel: z.number().optional().describe("Base level elevation in mm"),
    baseOffset: z.number().optional().default(0).describe("Offset from level in mm"),
  })).min(1),
});

export const SetElementPhaseInput = z.object({
  requests: z.array(z.object({
    elementId: z.number().describe("Element ID"),
    createdPhaseId: z.number().optional().describe("Phase ID for creation phase"),
    demolishedPhaseId: z.number().optional().describe("Phase ID for demolition phase"),
  })).min(1),
});

export const SetElementWorksetInput = z.object({
  requests: z.array(z.object({
    elementId: z.number().describe("Element ID"),
    worksetName: z.string().describe("Target workset name"),
  })).min(1),
});

export const ColorElementsInput = z.object({
  categoryName: z.string().describe("Category name (OST_* or display name)"),
  parameterName: z.string().describe("Parameter to group by for coloring"),
  useGradient: z.boolean().optional().default(false).describe("Use gradient (blue→red) instead of random colors"),
  customColors: z.array(z.object({
    r: z.number().min(0).max(255),
    g: z.number().min(0).max(255),
    b: z.number().min(0).max(255),
  })).optional().describe("Custom RGB colors per group"),
});

import { z } from "zod";

const PointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().describe("Z coordinate in mm"),
});

export const CreateDimensionsInput = z.object({
  dimensions: z
    .array(
      z.object({
        startPoint: PointSchema.optional().describe(
          "Start point in mm (for point-to-point dimensioning)"
        ),
        endPoint: PointSchema.optional().describe(
          "End point in mm (for point-to-point dimensioning)"
        ),
        linePoint: PointSchema.optional().describe(
          "Location of the dimension line in mm"
        ),
        elementIds: z
          .array(z.number())
          .optional()
          .describe("Element IDs to dimension between (2+)"),
        dimensionStyleId: z
          .number()
          .optional()
          .describe("Dimension type ID (-1 for default)"),
        viewId: z
          .number()
          .optional()
          .describe("Target view ID (-1 for active view)"),
      })
    )
    .min(1)
    .describe("Array of dimension specifications"),
});

export const CreateTextNoteInput = z.object({
  textNotes: z
    .array(
      z.object({
        text: z.string().describe("Text content"),
        position: PointSchema.describe("Position in mm"),
        viewId: z.number().optional().describe("Target view ID (default: active view)"),
        textNoteTypeId: z.number().optional().describe("TextNoteType ID"),
        horizontalAlignment: z
          .enum(["Left", "Center", "Right"])
          .optional()
          .default("Left")
          .describe("Text alignment"),
        width: z
          .number()
          .optional()
          .describe("Text width in mm (0 = auto)"),
      })
    )
    .min(1)
    .describe("Array of text note specifications"),
});

export const CreateColorLegendInput = z.object({
  parameterName: z.string().describe("Parameter name to group elements by"),
  categories: z
    .array(z.string())
    .optional()
    .describe("Categories to include (OST_* or display name)"),
  colorScheme: z
    .enum(["auto", "gradient", "custom"])
    .optional()
    .default("auto")
    .describe("Color scheme type"),
  customColors: z
    .array(
      z.object({
        value: z.string().describe("Parameter value to match"),
        r: z.number().min(0).max(255),
        g: z.number().min(0).max(255),
        b: z.number().min(0).max(255),
      })
    )
    .optional()
    .describe("Custom color assignments (for 'custom' scheme)"),
  createLegendView: z
    .boolean()
    .optional()
    .default(true)
    .describe("Create a drafting legend view"),
  legendTitle: z
    .string()
    .optional()
    .default("Color Legend")
    .describe("Legend view name"),
  targetViewId: z
    .number()
    .optional()
    .describe("Target view ID (0 for active view)"),
});

export const ImportTableInput = z.object({
  filePath: z.string().describe("Absolute path to CSV or TSV file"),
  delimiter: z
    .enum([",", ";", "\\t"])
    .optional()
    .default(",")
    .describe("Column delimiter"),
  viewType: z
    .enum(["drafting", "legend"])
    .optional()
    .default("drafting")
    .describe("View type to create"),
  viewName: z
    .string()
    .optional()
    .describe("Custom view name (auto-generated if omitted)"),
  textSize: z
    .number()
    .optional()
    .default(2.0)
    .describe("Text size in mm"),
  includeHeaders: z
    .boolean()
    .optional()
    .default(true)
    .describe("Treat first row as headers with bold styling"),
});

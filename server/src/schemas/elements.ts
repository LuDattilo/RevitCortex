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

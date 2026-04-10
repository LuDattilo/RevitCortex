import { z } from "zod";

export const GetProjectInfoInput = z.object({
  includePhases: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include project phases. Default: true"),
  includeWorksets: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include workset information. Default: true"),
  includeLinks: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include Revit link information. Default: true"),
  includeLevels: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include level information. Default: true"),
});

export const GetPhasesInput = z.object({
  includePhaseFilters: z
    .boolean()
    .optional()
    .default(true)
    .describe("Include phase filters in addition to phases. Default: true"),
});

export const GetWorksetsInput = z.object({
  includeSystemWorksets: z
    .boolean()
    .optional()
    .default(false)
    .describe("Include system worksets. Default: false"),
});

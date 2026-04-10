import { z } from "zod";

export const AddSharedParameterInput = z.object({
  parameterName: z.string().describe("Name of the shared parameter"),
  groupName: z
    .string()
    .optional()
    .default("RevitCortex")
    .describe("Group name in shared parameter file"),
  categories: z
    .array(z.string())
    .min(1)
    .describe("Categories to bind to (OST_* or display name)"),
  isInstance: z
    .boolean()
    .optional()
    .default(true)
    .describe("Instance (true) or type (false) binding"),
});

export const ManageProjectParametersInput = z.object({
  action: z
    .enum(["list", "create", "delete", "modify"])
    .describe("Action to perform"),
  parameterName: z
    .string()
    .optional()
    .describe("Parameter name (required for create/delete/modify)"),
  dataType: z
    .enum([
      "Text",
      "Integer",
      "Number",
      "Length",
      "Area",
      "Volume",
      "Angle",
      "YesNo",
      "URL",
    ])
    .optional()
    .default("Text")
    .describe("Data type (for create)"),
  isInstance: z
    .boolean()
    .optional()
    .default(true)
    .describe("Instance (true) or type (false) binding"),
  categories: z
    .array(z.string())
    .optional()
    .describe("Categories to bind (for create/modify)"),
});

export const AddPrefixSuffixInput = z.object({
  parameterName: z.string().describe("Parameter name to modify"),
  prefix: z.string().optional().describe("Prefix to add"),
  suffix: z.string().optional().describe("Suffix to add"),
  separator: z
    .string()
    .optional()
    .default("")
    .describe("Separator between prefix/value/suffix"),
  categories: z
    .array(z.string())
    .optional()
    .describe("Categories to filter (OST_* or display name)"),
  scope: z
    .enum(["whole_model", "active_view", "selection"])
    .optional()
    .default("whole_model")
    .describe("Element scope"),
  skipEmpty: z
    .boolean()
    .optional()
    .default(true)
    .describe("Skip elements with empty parameter values"),
  filterValue: z
    .string()
    .optional()
    .describe("Only modify elements with this specific value"),
  dryRun: z
    .boolean()
    .optional()
    .default(true)
    .describe("Preview mode — true = show preview without modifying. Default: true"),
});

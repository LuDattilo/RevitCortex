import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const FilterByParameterValueInput = z.object({
  categories: z
    .array(z.string())
    .optional()
    .describe("Revit categories to filter, e.g. ['OST_Walls', 'OST_Doors']. Omit for all categories."),
  parameterName: z
    .string()
    .describe("Parameter name to filter on"),
  condition: z.enum([
    "equals", "not_equals", "contains", "not_contains",
    "begins_with", "not_begins_with", "ends_with", "not_ends_with",
    "greater_than", "less_than", "is_empty", "is_not_empty",
  ]).describe("Comparison condition"),
  value: z
    .string()
    .optional()
    .describe("Value to compare against. Not required for is_empty/is_not_empty."),
  caseSensitive: z
    .boolean()
    .optional()
    .default(false)
    .describe("Case-sensitive comparison. Default: false"),
  scope: z
    .enum(["whole_model", "active_view", "selection"])
    .optional()
    .default("whole_model")
    .describe("Search scope. Default: whole_model"),
  parameterType: z
    .enum(["instance", "type", "both"])
    .optional()
    .default("both")
    .describe("Check instance, type, or both parameters. Default: both"),
  returnParameters: z
    .array(z.string())
    .optional()
    .describe("Additional parameter names to return for each matched element"),
});

export function registerFilterByParameterValueTool(server: McpServer): void {
  server.tool(
    "filter_by_parameter_value",
    "Filter elements by parameter value conditions. Supports equals, contains, greater_than, etc. with scope filtering (whole model, active view, selection).",
    FilterByParameterValueInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("filter_by_parameter_value", args);
        });
        return toolResponse("filter_by_parameter_value", result, Date.now() - start, args);
      } catch (error) {
        return toolError("filter_by_parameter_value", error, Date.now() - start);
      }
    }
  );
}

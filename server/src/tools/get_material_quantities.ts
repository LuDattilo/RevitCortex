import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const GetMaterialQuantitiesInput = z.object({
  categoryFilters: z
    .array(z.string())
    .optional()
    .describe("Category names to filter (e.g. ['OST_Walls', 'OST_Floors'])"),
  selectedElementsOnly: z
    .boolean()
    .optional()
    .default(false)
    .describe("Only analyze currently selected elements. Default: false"),
  maxResults: z
    .number()
    .int()
    .optional()
    .default(50)
    .describe("Max materials to return. Default: 50"),
});

export function registerGetMaterialQuantitiesTool(server: McpServer): void {
  server.tool(
    "get_material_quantities",
    "Calculate total area and volume of materials across elements. Useful for material takeoffs and cost estimation. Heavy query on large models.",
    GetMaterialQuantitiesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_material_quantities", args);
        });
        logToolCall({ tool: "get_material_quantities", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_material_quantities", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const GetAvailableFamilyTypesInput = z.object({
  categoryList: z
    .array(z.string())
    .optional()
    .describe("Category codes to filter (e.g. ['OST_Walls', 'OST_Doors'])"),
  familyNameFilter: z
    .string()
    .optional()
    .describe("Filter by family/type name (partial match, case-insensitive)"),
  limit: z
    .number()
    .int()
    .optional()
    .default(100)
    .describe("Max types to return. Default: 100"),
});

export function registerGetAvailableFamilyTypesTool(server: McpServer): void {
  server.tool(
    "get_available_family_types",
    "List available family types (loadable and system). Call before creating elements to get exact family/type names and IDs.",
    GetAvailableFamilyTypesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_available_family_types", args);
        });
        logToolCall({ tool: "get_available_family_types", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_available_family_types", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

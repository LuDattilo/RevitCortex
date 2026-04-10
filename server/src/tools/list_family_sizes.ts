import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

const ListFamilySizesInput = z.object({
  limit: z
    .number()
    .int()
    .optional()
    .default(50)
    .describe("Max families to return. Default: 50"),
  sortBy: z
    .enum(["instanceCount", "typeCount", "name"])
    .optional()
    .default("instanceCount")
    .describe("Sort order. Default: instanceCount"),
  categories: z
    .array(z.string())
    .optional()
    .describe("Filter by category codes (e.g. ['OST_Doors', 'OST_Windows'])"),
});

export function registerListFamilySizesTool(server: McpServer): void {
  server.tool(
    "list_family_sizes",
    "List families with instance/type counts. Helps identify bloated, unused, or in-place families.",
    ListFamilySizesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("list_family_sizes", args);
        });
        logToolCall({ tool: "list_family_sizes", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "list_family_sizes", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

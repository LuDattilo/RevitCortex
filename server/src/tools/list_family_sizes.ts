import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

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
        return toolResponse("list_family_sizes", result, Date.now() - start, args);
      } catch (error) {
        return toolError("list_family_sizes", error, Date.now() - start);
      }
    }
  );
}

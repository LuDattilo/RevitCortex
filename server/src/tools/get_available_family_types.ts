import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

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
        return toolResponse("get_available_family_types", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_available_family_types", error, Date.now() - start);
      }
    }
  );
}

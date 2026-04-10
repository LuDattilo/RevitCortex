import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const GetMaterialsInput = z.object({
  materialClass: z
    .string()
    .optional()
    .describe("Filter by material class (case-insensitive exact match)"),
  nameFilter: z
    .string()
    .optional()
    .describe("Filter materials whose name contains this substring (case-insensitive)"),
});

export function registerGetMaterialsTool(server: McpServer): void {
  server.tool(
    "get_materials",
    "List materials in the project, optionally filtered by class or name.",
    GetMaterialsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_materials", args);
        });
        return toolResponse("get_materials", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_materials", error, Date.now() - start);
      }
    }
  );
}

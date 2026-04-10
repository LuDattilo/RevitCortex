import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const GetSharedParametersInput = z.object({
  categoryFilter: z
    .string()
    .optional()
    .describe("Filter by category name (case-insensitive substring match)"),
});

export function registerGetSharedParametersTool(server: McpServer): void {
  server.tool(
    "get_shared_parameters",
    "List all project parameters (shared and project-specific) with bindings and applicable categories.",
    GetSharedParametersInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_shared_parameters", args);
        });
        return toolResponse("get_shared_parameters", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_shared_parameters", error, Date.now() - start);
      }
    }
  );
}

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

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
        logToolCall({ tool: "get_materials", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_materials", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

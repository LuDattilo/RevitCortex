import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

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
        logToolCall({ tool: "get_shared_parameters", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_shared_parameters", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

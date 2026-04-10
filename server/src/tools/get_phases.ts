import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetPhasesInput } from "../schemas/project.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerGetPhasesTool(server: McpServer): void {
  server.tool(
    "get_phases",
    "List all phases in the project with their sequence order and optionally phase filters.",
    GetPhasesInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_phases", args);
        });
        logToolCall({ tool: "get_phases", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "get_phases", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

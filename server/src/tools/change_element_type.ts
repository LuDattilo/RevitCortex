import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ChangeElementTypeInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerChangeElementTypeTool(server: McpServer): void {
  server.tool(
    "change_element_type",
    "Change family type of elements by type ID or name",
    ChangeElementTypeInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("change_element_type", args);
        });
        logToolCall({ tool: "change_element_type", success: true, durationMs: Date.now() - start });
        return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
      } catch (error) {
        logToolCall({ tool: "change_element_type", success: false, durationMs: Date.now() - start });
        return {
          content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }],
          isError: true,
        };
      }
    }
  );
}

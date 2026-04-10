import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DeleteSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDeleteSelectionTool(server: McpServer): void {
  server.tool("delete_selection", "Delete a saved selection filter", DeleteSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("delete_selection", args);
      });
      logToolCall({ tool: "delete_selection", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "delete_selection", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

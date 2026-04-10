import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { LoadSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerLoadSelectionTool(server: McpServer): void {
  server.tool("load_selection", "List or load saved selections", LoadSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("load_selection", args);
      });
      logToolCall({ tool: "load_selection", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "load_selection", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

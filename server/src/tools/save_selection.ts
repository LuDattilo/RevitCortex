import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SaveSelectionInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerSaveSelectionTool(server: McpServer): void {
  server.tool("save_selection", "Save element selection as named filter", SaveSelectionInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("save_selection", args);
      });
      logToolCall({ tool: "save_selection", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "save_selection", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

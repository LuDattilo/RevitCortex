import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { DuplicateViewInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerDuplicateViewTool(server: McpServer): void {
  server.tool("duplicate_view", "Duplicate views with configurable options", DuplicateViewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("duplicate_view", args);
      });
      logToolCall({ tool: "duplicate_view", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "duplicate_view", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

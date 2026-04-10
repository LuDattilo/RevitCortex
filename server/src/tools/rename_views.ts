import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { RenameViewsInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerRenameViewsTool(server: McpServer): void {
  server.tool("rename_views", "Rename views with find/replace, prefix, or suffix", RenameViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("rename_views", args);
      });
      logToolCall({ tool: "rename_views", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "rename_views", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

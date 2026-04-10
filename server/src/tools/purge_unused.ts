import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { PurgeUnusedInput } from "../schemas/audit.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerPurgeUnusedTool(server: McpServer): void {
  server.tool("purge_unused", "Find and remove unused families, types, and materials", PurgeUnusedInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("purge_unused", args);
      });
      logToolCall({ tool: "purge_unused", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "purge_unused", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

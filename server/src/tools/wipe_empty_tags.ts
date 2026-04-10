import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { WipeEmptyTagsInput } from "../schemas/bulk-operations.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerWipeEmptyTagsTool(server: McpServer): void {
  server.tool("wipe_empty_tags", "Find and remove empty or orphaned tags", WipeEmptyTagsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("wipe_empty_tags", args);
      });
      logToolCall({ tool: "wipe_empty_tags", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "wipe_empty_tags", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

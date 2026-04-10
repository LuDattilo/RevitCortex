import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { TagRoomsInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerTagRoomsTool(server: McpServer): void {
  server.tool("tag_rooms", "Tag rooms in the current view", TagRoomsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("tag_rooms", args);
      });
      logToolCall({ tool: "tag_rooms", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "tag_rooms", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

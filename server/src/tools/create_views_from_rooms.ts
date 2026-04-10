import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewsFromRoomsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateViewsFromRoomsTool(server: McpServer): void {
  server.tool("create_views_from_rooms", "Create callout, section, or elevation views from rooms", CreateViewsFromRoomsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_views_from_rooms", args);
      });
      logToolCall({ tool: "create_views_from_rooms", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_views_from_rooms", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

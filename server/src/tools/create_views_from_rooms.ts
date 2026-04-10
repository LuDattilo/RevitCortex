import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewsFromRoomsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateViewsFromRoomsTool(server: McpServer): void {
  server.tool("create_views_from_rooms", "Create callout, section, or elevation views from rooms", CreateViewsFromRoomsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_views_from_rooms", args);
      });
      return toolResponse("create_views_from_rooms", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_views_from_rooms", error, Date.now() - start);
    }
  });
}

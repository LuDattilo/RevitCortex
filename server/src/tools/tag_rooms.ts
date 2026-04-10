import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { TagRoomsInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerTagRoomsTool(server: McpServer): void {
  server.tool("tag_rooms", "Tag rooms in the current view", TagRoomsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("tag_rooms", args);
      });
      return toolResponse("tag_rooms", result, Date.now() - start, args);
    } catch (error) {
      return toolError("tag_rooms", error, Date.now() - start);
    }
  });
}

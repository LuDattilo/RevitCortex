import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateRoomInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateRoomTool(server: McpServer): void {
  server.tool("create_room", "Create a room at specified location", CreateRoomInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_room", args);
      });
      return toolResponse("create_room", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_room", error, Date.now() - start);
    }
  });
}

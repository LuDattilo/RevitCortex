import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateFloorInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateFloorTool(server: McpServer): void {
  server.tool("create_floor", "Create a floor from boundary points or room boundary", CreateFloorInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_floor", args);
      });
      return toolResponse("create_floor", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_floor", error, Date.now() - start);
    }
  });
}

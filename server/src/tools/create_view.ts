import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateViewTool(server: McpServer): void {
  server.tool("create_view", "Create a new floor plan, ceiling plan, section, or 3D view", CreateViewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_view", args);
      });
      return toolResponse("create_view", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_view", error, Date.now() - start);
    }
  });
}

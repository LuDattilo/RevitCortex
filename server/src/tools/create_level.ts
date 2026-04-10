import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateLevelInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateLevelTool(server: McpServer): void {
  server.tool("create_level", "Create a new level with optional floor/ceiling plans", CreateLevelInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_level", args);
      });
      return toolResponse("create_level", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_level", error, Date.now() - start);
    }
  });
}

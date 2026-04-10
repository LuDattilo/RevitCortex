import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { TagWallsInput } from "../schemas/creation.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerTagWallsTool(server: McpServer): void {
  server.tool("tag_walls", "Tag walls at their midpoints", TagWallsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("tag_walls", args);
      });
      return toolResponse("tag_walls", result, Date.now() - start, args);
    } catch (error) {
      return toolError("tag_walls", error, Date.now() - start);
    }
  });
}

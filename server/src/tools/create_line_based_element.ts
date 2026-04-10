import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateLineBasedElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateLineBasedElementTool(server: McpServer): void {
  server.tool("create_line_based_element", "Create walls, beams, and line-based elements from coordinates in mm", CreateLineBasedElementInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_line_based_element", args);
      });
      return toolResponse("create_line_based_element", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_line_based_element", error, Date.now() - start);
    }
  });
}

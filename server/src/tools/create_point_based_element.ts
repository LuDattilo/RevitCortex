import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreatePointBasedElementInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreatePointBasedElementTool(server: McpServer): void {
  server.tool("create_point_based_element", "Place point-based family instances (furniture, fixtures, doors, windows)", CreatePointBasedElementInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_point_based_element", args);
      });
      return toolResponse("create_point_based_element", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_point_based_element", error, Date.now() - start);
    }
  });
}

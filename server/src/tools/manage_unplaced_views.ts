import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageUnplacedViewsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerManageUnplacedViewsTool(server: McpServer): void {
  server.tool("manage_unplaced_views", "List or delete views that are not placed on any sheet", ManageUnplacedViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_unplaced_views", args);
      });
      return toolResponse("manage_unplaced_views", result, Date.now() - start, args);
    } catch (error) {
      return toolError("manage_unplaced_views", error, Date.now() - start);
    }
  });
}

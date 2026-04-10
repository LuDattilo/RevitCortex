import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewFilterInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerCreateViewFilterTool(server: McpServer): void {
  server.tool("create_view_filter", "Create, apply, or list parameter-based view filters", CreateViewFilterInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_view_filter", args);
      });
      return toolResponse("create_view_filter", result, Date.now() - start, args);
    } catch (error) {
      return toolError("create_view_filter", error, Date.now() - start);
    }
  });
}

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetCurrentViewInfoTool(server: McpServer): void {
  server.tool(
    "get_current_view_info",
    "Get metadata about the currently active view: name, type, scale, detail level.",
    {},
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_current_view_info", args);
        });
        return toolResponse("get_current_view_info", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_current_view_info", error, Date.now() - start);
      }
    }
  );
}

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { GetCurrentViewElementsInput } from "../schemas/elements.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerGetCurrentViewElementsTool(server: McpServer): void {
  server.tool(
    "get_current_view_elements",
    "Get elements from active view with category/field filtering",
    GetCurrentViewElementsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_current_view_elements", args);
        });
        return toolResponse("get_current_view_elements", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_current_view_elements", error, Date.now() - start);
      }
    }
  );
}

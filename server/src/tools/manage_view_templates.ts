import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageViewTemplatesInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

export function registerManageViewTemplatesTool(server: McpServer): void {
  server.tool("manage_view_templates", "List, duplicate, delete, or rename view templates", ManageViewTemplatesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_view_templates", args);
      });
      return toolResponse("manage_view_templates", result, Date.now() - start, args);
    } catch (error) {
      return toolError("manage_view_templates", error, Date.now() - start);
    }
  });
}

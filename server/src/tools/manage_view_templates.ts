import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageViewTemplatesInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerManageViewTemplatesTool(server: McpServer): void {
  server.tool("manage_view_templates", "List, duplicate, delete, or rename view templates", ManageViewTemplatesInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_view_templates", args);
      });
      logToolCall({ tool: "manage_view_templates", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "manage_view_templates", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

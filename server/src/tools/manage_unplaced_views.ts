import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { ManageUnplacedViewsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerManageUnplacedViewsTool(server: McpServer): void {
  server.tool("manage_unplaced_views", "List or delete views that are not placed on any sheet", ManageUnplacedViewsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("manage_unplaced_views", args);
      });
      logToolCall({ tool: "manage_unplaced_views", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "manage_unplaced_views", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

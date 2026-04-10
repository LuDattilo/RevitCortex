import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewFilterInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateViewFilterTool(server: McpServer): void {
  server.tool("create_view_filter", "Create, apply, or list parameter-based view filters", CreateViewFilterInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_view_filter", args);
      });
      logToolCall({ tool: "create_view_filter", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_view_filter", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

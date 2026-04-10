import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { CreateViewInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerCreateViewTool(server: McpServer): void {
  server.tool("create_view", "Create a new floor plan, ceiling plan, section, or 3D view", CreateViewInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("create_view", args);
      });
      logToolCall({ tool: "create_view", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "create_view", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}

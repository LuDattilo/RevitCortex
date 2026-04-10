import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { OverrideGraphicsInput } from "../schemas/views.js";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { logToolCall } from "../logging/logger.js";

export function registerOverrideGraphicsTool(server: McpServer): void {
  server.tool("override_graphics", "Set or reset graphic overrides for elements in a view", OverrideGraphicsInput.shape, async (args) => {
    const start = Date.now();
    try {
      const result = await withRevitConnection(async (client) => {
        return await client.sendCommand("override_graphics", args);
      });
      logToolCall({ tool: "override_graphics", success: true, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: JSON.stringify(result, null, 2) }] };
    } catch (error) {
      logToolCall({ tool: "override_graphics", success: false, durationMs: Date.now() - start });
      return { content: [{ type: "text" as const, text: `Error: ${error instanceof Error ? error.message : String(error)}` }], isError: true };
    }
  });
}
